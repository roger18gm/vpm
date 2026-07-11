# Full Break Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users add/edit individual break windows when creating or correcting closed time entries on the weekly timesheet, so `time_break` rows stay in sync with displayed totals.

**Architecture:** Extend `CreateTimeEntryRequest` / `UpdateTimeEntryRequest` with optional `breaks[]`. When present, `TimeEntryService` validates windows, replaces `time_break` rows, and sets `break_minutes` to their sum. The modal drops the minutes field and always posts `breaks[]`. Weekly GET is unchanged.

**Tech Stack:** ASP.NET Core, EF Core, PostgreSQL, Vue 3, Pinia, TypeScript, xUnit integration tests

**Spec:** [docs/superpowers/specs/2026-07-11-full-break-editor-design.md](../specs/2026-07-11-full-break-editor-design.md)

---

## File map

| File | Responsibility |
|------|----------------|
| `backend/Models/ApiContracts.cs` | `TimeBreakInputDto`; optional `Breaks` on create/update requests |
| `backend/Services/TimeEntryService.cs` | Validate windows; persist replace; recompute `BreakMinutes` |
| `backend.Tests/IntegrationTests/TimeEntryMutationIntegrationTests.cs` | Acceptance coverage for create/update/validation |
| `frontend/src/types/timesheet.ts` | `TimeBreakInput`; extend `TimeEntryInput` |
| `frontend/src/stores/timesheet.ts` | Pass `breaks` on POST/PUT |
| `frontend/src/components/time/TimeEntryFormModal.vue` | Break list UI; always emit `breaks` |
| `frontend/src/components/time/WeeklyTimesheet.vue` | Forward `breaks` in `handleSaved` |
| `docs/design/stakeholder-decisions.md` | Note Q7 / break-window editing |
| `docs/design/ui-spec.md` | SCR-010 modal: break list |
| `docs/backlog/feature-ideas.md` | Mark full break editor done when shipped |

---

### Task 1: API contracts — `TimeBreakInputDto` and optional `Breaks`

**Files:**
- Modify: `backend/Models/ApiContracts.cs`

- [ ] **Step 1: Add DTO and request fields**

After `UpdateTimeEntryRequest`, add:

```csharp
public sealed record TimeBreakInputDto(
    DateTimeOffset BreakStartAt,
    DateTimeOffset BreakEndAt,
    string BreakType);
```

Change create/update records to append optional breaks (keep existing defaults so positional test call sites keep working):

```csharp
public sealed record CreateTimeEntryRequest(
    int JobId,
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    int? PersonId,
    int BreakMinutes = 0,
    string? Notes = null,
    IReadOnlyList<TimeBreakInputDto>? Breaks = null);

public sealed record UpdateTimeEntryRequest(
    int JobId,
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    int BreakMinutes = 0,
    string? Notes = null,
    IReadOnlyList<TimeBreakInputDto>? Breaks = null);
```

- [ ] **Step 2: Build to confirm contracts compile**

