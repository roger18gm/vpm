# Gusto Employee Link

**Status:** Approved for implementation planning (2026-11-20)
**Depends on:** [Gusto demo time sync](2026-10-24-gusto-time-sync-design.md), Users admin (`/users`, owner and admin)

## Problem

`POST /api/time/weekly/gusto-sync` sends a worker's closed shifts only when that person has a `gusto_employee_link` row. The migration created the table and left it empty. Gusto configuration (demo host, client, company uuid) does not create those rows. An unlinked worker returns 422 and nothing is sent.

## Goal

An owner or admin links each active company member to one onboarded, active Gusto demo employee, and to that employee's compensation job, from the Users page. After the row exists, **Send week to Gusto** uses it. VisionPaint still does not calculate tax or store a Social Security number, a bank account, or a pay rate.

## Decisions

| Decision | Choice |
|----------|--------|
| Who links | `owner` and `admin` only, the same gate as `GET /api/users` |
| Where | A Gusto column on the existing Users table |
| Who can be linked | A company member whose membership status is `active`, any role |
| How the employee is chosen | The server loads the Gusto demo directory. The admin picks from that list |
| Compensation job | One job is shown as text and stored. Two or more jobs use a second dropdown. Zero jobs cannot be saved |
| Replace | The admin can save a different employee or job later. There is no unlink |
| Already sent shifts | `gusto_time_sheet_sync` stays as it is. Later sends use the link stored at send time |
| One employee, one user | Inside one VisionPaint company, one `gusto_employee_uuid` belongs to one person. The 409 names the person who already has it |
| Pay | Rate, payment unit, and compensation objects are dropped before the browser response is built |

This replaces the "seeded rows, no linking screen" decision in the time-sync spec. Pasting Gusto ids is still out of scope. Creating Gusto employees is still out of scope.

## Boundary

VisionPaint owns the person-to-Gusto mapping. Gusto owns the employee, the compensation job, and the pay rate.

The browser never calls Gusto. The server calls only `https://api.gusto-demo.com` with `X-Gusto-API-Version: 2026-06-15`, using the token refresh the time sync already uses. Any other host is missing configuration. Tokens stay in memory. Logs must not include the token, the Authorization header, or a Gusto response body.

`gusto_job_uuid` is the Gusto compensation job. It is a different record from a VisionPaint painting job.

## Data

No new columns. `gusto_employee_link` stays:

| Column | Notes |
|--------|--------|
| `person_id` | Primary key, references `person` |
| `company_id` | References `company` |
| `gusto_employee_uuid` | Required |
| `gusto_job_uuid` | Required |
| `created_at`, `updated_at` | `timestamptz` |

Add a unique index `gusto_employee_link_company_employee_uidx` on `(company_id, gusto_employee_uuid)`.

An update changes `gusto_employee_uuid`, `gusto_job_uuid`, and `updated_at`. `created_at` stays. A save of the same pair returns 200 and does not insert a second row.

## API

Both routes use the existing owner/admin check. A manager receives 403, `Only owners and admins can manage users.` The Gusto directory is not called for that 403.

### `GET /api/users/gusto-directory`

The handler refreshes the company token when it is missing or fewer than 5 minutes remain.

1. Page `GET /v1/companies/{company_uuid}/employees?onboarded_active=true&page={n}&per=100` until a page returns fewer than 100 employees.
2. For each employee uuid, page `GET /v1/employees/{employee_uuid}/jobs?page={n}&per=100` until a page returns fewer than 100 jobs.
3. Skip an object whose `uuid` is missing or blank. Read `first_name`, `last_name`, and `email` (missing strings become `""`). Read each job's `uuid`, `title` (missing becomes `""`), and `primary` (missing becomes false).
4. A non-array JSON body, invalid JSON, a Gusto status outside 200–299, a timeout, or a connection failure fails the whole directory. No partial list is returned.
5. A 401 refreshes the token once and retries that same call. A second 401 fails the directory.
6. More than 20 full pages of employees, or more than 20 full pages of jobs for one employee, fails the directory.
7. Sort employees by `first_name` + `last_name`, ordinal ignore case, then by uuid.

The HTTP timeout stays 30 seconds per Gusto request.

Result:

```json
{
  "employees": [
    {
      "uuid": "emp-1",
      "firstName": "Ada",
      "lastName": "Painter",
      "email": "ada@example.com",
      "jobs": [
        { "uuid": "job-1", "title": "Painter", "primary": true }
      ]
    }
  ],
  "links": [
    { "personId": 12, "gustoEmployeeUuid": "emp-1", "gustoJobUuid": "job-1" }
  ]
}
```

`links` is every `gusto_employee_link` row for the caller's VisionPaint company. An empty employee list is 200 with `employees: []`.

### `PUT /api/users/{personId}/gusto-link`

```json
{ "gustoEmployeeUuid": "emp-1", "gustoJobUuid": "job-1" }
```

The handler trims both uuids. The person must have `company_member.status = active` in the caller's company. It then loads the directory again and accepts the pair only when the employee uuid is in that directory and the job uuid is one of that employee's jobs. It inserts or updates the link for that `person_id` and the caller's `company_id`.

A repeat of the stored pair returns 200. Saving a new job for the same employee returns 200. Replacing the employee frees the previous employee uuid for someone else.

Success:

