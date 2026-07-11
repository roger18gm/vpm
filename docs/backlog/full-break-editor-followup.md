# Full Break Editor — Follow-up

**Status:** Superseded by approved design — see [2026-07-11-full-break-editor-design.md](../superpowers/specs/2026-07-11-full-break-editor-design.md)  
**Date:** 2026-06-07 (draft); approved 2026-07-11  
**Depends on:** Weekly timesheet, manual time entry CRUD (`POST/PUT/DELETE /api/time/entries`)

## Why this exists

Manual time correction (Option B) was shipped with **total break minutes only**:

- Create/edit modal: single **Break (minutes)** field
- On save: `time_entry.break_minutes` is set; existing `time_break` rows for that entry are **removed**
- Weekly view break **windows** come from `time_break`; after manual edit, totals are correct but the per-break list may be empty

Live clocking (Clock tab) still creates rich `time_break` rows (start, end, type). This follow-up closes the gap for **retrospective corrections** when stakeholders need audit-level detail (“lunch was 12:02–12:31, not just 29 minutes”).

## Stakeholder goal

When correcting or adding time on the **This week** tab, users should be able to:

- Add, edit, and remove **individual break windows** (start, end, type)
- See those windows in the weekly timesheet after save
- Keep **work minutes** and **break minutes** consistent everywhere (timesheet, job summary, dashboard later)

## Current vs target

| Concern | Today | Target |
|---------|--------|--------|
| Live shift breaks | `time_break` rows via Clock tab | Unchanged |
| Manual add/edit entry | `break_minutes` integer only | `breaks[]` with windows |
| Weekly break list | From `time_break` | Same, populated after manual save |
| `break_minutes` on entry | Set manually or on clock-out | **Always** sum of `time_break` (or explicit windows on save) |

## Design decisions (proposed)

| Decision | Proposal |
|----------|----------|
| Break types | Keep `lunch` \| `rest` \| `other` (schema already enforces) |
| Who can edit | Same as time entry CRUD: crew own + current week; managers anyone |
| Open entry | Crew still cannot edit in-progress entry breaks here; use Clock tab (or manager override) |
| Validation | Each break: `break_start_at` < `break_end_at`; all breaks within `[clock_in, clock_out]`; no overlapping breaks on same entry |
| `break_minutes` | Recomputed server-side as sum of windows; client may show live preview |
| Removing all breaks | Allowed (`break_minutes = 0`, no `time_break` rows) |
| Timezone | Company `timezone` (`America/Denver`); form uses local date/time like `TimeEntryFormModal` |

## API changes

### Extend request bodies

**`CreateTimeEntryRequest`** and **`UpdateTimeEntryRequest`** — add optional:

```json
"breaks": [
  {
    "breakStartAt": "2026-06-02T17:00:00-06:00",
    "breakEndAt": "2026-06-02T17:30:00-06:00",
    "breakType": "lunch"
  }
]
```

- If `breaks` is **omitted**: keep current behavior (`breakMinutes` only) for backward compatibility during rollout.
- If `breaks` is **present** (including `[]`): replace all `time_break` rows; set `break_minutes` from sum; **ignore** `breakMinutes` on the request (or validate they match).

Optional: `PUT` break item by id — not needed for v1; full replace on entry update is enough.

### Service logic (`TimeEntryService`)

1. Validate entry times and break windows.
2. On create/update with `breaks`:
   - `ClearBreakRowsAsync(entryId)`
   - Insert new `TimeBreak` rows
   - `entry.BreakMinutes = SumBreakMinutes(breaks)`
3. `GetWeeklyTimesheetAsync` / `BuildSessionDtoAsync` — no change; already reads `time_break`.

### New DTO

```csharp
public sealed record TimeBreakInputDto(
    DateTimeOffset BreakStartAt,
    DateTimeOffset BreakEndAt,
    string BreakType);
```

## Frontend changes

### `TimeEntryFormModal.vue`

Replace single **Break (minutes)** with a **break list** section:

- Each row: start time, end time, type (`VpSelect`), remove button
- **Add break** adds a row (default type `lunch`)
- On edit: hydrate rows from `session.breaks` (convert ISO → local time fields)
- On save: build `breaks[]` ISO timestamps via `localDateTimeToIso` (same date as shift or allow cross-midnight only if product later allows)

### `TimesheetDay.vue`

No structural change; break lines already render from API.