Run: `dotnet build backend/VisionPaint.csproj --no-restore`
(If packages missing first: `dotnet restore backend/VisionPaint.csproj`)

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/Models/ApiContracts.cs
git commit -m "Add TimeBreakInputDto and optional breaks on time entry requests"
```

---

### Task 2: Failing integration tests — create with break windows

**Files:**
- Modify: `backend.Tests/IntegrationTests/TimeEntryMutationIntegrationTests.cs`

- [ ] **Step 1: Add create-with-breaks test**

Append to `TimeEntryMutationIntegrationTests`:

```csharp
[Fact]
public async Task CreateEntry_with_breaks_persists_windows_and_sums_minutes()
{
    var owner = await BootstrapOwnerAsync();
    var (jobId, _) = await SeedJobAsync(_fixture, owner);
    var (clockIn, clockOut) = CurrentWeekRange();
    var lunchStart = clockIn.AddHours(1);
    var lunchEnd = lunchStart.AddMinutes(30);
    var restStart = clockIn.AddHours(2);
    var restEnd = restStart.AddMinutes(10);

    _fixture.AuthClient.SetBearerToken(owner.AccessToken);
    using var response = await _fixture.Client.PostAsJsonAsync("/api/time/entries", new CreateTimeEntryRequest(
        jobId,
        clockIn,
        clockOut,
        null,
        BreakMinutes: 0,
        Notes: null,
        Breaks: new[]
        {
            new TimeBreakInputDto(lunchStart, lunchEnd, "lunch"),
            new TimeBreakInputDto(restStart, restEnd, "rest"),
        }));

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var session = await response.Content.ReadFromJsonAsync<WeeklyTimesheetSessionDto>();
    Assert.NotNull(session);
    Assert.Equal(40, session!.BreakMinutes);
    Assert.Equal(200, session.WorkMinutes); // 4h - 40m
    Assert.Equal(2, session.Breaks.Count);
    Assert.Contains(session.Breaks, b => b.BreakType == "lunch" && b.Minutes == 30);
    Assert.Contains(session.Breaks, b => b.BreakType == "rest" && b.Minutes == 10);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~CreateEntry_with_breaks_persists_windows_and_sums_minutes --no-restore
```

Expected: FAIL — Created may succeed but `Breaks.Count == 0` and `BreakMinutes == 0` (breaks ignored today), or assert fails similarly.

- [ ] **Step 3: Commit failing test**

```bash
git add backend.Tests/IntegrationTests/TimeEntryMutationIntegrationTests.cs
git commit -m "test: fail until manual create persists break windows"
```

---

### Task 3: Backend — validate and persist breaks on create

**Files:**
- Modify: `backend/Services/TimeEntryService.cs`

- [ ] **Step 1: Add helpers**

Near `ClearBreakRowsAsync`, add:

```csharp
private static readonly HashSet<string> ValidBreakTypes = new(StringComparer.OrdinalIgnoreCase)
{
    "lunch", "rest", "other"
};

private string? ValidateBreakWindows(
    DateTimeOffset clockInAt,
    DateTimeOffset clockOutAt,
    IReadOnlyList<TimeBreakInputDto> breaks,
    out int totalBreakMinutes)
{
    totalBreakMinutes = 0;
    var ordered = breaks.OrderBy(b => b.BreakStartAt).ToList();
    DateTimeOffset? previousEnd = null;

    foreach (var b in ordered)
    {
        if (!ValidBreakTypes.Contains(b.BreakType))
        {
            return "Break type must be lunch, rest, or other.";
        }

        if (b.BreakEndAt <= b.BreakStartAt)
        {
            return "Break end must be after break start.";
        }

        if (b.BreakStartAt < clockInAt || b.BreakEndAt > clockOutAt)
        {
            return "Breaks must fall within the shift.";
        }

        if (previousEnd is not null && b.BreakStartAt < previousEnd)
        {
            return "Breaks cannot overlap.";
        }

        previousEnd = b.BreakEndAt;
        totalBreakMinutes += (int)Math.Max(0, (b.BreakEndAt - b.BreakStartAt).TotalMinutes);
    }

    return null;
}

private async Task ReplaceBreakRowsAsync(
    int timeEntryId,
    IReadOnlyList<TimeBreakInputDto> breaks,
    CancellationToken cancellationToken)
{
    await ClearBreakRowsAsync(timeEntryId, cancellationToken);
    foreach (var b in breaks)
    {
        _db.TimeBreaks.Add(new TimeBreak
        {
            TimeEntryId = timeEntryId,
            BreakStartAt = b.BreakStartAt,
            BreakEndAt = b.BreakEndAt,
            BreakType = b.BreakType.ToLowerInvariant()
        });
    }

    if (breaks.Count > 0)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 2: Wire `CreateManualEntryAsync`**

After `ValidateClosedEntryTimes` setup, resolve effective break minutes and optional window validation **before** creating the entity:

```csharp
var breakMinutes = request.BreakMinutes;
if (request.Breaks is not null)
{
    var breakError = ValidateBreakWindows(
        request.ClockInAt,
        request.ClockOutAt,
        request.Breaks,
        out breakMinutes);
    if (breakError is not null)
    {
        return (null, breakError, 400);
    }
}

var validationError = ValidateClosedEntryTimes(
    request.ClockInAt,
    request.ClockOutAt,
    breakMinutes,
    timeZone,
    user,
    null);
```

Set `entry.BreakMinutes = breakMinutes` (not `request.BreakMinutes`).

After first `SaveChangesAsync` (entry has Id), if `request.Breaks is not null`:

```csharp
await ReplaceBreakRowsAsync(entry.Id, request.Breaks, cancellationToken);
```

Then return `BuildSessionDtoAsync` as today.

- [ ] **Step 3: Run create-with-breaks test**

Run:

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~CreateEntry_with_breaks_persists_windows_and_sums_minutes
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/Services/TimeEntryService.cs
git commit -m "Persist break windows on manual time entry create"
```

---

### Task 4: Failing tests — update replace + validation errors

**Files:**
- Modify: `backend.Tests/IntegrationTests/TimeEntryMutationIntegrationTests.cs`

- [ ] **Step 1: Add update and validation tests**

```csharp
[Fact]
public async Task UpdateEntry_with_breaks_replaces_windows()
{
    var owner = await BootstrapOwnerAsync();
    var (jobId, personId) = await SeedJobAsync(_fixture, owner);
    var (clockIn, clockOut) = CurrentWeekRange();

    await using var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.Database.ConnectionString)
            .Options);

    var entry = new TimeEntry
    {
        JobId = jobId,
        PersonId = personId,
        ClockInAt = clockIn,
        ClockOutAt = clockOut,
        BreakMinutes = 15,
        CreatedAt = clockIn,
        UpdatedAt = clockOut
    };
    db.TimeEntries.Add(entry);
    await db.SaveChangesAsync();

    db.TimeBreaks.Add(new TimeBreak
    {
        TimeEntryId = entry.Id,
        BreakStartAt = clockIn.AddMinutes(30),
        BreakEndAt = clockIn.AddMinutes(45),
        BreakType = "rest"
    });
    await db.SaveChangesAsync();
    var oldBreakId = await db.TimeBreaks.Where(b => b.TimeEntryId == entry.Id).Select(b => b.Id).SingleAsync();

    _fixture.AuthClient.SetBearerToken(owner.AccessToken);
    var lunchStart = clockIn.AddHours(1);
    var lunchEnd = lunchStart.AddMinutes(20);
    using var response = await _fixture.Client.PutAsJsonAsync(
        $"/api/time/entries/{entry.Id}",
        new UpdateTimeEntryRequest(
            jobId,
            clockIn,
            clockOut,
            BreakMinutes: 99,
            Notes: null,
            Breaks: new[] { new TimeBreakInputDto(lunchStart, lunchEnd, "lunch") }));

    response.EnsureSuccessStatusCode();
    var session = await response.Content.ReadFromJsonAsync<WeeklyTimesheetSessionDto>();
    Assert.Equal(20, session!.BreakMinutes);
    Assert.Single(session.Breaks);
    Assert.Equal("lunch", session.Breaks[0].BreakType);
    Assert.DoesNotContain(session.Breaks, b => b.Id == oldBreakId);
}

