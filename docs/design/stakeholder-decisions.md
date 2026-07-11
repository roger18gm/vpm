# Stakeholder decisions (review complete)

Recorded outcomes from stakeholder review. Implementation and UX should follow these unless explicitly revisited.

| ID | Decision | Implication |
|----|----------|-------------|
| Q1 | **One active clock-in per worker** | Block second `time_entry` without `clock_out_at`; UI disables other jobs while clocked in. |
| Q2 | **Breaks are tracked; unpaid for MVP display** | Store `break_minutes` / `time_break`; clock-out summary shows “Work time” and “Break time” separately. No payroll export in MVP. |
| Q3 | **Overdue = `due_at` passed** | Job is overdue when `due_at < now` and `status` is `scheduled` or `in_progress`. Ignore `scheduled_end_at` for overdue badge until stakeholder asks otherwise. |
| Q4 | **Before, after, and progress photos** | `photo_kind`: `before` \| `after` \| `progress`. All allowed on any active job. |
| Q5 | **Only `owner`, `admin`, `manager` change job status** | Crew read-only on status; managers use dropdown on SCR-007 / edit. |
| Q6 | **Crew sees assigned jobs only** | `GET /api/jobs` filtered by `job_assignment` for `crew`; managers see company scope. |
| Q7 | **Manual time corrections edit break windows** | Add/Edit time on This week uses start/end/type rows (not minutes-only). Persists `time_break`; `break_minutes` is the sum. See [2026-07-11-full-break-editor-design.md](../superpowers/specs/2026-07-11-full-break-editor-design.md). |

## Navigation confirmed

- **Crew:** bottom nav — Jobs, Clock, Account. Photos from job detail.
- **Manager:** dashboard home on desktop; mobile bottom nav Dashboard / Jobs / Account.
- **Primary brand colors:** red, grey, white, black for chrome; semantic badge colors allowed (see [ui-spec.md](./ui-spec.md#brand-palette-vs-semantic-colors)).

## Sign-off

- [x] Journeys J1–J6 accepted for MVP
- [x] Wireframe layouts accepted
- [ ] Revisit Q2 if payroll integration is added (499B+)