### `timesheet` store

- Extend `TimeEntryInput` with `breaks?: { breakStartAt, breakEndAt, breakType }[]`
- Pass through on create/update

### UX copy

- Helper text: “Add each break separately. Totals update when you save.”
- If `breaks` sum ≠ old `breakMinutes`, show preview: “Work: X hrs · Break: Y hrs”

## Implementation plan

> Bite-sized tasks; TDD for backend. Run backend tests and `npm run build` after each task.

### Task 1: API contracts and validation helper

**Files:**

- Modify: `backend/Models/ApiContracts.cs`
- Modify: `backend/Services/TimeEntryService.cs` (private `ValidateBreakWindows` helper)

- [ ] Add `TimeBreakInputDto`
- [ ] Add optional `IReadOnlyList<TimeBreakInputDto>? Breaks` to create/update requests
- [ ] Implement validation: ordering, within shift, no overlap, valid `break_type`
- [ ] Unit-level tests via integration tests in Task 2

### Task 2: Backend — persist break windows on create/update

**Files:**

- Modify: `backend/Services/TimeEntryService.cs` (`CreateManualEntryAsync`, `UpdateEntryAsync`)
- Modify: `backend.Tests/IntegrationTests/TimeEntryMutationIntegrationTests.cs`

- [ ] Write failing tests:
  - Create entry with two breaks → weekly GET returns both windows; `break_minutes` = sum
  - Update entry replaces breaks (old ids gone, new rows inserted)
  - Overlapping breaks → 400
  - Break outside shift → 400
- [ ] Implement replace logic; recompute `break_minutes`
- [ ] Keep `breakMinutes`-only path when `breaks` omitted

### Task 3: Frontend types and store

**Files:**

- Modify: `frontend/src/types/timesheet.ts`
- Modify: `frontend/src/stores/timesheet.ts`

- [ ] Add `TimeBreakInput` type
- [ ] Include `breaks` in create/update API payloads when present

### Task 4: Break list UI in modal

**Files:**

- Modify: `frontend/src/components/time/TimeEntryFormModal.vue`
- Optional: Create `frontend/src/components/time/BreakWindowRow.vue`

- [ ] Break list with add/remove
- [ ] Hydrate from `session.breaks` on edit
- [ ] Client validation before emit (mirror server rules)
- [ ] Remove or hide standalone **Break (minutes)** when using break list

### Task 5: Docs and stakeholder sign-off

**Files:**

- Modify: `docs/design/stakeholder-decisions.md` (new Q7 or appendix)
- Modify: `docs/design/ui-spec.md` (SCR-010 timesheet modal)
- Modify: `feature-ideas.md` (mark item done when shipped)

- [ ] Stakeholder confirms: break types, overlap rules, crew vs manager on in-progress breaks

### Task 6: Verification

- [ ] `dotnet test backend.Tests/backend.Tests.csproj`
- [ ] `cd frontend && npm run build`
- [ ] Manual: add entry with lunch + rest breaks → expand day → see both lines
- [ ] Manual: edit entry → change break times → totals update

## Test scenarios (acceptance)

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Create manual entry with one lunch break | Window shown; work = span − break |
| 2 | Edit entry: add second break | Two rows in weekly view |
| 3 | Edit entry: remove all breaks | Empty break list; `break_minutes = 0` |
| 4 | Break end before break start | 400 |
| 5 | Break outside clock-in/out | 400 |
| 6 | Overlapping breaks | 400 |
| 7 | Crew, past week | 403 (unchanged) |
| 8 | Clock-tab live break then clock out | Unchanged; still creates `time_break` rows |

## Out of scope (this follow-up)

- Payroll export
- Editing breaks on **open** entry without clock-out (crew) — still Clock tab unless policy changes
- Cross-midnight break spanning two calendar days
- Audit log / “reason for edit” (separate backlog item)
- Per-break notes field (schema has `notes` on `time_break` — optional later)

## References

- Schema: `database/schema.md` — `time_break`, `time_entry.break_minutes`
- Weekly timesheet spec: `docs/superpowers/specs/2026-06-06-weekly-timesheet-design.md`
- Stakeholder Q2: breaks tracked separately for display — `docs/design/stakeholder-decisions.md`
- Current manual entry UI: `frontend/src/components/time/TimeEntryFormModal.vue`
- Current clear-on-save behavior: `TimeEntryService.ClearBreakRowsAsync`