[Fact]
public async Task CreateEntry_overlapping_breaks_returns_400()
{
    var owner = await BootstrapOwnerAsync();
    var (jobId, _) = await SeedJobAsync(_fixture, owner);
    var (clockIn, clockOut) = CurrentWeekRange();

    _fixture.AuthClient.SetBearerToken(owner.AccessToken);
    using var response = await _fixture.Client.PostAsJsonAsync("/api/time/entries", new CreateTimeEntryRequest(
        jobId,
        clockIn,
        clockOut,
        null,
        Breaks: new[]
        {
            new TimeBreakInputDto(clockIn.AddMinutes(30), clockIn.AddMinutes(60), "lunch"),
            new TimeBreakInputDto(clockIn.AddMinutes(45), clockIn.AddMinutes(75), "rest"),
        }));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task CreateEntry_break_outside_shift_returns_400()
{
    var owner = await BootstrapOwnerAsync();
    var (jobId, _) = await SeedJobAsync(_fixture, owner);
    var (clockIn, clockOut) = CurrentWeekRange();

    _fixture.AuthClient.SetBearerToken(owner.AccessToken);
    using var response = await _fixture.Client.PostAsJsonAsync("/api/time/entries", new CreateTimeEntryRequest(
        jobId,
        clockIn,
        clockOut,
        null,
        Breaks: new[]
        {
            new TimeBreakInputDto(clockIn.AddHours(-1), clockIn.AddMinutes(30), "lunch"),
        }));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task CreateEntry_break_end_before_start_returns_400()
{
    var owner = await BootstrapOwnerAsync();
    var (jobId, _) = await SeedJobAsync(_fixture, owner);
    var (clockIn, clockOut) = CurrentWeekRange();

    _fixture.AuthClient.SetBearerToken(owner.AccessToken);
    using var response = await _fixture.Client.PostAsJsonAsync("/api/time/entries", new CreateTimeEntryRequest(
        jobId,
        clockIn,
        clockOut,
        null,
        Breaks: new[]
        {
            new TimeBreakInputDto(clockIn.AddHours(1), clockIn.AddMinutes(30), "lunch"),
        }));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task CreateEntry_empty_breaks_array_clears_break_minutes()
{
    var owner = await BootstrapOwnerAsync();
    var (jobId, _) = await SeedJobAsync(_fixture, owner);
    var (clockIn, clockOut) = CurrentWeekRange();

    _fixture.AuthClient.SetBearerToken(owner.AccessToken);
    using var response = await _fixture.Client.PostAsJsonAsync("/api/time/entries", new CreateTimeEntryRequest(
        jobId,
        clockIn,
        clockOut,
        null,
        BreakMinutes: 30,
        Notes: null,
        Breaks: Array.Empty<TimeBreakInputDto>()));

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var session = await response.Content.ReadFromJsonAsync<WeeklyTimesheetSessionDto>();
    Assert.Equal(0, session!.BreakMinutes);
    Assert.Empty(session.Breaks);
}
```

- [ ] **Step 2: Run the new tests — expect update/validation failures until Task 5**

Run:

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~UpdateEntry_with_breaks|FullyQualifiedName~CreateEntry_overlapping|FullyQualifiedName~CreateEntry_break_outside|FullyQualifiedName~CreateEntry_break_end_before|FullyQualifiedName~CreateEntry_empty_breaks"
```

Expected: create validation tests may already PASS (if Task 3 helpers are used); `UpdateEntry_with_breaks_replaces_windows` FAIL until Task 5.

- [ ] **Step 3: Commit tests**

```bash
git add backend.Tests/IntegrationTests/TimeEntryMutationIntegrationTests.cs
git commit -m "test: cover break window replace and validation errors"
```

---

### Task 5: Backend — persist breaks on update; keep omit-`breaks` path

**Files:**
- Modify: `backend/Services/TimeEntryService.cs` (`UpdateEntryAsync`)

- [ ] **Step 1: Mirror create logic in `UpdateEntryAsync`**

Replace the block that always clears breaks and sets `request.BreakMinutes` with:

```csharp
var breakMinutes = request.BreakMinutes;
if (request.Breaks is not null)
{
    var breakError = ValidateBreakWindows(
        request.ClockInAt,
        request.ClockOutAt,
        request.Breaks,
        out breakMinutes);
    if (breakError is not null)
    {
        return (null, breakError, 400);
    }
}

var validationError = ValidateClosedEntryTimes(
    request.ClockInAt,
    request.ClockOutAt,
    breakMinutes,
    timeZone,
    user,
    entry);
if (validationError is not null)
{
    return (null, validationError, 400);
}

// ... job validation unchanged ...

if (request.Breaks is not null)
{
    await ReplaceBreakRowsAsync(entry.Id, request.Breaks, cancellationToken);
}
else
{
    await ClearBreakRowsAsync(entry.Id, cancellationToken);
}

entry.JobId = request.JobId;
entry.ClockInAt = request.ClockInAt;
entry.ClockOutAt = request.ClockOutAt;
entry.BreakMinutes = breakMinutes;
entry.Notes = request.Notes;
entry.UpdatedAt = DateTimeOffset.UtcNow;
await _db.SaveChangesAsync(cancellationToken);
```

Notes:
- When `Breaks` is omitted, keep today’s behavior: clear windows, use `request.BreakMinutes`.
- When `Breaks` is `[]`, `breakMinutes` is 0 and windows are cleared via `ReplaceBreakRowsAsync`.

- [ ] **Step 2: Run all mutation break tests + existing mutation suite**

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~TimeEntryMutationIntegrationTests
```

Expected: all PASS (including older create/update without `Breaks`).

- [ ] **Step 3: Commit**

```bash
git add backend/Services/TimeEntryService.cs
git commit -m "Replace break windows on manual time entry update"
```

---

### Task 6: Frontend types and timesheet store

**Files:**
- Modify: `frontend/src/types/timesheet.ts`
- Modify: `frontend/src/stores/timesheet.ts`

- [ ] **Step 1: Extend types**

In `timesheet.ts`:

```typescript
export type TimeBreakInput = {
  breakStartAt: string;
  breakEndAt: string;
  breakType: "lunch" | "rest" | "other";
};

export type TimeEntryInput = {
  jobId: number;
  clockInAt: string;
  clockOutAt: string;
  breakMinutes: number;
  breaks: TimeBreakInput[];
  notes?: string | null;
  personId?: number | null;
};
```

- [ ] **Step 2: Pass `breaks` in store**

In `createEntry` / `updateEntry` bodies, add:

```typescript
breaks: input.breaks,
```

Keep sending `breakMinutes` as the sum for any server that still reads it when `breaks` is present (server ignores it when `breaks` is set; harmless).

- [ ] **Step 3: Typecheck**

Run: `cd frontend; npm run build`

Expected: FAIL on `WeeklyTimesheet.vue` / modal until Task 7–8 (missing `breaks` on payloads). If build only typechecks changed imports, proceed to next tasks immediately.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/types/timesheet.ts frontend/src/stores/timesheet.ts
git commit -m "Pass breaks array on timesheet create and update"
```

---

### Task 7: Break list UI in `TimeEntryFormModal`

**Files:**
- Modify: `frontend/src/components/time/TimeEntryFormModal.vue`

- [ ] **Step 1: Replace minutes field with break rows**

Script changes:

1. Change emit payload to include `breaks: TimeBreakInput[]` and keep `breakMinutes` as computed sum for the store.
2. Replace `breakMinutes` ref with:

```typescript
type BreakRow = { start: string; end: string; breakType: "lunch" | "rest" | "other" };
const breakRows = ref<BreakRow[]>([]);
```

3. On open (edit): map `props.session.breaks` → local times via `isoToLocalTime`; on create: `breakRows = []`.
4. `addBreak()` pushes `{ start: "12:00", end: "12:30", breakType: "lunch" }`.
5. On submit:
   - Build ISO for each row with `localDateTimeToIso(date, start/end, timezoneId)`.
   - Validate: end > start; each within clock-in/out; no overlaps (sort by start).
   - Emit:

```typescript
const breaks = breakRows.value.map((row) => ({
  breakStartAt: localDateTimeToIso(date.value, row.start, props.timezoneId),
  breakEndAt: localDateTimeToIso(date.value, row.end, props.timezoneId),
  breakType: row.breakType,
}));
const breakMinutes = breaks.reduce((sum, b) => {
  return sum + Math.max(0, (new Date(b.breakEndAt).getTime() - new Date(b.breakStartAt).getTime()) / 60000);
}, 0);
emit("saved", { jobId, clockInAt, clockOutAt, breakMinutes, breaks, notes });
```

Template:

- Remove `<VpInput ... Break (minutes)>`.
- Add section:

```html
<div class="space-y-2">
  <div class="flex items-center justify-between">
    <p class="text-sm font-medium">Breaks</p>
    <button type="button" class="text-sm text-primary" @click="addBreak">+ Add break</button>
  </div>
  <p class="text-xs text-muted">Add each break separately. Totals update when you save.</p>
  <div v-for="(row, i) in breakRows" :key="i" class="grid grid-cols-[1fr_1fr_1fr_auto] gap-2 items-end">
    <VpInput v-model="row.start" label="Start" type="time" />
    <VpInput v-model="row.end" label="End" type="time" />
    <VpSelect v-model="row.breakType" label="Type">
      <option value="lunch">Lunch</option>
      <option value="rest">Rest</option>
      <option value="other">Other</option>
    </VpSelect>
    <button type="button" class="text-xs text-error pb-2" @click="breakRows.splice(i, 1)">Remove</button>
  </div>
  <p class="text-xs text-muted">{{ workBreakPreview }}</p>
</div>
```

`workBreakPreview` computed: from clock-in/out and sum of row durations → `Work: X hrs · Break: Y hrs` (reuse a small minutes formatter if one exists nearby; otherwise inline `Xh Ym`).

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/time/TimeEntryFormModal.vue
git commit -m "Replace break minutes with break window list in time entry modal"
```

---

### Task 8: Wire `WeeklyTimesheet` saved handler

**Files:**
- Modify: `frontend/src/components/time/WeeklyTimesheet.vue`

- [ ] **Step 1: Extend `handleSaved` payload type**

```typescript
async function handleSaved(payload: {
  jobId: number;
  clockInAt: string;
  clockOutAt: string;
  breakMinutes: number;
  breaks: { breakStartAt: string; breakEndAt: string; breakType: "lunch" | "rest" | "other" }[];
  notes: string | null;
}) {
  // unchanged body — spreads into createEntry / updateEntry
}
```

Ensure create/update calls pass through `breaks` (already via `...payload` / `payload`).

- [ ] **Step 2: Build frontend**

Run: `cd frontend; npm run build`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/time/WeeklyTimesheet.vue
git commit -m "Forward break windows from timesheet modal to API"
```

---

### Task 9: Docs

**Files:**
- Modify: `docs/design/stakeholder-decisions.md`
- Modify: `docs/design/ui-spec.md` (SCR-010 section — note Add/Edit time modal break list)
- Modify: `docs/backlog/feature-ideas.md`

- [ ] **Step 1: Stakeholder decisions**

Add a row (or short appendix note):

| Q7 | Manual time corrections edit break **windows** (start/end/type), not only total minutes | Matches Clock-tab `time_break` detail on weekly view |

- [ ] **Step 2: UI spec**

Under SCR-010 / This week modal: document break list (add/remove rows; types lunch/rest/other; no standalone minutes field).

- [ ] **Step 3: Feature ideas**

Mark full break editor line done (strikethrough or checkbox) and link the design spec.

- [ ] **Step 4: Commit**

```bash
git add docs/design/stakeholder-decisions.md docs/design/ui-spec.md docs/backlog/feature-ideas.md
git commit -m "Document full break editor in design backlog"
```

---

### Task 10: Full verification

- [ ] **Step 1: Backend tests**

```bash
dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~TimeEntryMutationIntegrationTests
```

Expected: all PASS.

- [ ] **Step 2: Frontend build**

```bash
cd frontend
npm run build
```

Expected: PASS.

- [ ] **Step 3: Manual smoke (local)**

1. Sign in as manager/crew with current-week access.
2. Clock → This week → Add time → add lunch + rest → Save.
3. Expand day → both break lines visible; work/break totals match.
4. Edit entry → remove one break → totals update.
5. Confirm Clock tab live start/end break still works after a normal clock-out.

- [ ] **Step 4: Final commit only if docs/tests needed fixes from smoke**

Otherwise done.

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| Optional `breaks[]` on create/update | 1 |
| Validate order / within shift / no overlap / types | 3, 4, 5 |
| Replace `time_break`; recompute `break_minutes` | 3, 5 |
| Omit `breaks` keeps minutes-only path | 5 |
| Windows-only modal; always send `breaks` | 7, 8 |
| Hydrate from `session.breaks` | 7 |
| Weekly view unchanged | (no code — existing) |
| Acceptance scenarios 1–7 | 2, 4, 10 |
| Scenario 8 Clock tab unchanged | 10 manual |
| Out of scope left out | — |

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-11-full-break-editor.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks  
**2. Inline Execution** — run tasks in this session with executing-plans checkpoints  

Which approach?
