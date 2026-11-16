# Gusto Demo Time Sync

**Status:** Approved for implementation planning (2026-11-14)
**Depends on:** Weekly timesheet (`GET /api/time/weekly`), company timezone, manager roles

## Problem

VisionPaint already stores closed shifts and work minutes after unpaid breaks. A painting company still needs a payroll provider to withhold taxes and pay employees by direct deposit. Stripe can move money, and it does not run payroll.

## Goal

A manager sends one worker's closed shifts for a Sunday–Saturday week to a Gusto demo company. Gusto remains the payroll system. VisionPaint does not calculate tax, store a Social Security number, store a bank account, or store a pay rate.

After a successful send, the manager opens Gusto's Time Tracking page and uses Gusto's own "Sync hours to payroll" action.

## Decisions

| Decision | Choice |
|----------|--------|
| Integration | Gusto time-tracking API against `https://api.gusto-demo.com` |
| What is sent | One Gusto time sheet per closed `time_entry` |
| Hours | Work minutes ÷ 60, rounded half away from zero to 4 decimal places, `pay_classification` `Regular` |
| Overtime | Not classified in this version |
| Payroll run | Not called. No `payroll_syncs` request and no Gusto webhooks |
| Employee link | Seeded `gusto_employee_link` rows. No linking screen |
| Already accepted | A `synced` shift is not sent again, including after a later edit in VisionPaint |
| Who can send | `owner`, `admin`, `manager` |
| Secrets | Company uuid, client id, client secret, and refresh token in configuration. Access tokens in memory only |

## Boundary

VisionPaint owns clock-in, clock-out, unpaid breaks, work minutes, the Sunday week in the company timezone, the person-to-Gusto mapping, and the sync status of each shift.

Gusto owns the employee, the compensation job, the pay rate, tax, the bank account, the pay stub, and direct deposit. The compensation job is the Gusto job uuid stored on the link. It is a different record from a VisionPaint painting job.

The configured base URL must be `https://api.gusto-demo.com`. Any other host is treated as missing configuration.

## Data

### `gusto_employee_link`

One row per person.

| Column | Notes |
|--------|--------|
| `person_id` | Primary key, references `person` |
| `company_id` | References `company` |
| `gusto_employee_uuid` | Required |
| `gusto_job_uuid` | Gusto compensation job, required |
| `created_at`, `updated_at` | `timestamptz` |

### `gusto_time_sheet_sync`

One row per `time_entry` that a send has attempted.

| Column | Notes |
|--------|--------|
| `time_entry_id` | Primary key, references `time_entry` on delete cascade |
| `gusto_time_sheet_uuid` | Nullable |
| `status` | `pending`, `synced`, or `failed` |
| `hours_worked` | Numeric, 4 decimal places |
| `error_message` | Nullable |
| `created_at`, `updated_at` | `timestamptz` |

`pending` is written before the Gusto post. `synced` is written after Gusto accepts the sheet. The unique `time_entry_id` stops a second successful send.

Configuration keys, not columns:

- `Gusto:BaseUrl`
- `Gusto:ClientId`
- `Gusto:ClientSecret`
- `Gusto:RefreshToken`
- `Gusto:CompanyUuid`
- `Gusto:ApiVersion` = `2026-06-15`

Demo link rows are inserted for the demo crew by the developer. The migration creates the tables and does not insert those rows. Tests insert the rows they need.

## API

### `POST /api/time/weekly/gusto-sync`

JSON body:

```json
{
  "weekStart": "2026-10-18",
  "personId": 12
}
```

`weekStart` uses the weekly timesheet rule: a Sunday, interpreted in the company timezone. The caller must be `owner`, `admin`, or `manager`, and the person must be an active member of the caller's company.

Result:

```json
{
  "sent": 1,
  "alreadySynced": 0,
  "skippedOpen": 1,
  "skippedZero": 0,
  "failed": [
    { "timeEntryId": 44, "message": "Gusto message" }
  ]
}
```

### Send steps

1. Reject the request before any sync row is written when the caller, week, person, link, or Gusto configuration is unusable. See Errors.
2. Load the person's shifts for that week with the same timezone filter as `GET /api/time/weekly`.
3. Skip an open shift (`clock_out_at` is null). Count it as `skippedOpen`. Write no row.
4. Skip a closed shift whose work minutes are 0. Count it as `skippedZero`. Write no row.
5. Skip a shift whose sync row is `synced`. Count it as `alreadySynced`.
6. For each remaining shift, write a `pending` row, or set an existing `failed` or `pending` row back to `pending`, then `POST /v1/companies/{company_uuid}/time_tracking/time_sheets`.
7. On success, store the returned time sheet uuid and mark the row `synced`. On a per-shift failure, mark it `failed` and continue, except for the auth stop rule below.

`hours_worked` is the shift's work minutes divided by 60, rounded half away from zero to 4 decimal places. One minute is `0.0167`.

