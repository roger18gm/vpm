# Job Area Progress (S2) Design

**Status:** Approved for implementation planning (2026-09-17)  
**Requirement:** S2 — room-by-room or area-by-area job progress tracking  
**Screen:** SCR-015 `/jobs/:id/areas`

## Problem

Managers track painting work as one job status. Crews actually finish rooms and surfaces independently (Kitchen vs Exterior Trim). There is no way to record that progress even though `job_area` already exists in the schema.

## Goal

A manager creates a flat list of named areas on a job and updates each area independently through `not_started`, `in_progress`, `completed`, or `blocked`. Assigned crew can view the same list. Job status and photos stay unchanged.

## Decisions

| Decision | Choice |
|----------|--------|
| Approach | Nested REST resource, same pattern as photos/assignments |
| Who creates/edits | Manager-only (`owner`, `admin`, `manager`) |
| Who views | Anyone who can view the job |
| Structure | Flat list only; `parent_job_area_id` always null |
| UI | Dedicated `/jobs/:id/areas` plus summary card on job detail |
| Schema | Existing `job_area` table; no migration |
| Photo tagging | Out of scope (`job_photo.job_area_id` unused) |
| Notes / reorder UI | Out of scope |
| Job status sync | Areas never change job status |

## Data model

Use existing `job_area`. New EF entity `JobArea` mapped in `AppDbContext`. No new columns.

| Column | S2 use |
|--------|--------|
| `id` | PK |
| `job_id` | Parent job |
| `parent_job_area_id` | Always `null` |
| `name` | Required, trimmed, unique per job (case-insensitive) |
| `status` | `not_started` \| `in_progress` \| `completed` \| `blocked` |
| `sort_order` | Append-only; `max + 1` on create |
| `notes` | Unused (leave null) |
| `started_at` | Set once on first transition to `in_progress` |
| `completed_at` | Set on transition to `completed`; cleared when leaving `completed` |

Name uniqueness is per job, not global. Compare trimmed names with a case-insensitive match.

## API

`JobAreasController` at `/api/jobs/{jobId}/areas`. Auth and job access follow photos: unknown/unassigned job → **404**; crew mutating → **403**.

### DTO

```csharp
public sealed record JobAreaDto(
    int Id,
    int JobId,
    string Name,
    string Status,
    int SortOrder,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record CreateJobAreaRequest(string Name);

public sealed record UpdateJobAreaRequest(string? Name, string? Status);
```

### `GET /api/jobs/{jobId}/areas`

Anyone who can view the job. Returns areas ordered by `sort_order`, then `id`.

### `POST /api/jobs/{jobId}/areas`

Manager only. Body: `{ "name": "Kitchen" }`.

- Trim name; reject empty or longer than 80 characters (400)
- Reject duplicate name on that job (400)
- Reject if job is `cancelled` (400)
- Insert with `status = not_started`, `sort_order = max(existing) + 1` (0 if none)
- Return **201** with `JobAreaDto`

### `PATCH /api/jobs/{jobId}/areas/{areaId}`

Manager only. Body may include `name` and/or `status`. Omitted fields stay unchanged. If both are omitted → 400.

- Unknown area for that job → 404
- Cancelled job → 400
- Invalid status → 400
- Renamed name empty or longer than 80 characters → 400
- Renamed duplicate → 400
- Status timestamps:
  - to `in_progress`: set `started_at` if null
  - to `completed`: set `completed_at` to now
  - leaving `completed`: set `completed_at` to null
  - leaving `in_progress` back to `not_started` or `blocked`: keep `started_at`
- Return **200** with updated `JobAreaDto`

### `DELETE /api/jobs/{jobId}/areas/{areaId}`

Manager only. Unknown area → 404. Cancelled job → 400. **204** on success.

## Frontend

### Route

`/jobs/:id/areas` → `JobAreasView.vue`, name `job-areas`. **Not** `managerOnly` so crew can open it. Job access still enforced by the API (404 → “Job not found.”).

### `JobAreasView.vue`

- Back link to job detail; heading **Areas**; job title subtitle
- List: name, status chip, optional started/completed timestamps
- Empty: “No areas yet.” Managers also see a short prompt (Kitchen, Exterior Trim, Bedroom 1)
- Manager only: add name + submit; per-row status select; rename; delete with confirm
- Crew: same list, no controls

### `JobAreasCard.vue` on job detail

- Title **Areas**
- If any: “N of M completed” (N = `completed`, M = total) and a link to the areas page
- If none: “No areas yet” plus the same link (managers can add from the areas page)
- Load via `GET /api/jobs/:id/areas` (store `fetchAreas`)

### `useJobAreasStore`

Pinia store matching `photos.ts`: cache by job id; `fetchAreas`, `createArea`, `updateArea`, `deleteArea`.

## Error handling

| Case | API | UI |
|------|-----|-----|
| Unauthenticated | 401 | Existing session redirect |
| Job not visible | 404 | “Job not found.” |
| Crew mutate | 403 | Inline error on areas page |
| Cancelled job mutate | 400 | Inline error |
| Blank / duplicate name, invalid status | 400 | Inline error; add button disabled when name blank |
| Delete missing area | 404 | Inline error |

Failed mutate keeps the last successful list. Job detail 403 → `/forbidden`.

## Out of scope

- Nested areas / `parent_job_area_id`
- Photo tagging by area
- Area notes
- Drag-and-drop reorder
- Auto-updating job status from area completion
- Crew status updates
- Before/after photo compare

## Testing

`JobAreasIntegrationTests` using the existing backend integration fixture:

1. Manager creates Kitchen, Exterior Trim, Bedroom 1 → list in that order, all `not_started`
2. Patch one area `in_progress` then `completed` → timestamps set; other areas unchanged
3. Duplicate name on the same job → 400
4. Assigned crew GET succeeds; POST/PATCH/DELETE → 403
5. Unassigned crew GET → 404
6. Cancelled job POST → 400
7. DELETE then GET → area gone

Frontend: one Playwright flow if kept small — manager opens a job, adds an area, changes status, sees the summary on job detail. No extra RTL unless add/status controls get fiddly.

## Acceptance (S2 demo)

A manager opens a job, adds Kitchen / Exterior Trim / Bedroom 1, sets Kitchen to In progress and Exterior Trim to Completed, refreshes, and sees independent statuses on the areas page and the job-detail card. Assigned crew can view, not edit.

## References

- Requirements: `docs/requirements-specification.md` S2
- Screen map: `docs/design/screen-map.md` SCR-015
- Schema: `database/schema.md` `job_area`
- Photo access pattern: `backend/Controllers/JobPhotosController.cs`
- Assignment manager gate: `backend/Controllers/JobAssignmentsController.cs`
