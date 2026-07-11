# Full Break Editor on Manual Time Entries

**Status:** Approved for implementation planning (2026-07-11)  
**Depends on:** Weekly timesheet, manual time entry CRUD (`POST/PUT/DELETE /api/time/entries`)  
**Supersedes backlog draft:** [docs/backlog/full-break-editor-followup.md](../../backlog/full-break-editor-followup.md)

## Problem

Manual time correction ships with a single **Break (minutes)** field. On save, `time_entry.break_minutes` is set and existing `time_break` rows for that entry are cleared. The weekly timesheet shows break **windows** from `time_break`, so after a manual edit the total is correct but the per-break list is empty.

Live clocking (Clock tab) already creates rich `time_break` rows. Stakeholders need the same audit detail when correcting time retrospectively (“lunch was 12:02–12:31, not just 29 minutes”).

## Goal

On the **This week** tab, when adding or editing a closed time entry, users can:

- Add, edit, and remove individual break windows (start, end, type)
- See those windows in the weekly timesheet after save
- Keep work minutes and break minutes consistent (timesheet, job summary, dashboard)

## Decisions

| Decision | Choice |
|----------|--------|
| Approach | Nested `breaks[]` on create/update entry APIs (not separate break CRUD) |
| Modal UX | **Windows only** — replace Break (minutes); always send `breaks[]` (including `[]`) |
| Break types | `lunch` \| `rest` \| `other` (schema already enforces) |
| Who can edit | Same as time entry CRUD: crew own + current week; managers anyone |
| Open (in-progress) entry | Crew cannot edit breaks here; use Clock tab |
| `break_minutes` | Recomputed server-side as sum of windows when `breaks` is present |
| API omit `breaks` | Keep minutes-only path for compatibility (tests / older clients) |
| Timezone | Company timezone; form uses local date/time like today |

## API

### Request bodies

Extend `CreateTimeEntryRequest` and `UpdateTimeEntryRequest` with optional:

```json
"breaks": [
  {
    "breakStartAt": "2026-06-02T17:00:00-06:00",
    "breakEndAt": "2026-06-02T17:30:00-06:00",
    "breakType": "lunch"
  }
]
```

DTO:

```csharp
public sealed record TimeBreakInputDto(
    DateTimeOffset BreakStartAt,
    DateTimeOffset BreakEndAt,
    string BreakType);
```

Behavior:

- `breaks` **present** (including `[]`): replace all `time_break` rows; set `break_minutes` from sum; ignore request `breakMinutes`
- `breaks` **omitted**: current behavior (`breakMinutes` only; clear windows)

No per-break id endpoints in v1; full replace on entry save is enough.

### Validation (400)

- Each break: `break_start_at` < `break_end_at`
- All breaks within `[clock_in, clock_out]`
- No overlapping breaks on the same entry
- Valid `breakType`

### Reads

No change to weekly GET / session DTOs — they already return break windows from `time_break`.

### Service (`TimeEntryService`)

On create/update with `breaks`:

1. Validate entry times and break windows
2. Clear existing `time_break` rows for the entry
3. Insert new `TimeBreak` rows
4. Set `entry.BreakMinutes` to the sum of windows

## Frontend

### `TimeEntryFormModal.vue`

- Remove standalone **Break (minutes)** field
- **Breaks** section: rows with start time, end time, type (`VpSelect`), remove
- **Add break** (default type `lunch`)
- Helper: “Add each break separately. Totals update when you save.”
- Live preview: “Work: X hrs · Break: Y hrs”
- Edit: hydrate from `session.breaks` (ISO → local time on shift date)
- Client validation mirrors server rules before emit
- Always emit `breaks` (including `[]`)

### Store / types

- Add `TimeBreakInput`
- Include `breaks` on create/update payloads from the modal

### `TimesheetDay.vue`

No structural change; break lines already render from the API.

## Out of scope

- Payroll export
- Editing breaks on an open entry without clock-out (crew) — Clock tab
- Cross-midnight breaks spanning two calendar days
- Audit log / reason for edit
- Per-break notes (`time_break.notes` exists; optional later)
- Before/after photo compare and other non–time-tracking backlog items

## Acceptance tests

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Create manual entry with one lunch break | Window shown; work = span − break |
| 2 | Edit entry: add second break | Two rows in weekly view |
| 3 | Edit entry: remove all breaks | Empty list; `break_minutes = 0` |
| 4 | Break end before start | 400 |
| 5 | Break outside clock-in/out | 400 |
| 6 | Overlapping breaks | 400 |
| 7 | Crew, past week | 403 (unchanged) |
| 8 | Clock-tab live break then clock out | Unchanged |

Verification: `dotnet test backend.Tests/backend.Tests.csproj`, `npm run build` in frontend, plus manual add/edit on This week.

## References

- Schema: `database/schema.md` — `time_break`, `time_entry.break_minutes`
- Weekly timesheet: `docs/superpowers/specs/2026-06-06-weekly-timesheet-design.md` (if present) / shipped weekly timesheet
- Stakeholder Q2 (breaks tracked separately): `docs/design/stakeholder-decisions.md`
- Current UI: `frontend/src/components/time/TimeEntryFormModal.vue`
- Current clear-on-save: `TimeEntryService.ClearBreakRowsAsync`