Each Gusto time sheet body:

- `entity_uuid`: the link's employee uuid
- `entity_type`: `Employee`
- `job_uuid`: the link's compensation job uuid
- `time_zone`: the company timezone id
- `shift_started_at`, `shift_ended_at`: the stored clock-in and clock-out
- `entries`: one entry, `pay_classification` `Regular`, `hours_worked` as above
- `metadata`: `visionpaint_time_entry_id` and `job_title`, both strings

Headers on every Gusto call: `Authorization: Bearer`, `X-Gusto-API-Version: 2026-06-15`. The company access token is refreshed with `POST /oauth/token` and `grant_type=refresh_token` when it is missing or fewer than 5 minutes remain before expiry. Tokens and the Authorization header are not logged. Logs may include the `time_entry` id and the Gusto status code.

An edit in VisionPaint after `synced` does not change Gusto. The row stays `synced`, and a later send counts that shift as `alreadySynced`.

### Weekly read

`WeeklyTimesheetSessionDto` gains `gustoSyncStatus`: `null`, `pending`, `synced`, or `failed`. `null` means no sync row. The field is present for every caller who can load the timesheet.

## Errors

The whole request stops, and no sync row is written, for:

| Condition | Status |
|-----------|--------|
| Caller is `crew` | 403 |
| `weekStart` is not a Sunday | 400 |
| Person is not an active member of the company | 404 |
| Person has no `gusto_employee_link` | 422, message says the worker is not linked to Gusto |
| Demo configuration is missing, the base URL is not the demo host, or token refresh fails before any shift is posted | 503, message says the Gusto demo is not configured or could not be reached |

A week with nothing to send is **200** with `sent: 0`.

During the batch, each shift is independent. Any Gusto response other than **401**, and any timeout, marks that row `failed`, stores the message, and the handler continues. The HTTP response is still **200**. Each failure lists the `time_entry` id and the message. The HTTP client timeout is 30 seconds per Gusto request.

**401** refreshes the token once and retries that same shift. If the refresh fails or the retry is also **401**, that shift is marked `failed` and the handler stops. Shifts not yet attempted get no row.

A later send skips `synced`, and posts `failed` and `pending` again. A `pending` row with no Gusto uuid means the process stopped after the row was written and before the uuid was saved. If Gusto had already accepted that post, the retry can create a second sheet. That window is accepted for this demo.

## UI

The button lives on the existing weekly timesheet (`WeeklyTimesheet.vue`), under the week header, for the worker and week already selected.

- Label: **Send week to Gusto**
- Visible only when the signed-in role is `owner`, `admin`, or `manager`
- Confirm with the existing `ConfirmDialog`: send that worker's closed shifts for that week to the Gusto demo, and leave shifts that were already sent alone
- Disabled while the request runs
- The result line under the header shows sent, already synced, skipped open, skipped zero, and failed. Each failure lists the shift and the message. The line clears when the worker or the week changes

On each session, a manager sees **Sent to Gusto** or **Gusto failed** from `gustoSyncStatus`. Crew members do not see the button or those labels. A shift edited after a successful send still shows **Sent to Gusto**.

There is no screen for pasting Gusto ids. A 422 for an unlinked worker is shown on the timesheet, and nothing is sent.

## Testing

Backend integration tests use the existing `WebApplicationFactory` and Postgres fixture. A stub HTTP handler replaces Gusto. The suite never calls `api.gusto-demo.com`. The stub records time-sheet posts and token requests, and each test chooses the status Gusto returns.

- A manager sends two closed shifts. The stub receives two posts with `Regular`, the rounded hours, the company timezone, and metadata for the `time_entry` id and painting job title. Both rows are `synced`. The response counts `sent: 2`.
- A second send posts nothing and counts `alreadySynced: 2`. Editing a sent shift and sending again still posts nothing.
- An open shift and a closed shift with zero work minutes create no rows and count as `skippedOpen` and `skippedZero`.
- A crew member receives **403**. An unlinked worker receives **422**. The stub sees no time-sheet post.
- Missing demo configuration returns **503** before any row is written.
- One shift gets **422** and the next gets **201**. The response is **200**, with one `synced` row and one `failed` row storing Gusto's message. The weekly timesheet returns `gustoSyncStatus` for both.
- A **401** triggers one token refresh, then a successful retry of that shift.
- A second **401** marks that shift `failed` and leaves every later shift with no row.

During implementation, the timesheet button gets one manual pass: a manager confirms a send, sees the counts, and sees **Sent to Gusto** on a session. A crew member does not see the button or the label.

## Docs

When this is implemented, check the payroll revisit box on Q2 in `docs/design/stakeholder-decisions.md` and point it at this spec.

## Out of scope

Overtime and double overtime, calling Gusto's payroll sync, webhooks, creating Gusto employees, a linking screen, live Gusto, and updating a sheet that Gusto has already accepted.