```json
{ "personId": 12, "gustoEmployeeUuid": "emp-1", "gustoJobUuid": "job-1" }
```

`person.IsActive` is not an extra gate. Membership status is the gate, matching who **Send week to Gusto** already accepts.

## Errors

Directory and save failures leave every stored link unchanged. The Users list still loads when the directory fails.

| Condition | Status | Message |
|-----------|--------|---------|
| Caller is not owner or admin | 403 | Only owners and admins can manage users. |
| Person is not an active member of the company | 404 | Person not found. |
| Either uuid is blank | 400 | Choose a Gusto employee and a compensation job. |
| Employee is not in the directory | 422 | That Gusto employee is not available to link. |
| Job is not one of that employee's jobs | 422 | That compensation job does not belong to the selected employee. |
| Another user in the company already has that employee | 409 | This Gusto employee is already linked to {name}. |
| Demo configuration is missing, the host is not the demo host, the token refresh fails, a Gusto call fails, or the page cap is hit | 503 | The Gusto demo is not configured or could not be reached. |

The 409 name is `person.name` of the user who already holds the uuid. The unique index is the backstop when two saves race. The response is still 409 with that name. If the winning row cannot be read, the name is **another user**.

**Send week to Gusto** is unchanged. An unlinked worker still gets 422 and nothing is posted. A linked worker sends with the employee and job stored at that moment.

## UI

The Gusto column is on `/users`, after Role. The page loads `GET /api/users` and `GET /api/users/gusto-directory` together. The table's one primary action stays **New user**. The row action is a secondary **Save**.

Until the directory returns, every Gusto cell reads **Loading Gusto…**.

A directory failure shows the 503 message once above the table. Those cells show an em dash and no dropdowns. The user rows remain.

A row gets controls only when `membershipStatus` is `active`. Any other membership shows an em dash.

The employee dropdown lists the directory as **First Last (email)**, in the server's order. A name with no email omits the parentheses. A missing name uses the email, and a missing name and email uses **Gusto employee**. An employee already linked to someone else stays in the list, with **linked to {name}** on that option, and the option is disabled. The row's current employee is selected when the directory still contains that uuid.

The job control depends on the chosen employee:

- One job shows that job's title as text, with **(primary)** when Gusto marks it primary. That job uuid is what Save stores.
- Two or more jobs show a second dropdown. Choosing an employee starts that dropdown on the primary job when there is one, and leaves it empty when there is not. Opening a row that already stores a job uuid keeps that stored job selected.
- No jobs shows **No compensation job**. Save stays disabled.

**Save** stays disabled while the selection matches the stored link, while a job uuid is missing, or while that row's request is running. A successful save shows **Saved** and leaves the selection in place. Changing the employee clears **Saved** and applies that employee's job rule again.

A stored employee who is no longer in the directory selects **Linked employee is no longer in Gusto**. The stored row remains until Save writes a new pair. An empty directory uses that same sentence on a row that already has a link, and **No Gusto employees are ready to link.** on a row that does not.

There is no clear control. A save error is shown under that cell. The dropdowns stay on the attempted choice. A 401 from VisionPaint still sends the user to login.

The table keeps horizontal scrolling. The Gusto column is not hidden on a narrow screen.

## Testing

Backend integration tests use the existing `WebApplicationFactory` and Postgres fixture. `StubGustoHandler` answers employee and job GETs. The suite never calls `api.gusto-demo.com`. Time-sheet posts keep their current stub behavior.

- Two links in one company with the same `gusto_employee_uuid` fail at the database.
- The directory request uses `onboarded_active=true`, `per=100`, the configured company uuid, the bearer token, and `X-Gusto-API-Version: 2026-06-15`. A 100-employee page is followed by the next page. Each employee gets a jobs request. A job payload that includes `rate` does not appear in the API response.
- A Gusto 401 on a directory call refreshes the token once and retries that call.
- A non-demo host, a failed token call, and a failed employee call throw before a partial directory is returned.
- An owner loads the directory and saves a link for an active crew member. The row stores `person_id`, `company_id`, and both uuids. Saving the same pair again returns 200 and leaves one row.
- A job uuid from another employee returns 422 and writes nothing. An employee missing from the directory returns 422 and writes nothing. Blank uuids return 400.
- A second user saving the same employee returns 409 and the message names the first user. The first row stays.
- Replacing the job or the employee updates that one row. An existing `gusto_time_sheet_sync` row is unchanged.
- A manager receives 403 on the directory and on the save. The stub sees no employee request.
- An inactive membership receives 404 and no row is written.
- Missing demo configuration returns 503 and does not request a token.

`npm run build` in `frontend` must pass. One manual pass: with the demo configured, an owner sees Gusto employees, saves a link, sees **Saved**, and **Send week to Gusto** no longer returns the unlinked 422 for that person. A manager does not see Users. With the demo unconfigured, Users still lists people and shows the 503 message.

## Docs

When this is implemented, point the Users row in `docs/design/screen-map.md` and a SCR-017 section in `docs/design/ui-spec.md` at these two routes. Point Q2 in `docs/design/stakeholder-decisions.md` at this spec. Update the time-sync spec so its employee-link decision points here.

## Out of scope

Creating Gusto employees, pasting uuids, unlinking, matching by email, live Gusto, overtime, payroll sync, webhooks, and updating a time sheet Gusto has already accepted.
