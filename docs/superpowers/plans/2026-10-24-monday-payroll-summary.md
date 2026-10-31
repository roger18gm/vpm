# Monday Payroll Summary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Note:** Skip `git commit` steps if the human asked not to commit.

**Goal:** Email every active owner and admin the previous Sunday–Saturday payroll summary the first time the Production API is running on or after Monday 07:00 company time.

**Architecture:** A shared minute helper feeds both the weekly timesheet and a `PayrollSummaryService`. That service claims one `payroll_summary_send` row per company and week, builds the summary, and sends it through the existing Brevo `IEmailSender`. A `BackgroundService` runs the check at startup and every five minutes, and only in Production.

**Tech Stack:** ASP.NET Core, EF Core, Npgsql, xUnit, Brevo `IEmailSender`

**Spec:** [docs/superpowers/specs/2026-10-24-monday-payroll-summary-design.md](../specs/2026-10-24-monday-payroll-summary-design.md)

## Global Constraints

- Recipients are active `owner` and `admin` members with an active `auth_user` and a non-blank sign-in email. Not `manager` or `crew`.
- Listed people are every `company_member.status = active` member, including zero hours. `person.is_active` is not required. A membership that is not `active` is omitted even if that person worked.
- Week is Sunday–Saturday in the company timezone. The target week starts on `SundayOfWeek(local today)` minus 7 days.
- Sending opens at 07:00 local on the Monday after that Saturday. Before that instant, do nothing.
- A week that was never sent is not backfilled once a newer week is the target.
- All minutes for a shift sit on the local clock-in day.
- An open shift counts through the send instant. The person line says `Still clocked in` when any entry in that week has no clock-out.
- Hours are decimal hours to one place, invariant culture, from integer minutes: `90` is `1.5`, `0` is `0.0`. Sum minutes, then format once.
- Same calendar year subject: `Payroll summary for Oct 18–Oct 24, 2026`. Cross-year subject: `Payroll summary for Dec 27, 2026–Jan 2, 2027`. The dash is an en dash (`–`).
- Claim table `payroll_summary_send` primary key `(company_id, week_start)`. Take over an unfinished claim only when `claimed_at` is at least 15 minutes old, and only if `claimed_at` still equals the value that was read.
- If at least one send succeeds, set `sent_at` and do not retry failures. If every send fails, or there is no recipient address, delete the claim.
- Scheduler runs only when the host environment is Production. It checks once at startup, then every five minutes.
- No HTTP endpoint. Do not change password-reset mail or `DashboardService`.

---

## File map

| File | Responsibility |
|------|----------------|
| `backend/Services/TimeEntryMinutes.cs` | The one minute formula |
| `backend/Services/TimeEntryService.cs` | Call the shared formula |
| `backend/Services/PayrollWeek.cs` | Decide whether a week is sendable, and which Sunday it is |
| `backend/Services/PayrollSummaryMail.cs` | Subject, hour text, HTML, and plain text |
| `backend/Models/PayrollSummarySend.cs` | EF entity for the claim row |
| `backend/Data/AppDbContext.cs` | Map `payroll_summary_send` |
| `backend/Services/IPayrollSummaryService.cs` | Scheduler seam |
| `backend/Services/PayrollSummaryService.cs` | Claim, build, send |
| `backend/Services/PayrollSummaryHost.cs` | Production-only gate |
| `backend/Services/PayrollSummaryScheduler.cs` | Startup check and five-minute loop |
| `backend/Program.cs` | Register the service, clock, and scheduler |
| `database/migrations/20261024_170000_payroll_summary_send.sql` | Create the table |
| `database/schema.sql` | Same table in the draft schema |
| `backend.Tests/Infrastructure/TestDatabaseInitializer.cs` | Truncate the new table |
| `backend.Tests/TimeEntryMinutesTests.cs` | Formula unit tests |
| `backend.Tests/IntegrationTests/TimeEntryMinutesIntegrationTests.cs` | Weekly timesheet matches the helper |
| `backend.Tests/PayrollWeekTests.cs` | Send window unit tests |
| `backend.Tests/PayrollSummaryMailTests.cs` | Subject and body unit tests |
| `backend.Tests/IntegrationTests/PayrollSummaryIntegrationTests.cs` | Spec scenarios 1–7 |
| `backend.Tests/PayrollSummarySchedulerTests.cs` | Environment gate and startup check |
| `docs/superpowers/specs/2026-10-24-monday-payroll-summary-design.md` | Mark approved once implemented |
| `docs/backlog/reporting-and-notifications.md` | Point item 1 at this plan |

---

### Task 1: Shared minute helper

**Files:**
- Create: `backend/Services/TimeEntryMinutes.cs`
- Modify: `backend/Services/TimeEntryService.cs` (replace `ComputeEntryMinutes` and delete the static `SumBreakMinutes` only)
- Create: `backend.Tests/TimeEntryMinutesTests.cs`
- Create: `backend.Tests/IntegrationTests/TimeEntryMinutesIntegrationTests.cs`

**Interfaces:**
- Consumes: `TimeEntry`, `TimeBreak`
- Produces: `TimeEntryMinuteResult TimeEntryMinutes.Compute(TimeEntry entry, IReadOnlyList<TimeBreak>? breaks, DateTimeOffset now)` with `WorkMinutes` and `BreakMinutes`

Do not change `SumBreakMinutesAsync` or `DashboardService`.

- [ ] **Step 1: Write the failing unit tests**

Create `backend.Tests/TimeEntryMinutesTests.cs`:

```csharp
using VisionPaint.Models;
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class TimeEntryMinutesTests
{
    [Fact]
    public void Closed_entry_uses_stored_break_minutes()
    {
        var clockIn = new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero);
        var entry = new TimeEntry
        {
            ClockInAt = clockIn,
            ClockOutAt = clockIn.AddMinutes(90),
            BreakMinutes = 30
        };

        var result = TimeEntryMinutes.Compute(entry, breaks: null, now: clockIn.AddHours(5));

        Assert.Equal(60, result.WorkMinutes);
        Assert.Equal(30, result.BreakMinutes);
    }

    [Fact]
    public void Closed_entry_floors_work_at_zero_when_break_exceeds_span()
    {
        var clockIn = new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero);
        var entry = new TimeEntry
        {
            ClockInAt = clockIn,
            ClockOutAt = clockIn.AddMinutes(10),
            BreakMinutes = 40
        };

        var result = TimeEntryMinutes.Compute(entry, breaks: null, now: clockIn);

        Assert.Equal(0, result.WorkMinutes);
        Assert.Equal(40, result.BreakMinutes);
    }

    [Fact]
    public void Open_entry_counts_through_now_and_open_breaks()
    {
        var now = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero);
        var entry = new TimeEntry
        {
            ClockInAt = now.AddMinutes(-90),
            ClockOutAt = null,
            BreakMinutes = 0
        };
        var breaks = new List<TimeBreak>
        {
            new()
            {
                BreakStartAt = now.AddMinutes(-15),
                BreakEndAt = null,
                BreakType = "rest"
            }
        };

        var result = TimeEntryMinutes.Compute(entry, breaks, now);

        Assert.Equal(75, result.WorkMinutes);
        Assert.Equal(15, result.BreakMinutes);
    }
}
```

- [ ] **Step 2: Run the unit tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~TimeEntryMinutesTests`

Expected: build fails because `TimeEntryMinutes` does not exist.

- [ ] **Step 3: Add the helper and switch the weekly timesheet**

Create `backend/Services/TimeEntryMinutes.cs`:

```csharp
using VisionPaint.Models;

namespace VisionPaint.Services;

public readonly record struct TimeEntryMinuteResult(int WorkMinutes, int BreakMinutes);

public static class TimeEntryMinutes
{
    public static TimeEntryMinuteResult Compute(
        TimeEntry entry,
        IReadOnlyList<TimeBreak>? breaks,
        DateTimeOffset now)
    {
        if (entry.ClockOutAt is null)
        {
            var breakMinutes = SumBreaks(breaks, now);
            var partial = (int)Math.Max(0, (now - entry.ClockInAt).TotalMinutes);
            return new TimeEntryMinuteResult(Math.Max(0, partial - breakMinutes), breakMinutes);
        }

        var span = (int)Math.Max(0, (entry.ClockOutAt.Value - entry.ClockInAt).TotalMinutes);
        return new TimeEntryMinuteResult(Math.Max(0, span - entry.BreakMinutes), entry.BreakMinutes);
    }

    private static int SumBreaks(IReadOnlyList<TimeBreak>? breaks, DateTimeOffset now)
    {
        if (breaks is null)
        {
            return 0;
        }

        return breaks.Sum(b => (int)Math.Max(0, ((b.BreakEndAt ?? now) - b.BreakStartAt).TotalMinutes));
    }
}
```

In `TimeEntryService`, replace every `ComputeEntryMinutes(entry, breaks, now)` call with `TimeEntryMinutes.Compute(entry, breaks, now)`. Delete the private `EntryMinutes` record, the private `ComputeEntryMinutes` method, and the private static `SumBreakMinutes` method. Leave `SumBreakMinutesAsync` in place.

- [ ] **Step 4: Write the weekly-timesheet match test**

Create `backend.Tests/IntegrationTests/TimeEntryMinutesIntegrationTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class TimeEntryMinutesIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public TimeEntryMinutesIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Weekly_timesheet_matches_shared_helper_for_closed_and_open_entries()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "Owner One",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        using var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Minute job" });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.Database.ConnectionString).Options);
        var personId = await db.People.Where(p => p.AuthUserId == owner.User.AuthUserId).Select(p => p.Id).SingleAsync();

        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var openClockIn = DateTimeOffset.UtcNow.AddMinutes(-90);
        var openLocalDate = TimeZoneHelper.ToLocalDate(openClockIn, tz);
        var weekStart = openLocalDate.AddDays(-(int)openLocalDate.DayOfWeek);
        var closedClockIn = TimeZoneHelper.ToUtcStartOfDay(openLocalDate, tz).AddMinutes(1);
        var closed = new TimeEntry
        {
            JobId = job!.Id,
            PersonId = personId,
            ClockInAt = closedClockIn,
            ClockOutAt = closedClockIn.AddMinutes(90),
            BreakMinutes = 30,
            CreatedAt = closedClockIn,
            UpdatedAt = closedClockIn
        };
        var open = new TimeEntry
        {
            JobId = job.Id,
            PersonId = personId,
            ClockInAt = openClockIn,
            BreakMinutes = 0,
            CreatedAt = openClockIn,
            UpdatedAt = openClockIn
        };
        db.TimeEntries.AddRange(closed, open);
        await db.SaveChangesAsync();
        db.TimeBreaks.Add(new TimeBreak
        {
            TimeEntryId = open.Id,
            BreakStartAt = openClockIn.AddMinutes(75),
            BreakEndAt = null,
            BreakType = "rest"
        });
        await db.SaveChangesAsync();

        var before = DateTimeOffset.UtcNow;
        var sheet = await _fixture.Client.GetFromJsonAsync<WeeklyTimesheetDto>(
            $"/api/time/weekly?weekStart={weekStart:yyyy-MM-dd}&personId={personId}");
        var after = DateTimeOffset.UtcNow;

        var closedMinutes = TimeEntryMinutes.Compute(closed, breaks: null, now: before);
        var openBefore = TimeEntryMinutes.Compute(open, new[]
        {
            new TimeBreak { BreakStartAt = openClockIn.AddMinutes(75), BreakEndAt = null, BreakType = "rest" }
        }, before);
        var openAfter = TimeEntryMinutes.Compute(open, new[]
        {
            new TimeBreak { BreakStartAt = openClockIn.AddMinutes(75), BreakEndAt = null, BreakType = "rest" }
        }, after);

        Assert.NotNull(sheet);
        Assert.Equal(60, closedMinutes.WorkMinutes);
        Assert.InRange(
            sheet!.WeekTotalWorkMinutes,
            closedMinutes.WorkMinutes + openBefore.WorkMinutes,
            closedMinutes.WorkMinutes + openAfter.WorkMinutes);
        Assert.InRange(
            sheet.WeekTotalBreakMinutes,
            closedMinutes.BreakMinutes + openBefore.BreakMinutes,
            closedMinutes.BreakMinutes + openAfter.BreakMinutes);
    }
}
```

- [ ] **Step 5: Run both test classes**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~TimeEntryMinutes`

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add backend/Services/TimeEntryMinutes.cs backend/Services/TimeEntryService.cs backend.Tests/TimeEntryMinutesTests.cs backend.Tests/IntegrationTests/TimeEntryMinutesIntegrationTests.cs
git commit -m "Share time-entry minute math with the weekly timesheet"
```

---

### Task 2: Week window and email text

**Files:**
- Create: `backend/Services/PayrollWeek.cs`
- Create: `backend/Services/PayrollSummaryMail.cs`
- Create: `backend.Tests/PayrollWeekTests.cs`
- Create: `backend.Tests/PayrollSummaryMailTests.cs`

**Interfaces:**
- Consumes: `TimeZoneHelper.SundayOfWeek`, `TimeZoneHelper.FormatDayLabel`
- Produces:
  - `bool PayrollWeek.TryGetSendableWeek(DateTimeOffset utcNow, TimeZoneInfo timeZone, out DateOnly weekStart)`
  - `string PayrollSummaryMail.FormatHours(int minutes)`
  - `string PayrollSummaryMail.SubjectFor(DateOnly weekStart, DateOnly weekEnd)`
  - `(string Subject, string Html, string Text) PayrollSummaryMail.Render(PayrollSummaryModel model)`
  - `PayrollSummaryModel`, `PayrollPersonModel`, `PayrollDayModel`

- [ ] **Step 1: Write the failing tests**

Create `backend.Tests/PayrollWeekTests.cs`:

```csharp
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class PayrollWeekTests
{
    private static readonly TimeZoneInfo Denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

    private static DateTimeOffset AtDenver(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified),
            Denver);

    [Theory]
    [InlineData(2026, 10, 25, 20, 0)]
    [InlineData(2026, 10, 26, 6, 59)]
    public void Before_monday_0700_is_not_sendable(int year, int month, int day, int hour, int minute)
    {
        var sendable = PayrollWeek.TryGetSendableWeek(AtDenver(year, month, day, hour, minute), Denver, out _);
        Assert.False(sendable);
    }

    [Fact]
    public void Monday_0700_selects_the_previous_sunday()
    {
        var sendable = PayrollWeek.TryGetSendableWeek(AtDenver(2026, 10, 26, 7, 0), Denver, out var weekStart);

        Assert.True(sendable);
        Assert.Equal(new DateOnly(2026, 10, 18), weekStart);
    }

    [Fact]
    public void Later_in_the_same_week_keeps_that_sunday()
    {
        var sendable = PayrollWeek.TryGetSendableWeek(AtDenver(2026, 10, 27, 15, 0), Denver, out var weekStart);

        Assert.True(sendable);
        Assert.Equal(new DateOnly(2026, 10, 18), weekStart);
    }

    [Fact]
    public void Next_monday_0700_moves_to_the_newer_week()
    {
        var sendable = PayrollWeek.TryGetSendableWeek(AtDenver(2026, 11, 2, 7, 0), Denver, out var weekStart);

        Assert.True(sendable);
        Assert.Equal(new DateOnly(2026, 10, 25), weekStart);
    }
}
```

Create `backend.Tests/PayrollSummaryMailTests.cs`:

```csharp
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class PayrollSummaryMailTests
{
    [Fact]
    public void FormatHours_uses_one_decimal_from_minutes()
    {
        Assert.Equal("0.0", PayrollSummaryMail.FormatHours(0));
        Assert.Equal("1.5", PayrollSummaryMail.FormatHours(90));
    }

    [Fact]
    public void Subject_uses_one_year_inside_a_calendar_year()
    {
        var subject = PayrollSummaryMail.SubjectFor(new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 24));
        Assert.Equal("Payroll summary for Oct 18–Oct 24, 2026", subject);
    }

    [Fact]
    public void Subject_repeats_the_year_when_the_week_crosses_years()
    {
        var subject = PayrollSummaryMail.SubjectFor(new DateOnly(2026, 12, 27), new DateOnly(2027, 1, 2));
        Assert.Equal("Payroll summary for Dec 27, 2026–Jan 2, 2027", subject);
    }

    [Fact]
    public void Render_lists_seven_days_zero_hours_and_an_open_shift()
    {
        var days = Enumerable.Range(0, 7)
            .Select(i => new PayrollDayModel(new DateOnly(2026, 10, 18).AddDays(i), 0, 0))
            .ToArray();
        days[6] = days[6] with { WorkMinutes = 2910 };

        var model = new PayrollSummaryModel(
            "VisionPaint",
            new DateOnly(2026, 10, 18),
            new DateOnly(2026, 10, 24),
            3000,
            0,
            new[]
            {
                new PayrollPersonModel("Open Worker", 2910, 0, true, days)
            });

        var (subject, html, text) = PayrollSummaryMail.Render(model);

        Assert.Equal("Payroll summary for Oct 18–Oct 24, 2026", subject);
        Assert.Contains("VisionPaint", text);
        Assert.Contains("Open Worker", text);
        Assert.Contains("Work 48.5  Break 0.0  Still clocked in", text);
        Assert.Contains("Sun 10/18  Work 0.0  Break 0.0", text);
        Assert.Contains("Sat 10/24  Work 48.5  Break 0.0", text);
        Assert.Contains("Company total", text);
        var afterTotal = text.Split("Company total")[1];
        Assert.Contains("Work 50.0", afterTotal);
        Assert.DoesNotContain("Work 50.0", text.Split("Company total")[0]);
        Assert.Contains("Still clocked in", html);
        Assert.Contains("48.5", html);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~PayrollWeekTests|FullyQualifiedName~PayrollSummaryMailTests"`

Expected: build fails because `PayrollWeek` and `PayrollSummaryMail` do not exist.

- [ ] **Step 3: Implement the week window and the mail renderer**

Create `backend/Services/PayrollWeek.cs`:

```csharp
namespace VisionPaint.Services;

public static class PayrollWeek
{
    public static bool TryGetSendableWeek(DateTimeOffset utcNow, TimeZoneInfo timeZone, out DateOnly weekStart)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var localDate = DateOnly.FromDateTime(local.DateTime);
        var thisSunday = TimeZoneHelper.SundayOfWeek(localDate);
        weekStart = thisSunday.AddDays(-7);
        var saturday = weekStart.AddDays(6);
        var monday = saturday.AddDays(2);
        var sendOpensLocal = DateTime.SpecifyKind(monday.ToDateTime(new TimeOnly(7, 0)), DateTimeKind.Unspecified);
        var sendOpensUtc = TimeZoneInfo.ConvertTimeToUtc(sendOpensLocal, timeZone);
        return utcNow >= sendOpensUtc;
    }
}
```

Create `backend/Services/PayrollSummaryMail.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Text;

namespace VisionPaint.Services;

public sealed record PayrollDayModel(DateOnly Date, int WorkMinutes, int BreakMinutes);

public sealed record PayrollPersonModel(
    string Name,
    int WorkMinutes,
    int BreakMinutes,
    bool StillClockedIn,
    IReadOnlyList<PayrollDayModel> Days);

public sealed record PayrollSummaryModel(
    string CompanyName,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int CompanyWorkMinutes,
    int CompanyBreakMinutes,
    IReadOnlyList<PayrollPersonModel> People);

public static class PayrollSummaryMail
{
    public static string FormatHours(int minutes) =>
        (minutes / 60m).ToString("0.0", CultureInfo.InvariantCulture);

    public static string SubjectFor(DateOnly weekStart, DateOnly weekEnd)
    {
        var culture = CultureInfo.InvariantCulture;
        if (weekStart.Year == weekEnd.Year)
        {
            return $"Payroll summary for {weekStart.ToString("MMM d", culture)}–{weekEnd.ToString("MMM d", culture)}, {weekEnd.Year}";
        }

        return $"Payroll summary for {weekStart.ToString("MMM d, yyyy", culture)}–{weekEnd.ToString("MMM d, yyyy", culture)}";
    }

    public static (string Subject, string Html, string Text) Render(PayrollSummaryModel model)
    {
        var subject = SubjectFor(model.WeekStart, model.WeekEnd);
        var text = new StringBuilder();
        text.AppendLine(model.CompanyName);
        text.AppendLine(subject);
        text.AppendLine();
        foreach (var person in model.People)
        {
            text.AppendLine(person.Name);
            text.Append("Work ").Append(FormatHours(person.WorkMinutes));
            text.Append("  Break ").Append(FormatHours(person.BreakMinutes));
            if (person.StillClockedIn)
            {
                text.Append("  Still clocked in");
            }

            text.AppendLine();
            foreach (var day in person.Days)
            {
                text.Append(TimeZoneHelper.FormatDayLabel(day.Date));
                text.Append("  Work ").Append(FormatHours(day.WorkMinutes));
                text.Append("  Break ").Append(FormatHours(day.BreakMinutes));
                text.AppendLine();
            }

            text.AppendLine();
        }

        text.AppendLine("Company total");
        text.Append("Work ").Append(FormatHours(model.CompanyWorkMinutes));
        text.Append("  Break ").Append(FormatHours(model.CompanyBreakMinutes));
        text.AppendLine();

        var html = new StringBuilder();
        html.Append("<p>").Append(WebUtility.HtmlEncode(model.CompanyName)).Append("</p>");
        html.Append("<p>").Append(WebUtility.HtmlEncode(subject)).Append("</p><table>");
        foreach (var person in model.People)
        {
            html.Append("<tr><th>").Append(WebUtility.HtmlEncode(person.Name)).Append("</th>");
            html.Append("<td>Work ").Append(FormatHours(person.WorkMinutes)).Append("</td>");
            html.Append("<td>Break ").Append(FormatHours(person.BreakMinutes)).Append("</td>");
            if (person.StillClockedIn)
            {
                html.Append("<td>Still clocked in</td>");
            }

            html.Append("</tr>");
            foreach (var day in person.Days)
            {
                html.Append("<tr><td>").Append(WebUtility.HtmlEncode(TimeZoneHelper.FormatDayLabel(day.Date))).Append("</td>");
                html.Append("<td>Work ").Append(FormatHours(day.WorkMinutes)).Append("</td>");
                html.Append("<td>Break ").Append(FormatHours(day.BreakMinutes)).Append("</td></tr>");
            }
        }

        html.Append("<tr><th>Company total</th><td>Work ");
        html.Append(FormatHours(model.CompanyWorkMinutes));
        html.Append("</td><td>Break ");
        html.Append(FormatHours(model.CompanyBreakMinutes));
        html.Append("</td></tr></table>");
        return (subject, html.ToString(), text.ToString());
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~PayrollWeekTests|FullyQualifiedName~PayrollSummaryMailTests"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/Services/PayrollWeek.cs backend/Services/PayrollSummaryMail.cs backend.Tests/PayrollWeekTests.cs backend.Tests/PayrollSummaryMailTests.cs
git commit -m "Add payroll week window and summary email text"
```

---

### Task 3: Claim table

**Files:**
- Create: `database/migrations/20261024_170000_payroll_summary_send.sql`
- Modify: `database/schema.sql` (append after the checklist indexes)
- Create: `backend/Models/PayrollSummarySend.cs`
- Modify: `backend/Data/AppDbContext.cs`
- Modify: `backend.Tests/Infrastructure/TestDatabaseInitializer.cs`

**Interfaces:**
- Consumes: nothing from Tasks 1–2
- Produces: `AppDbContext.PayrollSummarySends` and `PayrollSummarySend` with `CompanyId`, `WeekStart`, `ClaimedAt`, `SentAt`

- [ ] **Step 1: Add the migration and the schema draft**

`database/migrations/20261024_170000_payroll_summary_send.sql`:

```sql
create table if not exists public.payroll_summary_send (
    company_id integer not null references public.company (id) on delete cascade,
    week_start date not null,
    claimed_at timestamptz not null,
    sent_at timestamptz,
    primary key (company_id, week_start)
);
```

Append the same statement to `database/schema.sql`, using unqualified table names to match that file (`references company (id)`).

- [ ] **Step 2: Map the entity**

Create `backend/Models/PayrollSummarySend.cs`:

```csharp
namespace VisionPaint.Models;

public sealed class PayrollSummarySend
{
    public int CompanyId { get; set; }

    public DateOnly WeekStart { get; set; }

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
```

In `AppDbContext`, add `public DbSet<PayrollSummarySend> PayrollSummarySends => Set<PayrollSummarySend>();` and this configuration inside `OnModelCreating`:

```csharp
modelBuilder.Entity<PayrollSummarySend>(entity =>
{
    entity.ToTable("payroll_summary_send");
    entity.HasKey(send => new { send.CompanyId, send.WeekStart });
    entity.Property(send => send.CompanyId).HasColumnName("company_id");
    entity.Property(send => send.WeekStart).HasColumnName("week_start");
    entity.Property(send => send.ClaimedAt).HasColumnName("claimed_at");
    entity.Property(send => send.SentAt).HasColumnName("sent_at");
});
```

- [ ] **Step 3: Truncate the table in test reset**

In `TestDatabaseInitializer.ResetAsync`, add `public.payroll_summary_send` as the first table in the `truncate table` list.

- [ ] **Step 4: Build**

Run: `dotnet build backend/VisionPaint.csproj --nologo`

Expected: build succeeds. If `VisionPaint.exe` is locked by a running API, stop that process and build again. The compile must not report a C# error.

- [ ] **Step 5: Commit**

```bash
git add database/migrations/20261024_170000_payroll_summary_send.sql database/schema.sql backend/Models/PayrollSummarySend.cs backend/Data/AppDbContext.cs backend.Tests/Infrastructure/TestDatabaseInitializer.cs
git commit -m "Add payroll summary send claim table"
```

---

### Task 4: Send the summary

**Files:**
- Create: `backend/Services/IPayrollSummaryService.cs`
- Create: `backend/Services/PayrollSummaryService.cs`
- Create: `backend.Tests/IntegrationTests/PayrollSummaryIntegrationTests.cs`
- Modify: `backend/Program.cs` (register `TimeProvider` and `PayrollSummaryService` only; the scheduler is Task 5)

**Interfaces:**
- Consumes: `TimeEntryMinutes.Compute`, `PayrollWeek.TryGetSendableWeek`, `PayrollSummaryMail.Render`, `PayrollSummarySend`, `IEmailSender`
- Produces: `IPayrollSummaryService.SendDueSummariesAsync(CancellationToken cancellationToken)` implemented by `PayrollSummaryService`

- [ ] **Step 1: Write the failing integration tests**

Add `backend.Tests/Infrastructure/FixedTimeProvider.cs`:

```csharp
namespace VisionPaint.Tests.Infrastructure;

public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        UtcNowValue = utcNow;
    }

    public DateTimeOffset UtcNowValue { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNowValue;
}
```

Create `backend.Tests/IntegrationTests/PayrollSummaryIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class PayrollSummaryIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private const string OwnerEmail = "owner-one@example.com";
    private const string AdminEmail = "admin-user@example.com";
    private static readonly TimeZoneInfo Denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
    private static readonly DateTimeOffset MondaySeven = AtDenver(2026, 10, 26, 7, 0);
    private readonly BackendIntegrationFixture _fixture;

    public PayrollSummaryIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() =>
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Before_monday_0700_does_not_send_or_claim()
    {
        var email = new ScriptedEmailSender();
        await using var db = CreateDb();
        await SendAt(db, email, AtDenver(2026, 10, 25, 20, 0));
        await SendAt(db, email, AtDenver(2026, 10, 26, 6, 59));

        Assert.Empty(email.Inner.Sent);
        Assert.Empty(await db.PayrollSummarySends.ToListAsync());
    }

    [Fact]
    public async Task Monday_0700_emails_owners_and_admins_with_daily_hours()
    {
        await SeedCompanyAsync();
        var email = new ScriptedEmailSender();
        await using var db = CreateDb();
        await SendAt(db, email, MondaySeven);

        var messages = email.Inner.Sent.ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Equal(
            new[] { AdminEmail, OwnerEmail },
            messages.Select(m => m.To).OrderBy(a => a).ToArray());
        Assert.All(messages, m => Assert.Equal("Payroll summary for Oct 18–Oct 24, 2026", m.Subject));
        var text = messages[0].Text!;
        Assert.Contains("Absent Worker", text);
        Assert.Contains("Flagged Person", text);
        Assert.Contains("Work 1.5", text);
        Assert.Contains("Work 48.5  Break 0.0  Still clocked in", text);
        Assert.Contains("Mon 10/19  Work 1.5  Break 0.0", text);
        Assert.Contains("Sat 10/24  Work 48.5  Break 0.0", text);
        Assert.Contains("Work 50.0", text.Split("Company total")[1]);
        Assert.DoesNotContain("Disabled Worker", text);
        Assert.Contains("Still clocked in", messages[0].Html);
        var claim = await db.PayrollSummarySends.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 18), claim.WeekStart);
        Assert.NotNull(claim.SentAt);
    }

    [Fact]
    public async Task Second_run_does_not_send_again()
    {
        await SeedCompanyAsync();
        var email = new ScriptedEmailSender();
        await using var db = CreateDb();
        await SendAt(db, email, MondaySeven);
        await SendAt(db, email, MondaySeven.AddHours(1));

        Assert.Equal(2, email.Inner.Sent.Count);
    }

    [Fact]
    public async Task All_sends_failing_releases_the_claim_and_a_later_run_sends()
    {
        await SeedCompanyAsync();
        var email = new ScriptedEmailSender { FailAll = true };
        await using var db = CreateDb();
        await SendAt(db, email, MondaySeven);
        Assert.Empty(await db.PayrollSummarySends.ToListAsync());

        email.FailAll = false;
        await SendAt(db, email, MondaySeven.AddMinutes(1));
        Assert.Equal(2, email.Inner.Sent.Count);
        Assert.NotNull((await db.PayrollSummarySends.SingleAsync()).SentAt);
    }

    [Fact]
    public async Task Partial_failure_marks_the_week_sent()
    {
        await SeedCompanyAsync();
        var email = new ScriptedEmailSender();
        email.FailFor.Add(AdminEmail);
        await using var db = CreateDb();
        await SendAt(db, email, MondaySeven);

        Assert.Equal(new[] { OwnerEmail }, email.Inner.Sent.Select(m => m.To).ToArray());
        Assert.NotNull((await db.PayrollSummarySends.SingleAsync()).SentAt);

        email.FailFor.Clear();
        await SendAt(db, email, MondaySeven.AddMinutes(1));
        Assert.Single(email.Inner.Sent);
    }

    [Fact]
    public async Task No_recipient_address_releases_the_claim()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "Owner One", OwnerEmail, "Password123!"));
        await using var db = CreateDb();
        var auth = await db.AuthUsers.SingleAsync(u => u.Id == owner.User.AuthUserId);
        auth.Email = " ";
        await db.SaveChangesAsync();

        var email = new ScriptedEmailSender();
        await SendAt(db, email, MondaySeven);

        Assert.Empty(email.Inner.Sent);
        Assert.Empty(await db.PayrollSummarySends.ToListAsync());
    }

    [Fact]
    public async Task Next_monday_sends_only_the_new_week()
    {
        await SeedCompanyAsync();
        var email = new ScriptedEmailSender();
        await using var db = CreateDb();
        var clock = new FixedTimeProvider(MondaySeven);
        var service = CreateService(db, email, clock);
        await service.SendDueSummariesAsync(CancellationToken.None);

        clock.UtcNowValue = AtDenver(2026, 11, 2, 7, 0);
        await service.SendDueSummariesAsync(CancellationToken.None);

        var subjects = email.Inner.Sent.Select(m => m.Subject).ToArray();
        Assert.Equal(4, subjects.Length);
        Assert.Equal(2, subjects.Count(s => s == "Payroll summary for Oct 25–Oct 31, 2026"));
        Assert.All(
            email.Inner.Sent.Where(m => m.Subject.Contains("Oct 25")),
            m => Assert.DoesNotContain("Still clocked in", m.Text));
        Assert.NotNull(await db.PayrollSummarySends.SingleAsync(s => s.WeekStart == new DateOnly(2026, 10, 25)));
    }

    [Fact]
    public async Task Fresh_claim_is_not_taken_over()
    {
        await SeedCompanyAsync();
        await using var db = CreateDb();
        db.PayrollSummarySends.Add(new PayrollSummarySend
        {
            CompanyId = 1,
            WeekStart = new DateOnly(2026, 10, 18),
            ClaimedAt = MondaySeven.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var email = new ScriptedEmailSender();
        await SendAt(db, email, MondaySeven);
        Assert.Empty(email.Inner.Sent);
    }

    [Fact]
    public async Task Stale_claim_is_taken_over_and_sent()
    {
        await SeedCompanyAsync();
        await using var db = CreateDb();
        db.PayrollSummarySends.Add(new PayrollSummarySend
        {
            CompanyId = 1,
            WeekStart = new DateOnly(2026, 10, 18),
            ClaimedAt = MondaySeven.AddMinutes(-16)
        });
        await db.SaveChangesAsync();

        var email = new ScriptedEmailSender();
        await SendAt(db, email, MondaySeven);
        Assert.Equal(2, email.Inner.Sent.Count);
        Assert.NotNull((await db.PayrollSummarySends.SingleAsync()).SentAt);
    }

    private async Task SendAt(AppDbContext db, ScriptedEmailSender email, DateTimeOffset utcNow)
    {
        var service = CreateService(db, email, new FixedTimeProvider(utcNow));
        await service.SendDueSummariesAsync(CancellationToken.None);
    }

    private static PayrollSummaryService CreateService(AppDbContext db, IEmailSender email, FixedTimeProvider clock) =>
        new(db, email, clock, NullLogger<PayrollSummaryService>.Instance);

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.Database.ConnectionString).Options);

    private async Task SeedCompanyAsync()
    {
        await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "Owner One", OwnerEmail, "Password123!"));

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var members = new (string Name, string Email, string Role, bool PersonActive, string Status)[]
        {
            ("Absent Worker", "absent-worker@example.com", "crew", true, "active"),
            ("Admin User", AdminEmail, "admin", true, "active"),
            ("Crew Worker", "crew-worker@example.com", "crew", true, "active"),
            ("Manager User", "manager-user@example.com", "manager", true, "active"),
            ("Open Worker", "open-worker@example.com", "crew", true, "active"),
            ("Flagged Person", "flagged@example.com", "crew", false, "active"),
            ("Disabled Worker", "disabled-worker@example.com", "crew", true, "disabled")
        };

        var people = new Dictionary<string, Person>();
        foreach (var member in members)
        {
            var auth = new AuthUser
            {
                Id = Guid.NewGuid(),
                Email = member.Email,
                PasswordHash = "not-used",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            var person = new Person
            {
                AuthUserId = auth.Id,
                Name = member.Name,
                Email = member.Email,
                IsActive = member.PersonActive,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.AuthUsers.Add(auth);
            db.People.Add(person);
            people[member.Name] = person;
        }

        await db.SaveChangesAsync();
        foreach (var member in members)
        {
            db.CompanyMembers.Add(new CompanyMember
            {
                CompanyId = 1,
                PersonId = people[member.Name].Id,
                Role = member.Role,
                Status = member.Status,
                JoinedAt = now
            });
        }

        var job = new Job
        {
            CompanyId = 1,
            Title = "Payroll job",
            Status = "in_progress",
            Priority = "normal",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var crewIn = AtDenver(2026, 10, 19, 8, 0);
        var openIn = AtDenver(2026, 10, 24, 6, 30);
        var disabledIn = AtDenver(2026, 10, 20, 8, 0);
        db.TimeEntries.AddRange(
            new TimeEntry
            {
                JobId = job.Id,
                PersonId = people["Crew Worker"].Id,
                ClockInAt = crewIn,
                ClockOutAt = crewIn.AddMinutes(90),
                BreakMinutes = 0,
                CreatedAt = crewIn,
                UpdatedAt = crewIn
            },
            new TimeEntry
            {
                JobId = job.Id,
                PersonId = people["Open Worker"].Id,
                ClockInAt = openIn,
                BreakMinutes = 0,
                CreatedAt = openIn,
                UpdatedAt = openIn
            },
            new TimeEntry
            {
                JobId = job.Id,
                PersonId = people["Disabled Worker"].Id,
                ClockInAt = disabledIn,
                ClockOutAt = disabledIn.AddMinutes(60),
                BreakMinutes = 0,
                CreatedAt = disabledIn,
                UpdatedAt = disabledIn
            });
        await db.SaveChangesAsync();
    }

    private static DateTimeOffset AtDenver(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified),
            Denver);

    private sealed class ScriptedEmailSender : IEmailSender
    {
        public RecordingEmailSender Inner { get; } = new();
        public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool FailAll { get; set; }

        public Task SendAsync(
            string toEmail,
            string subject,
            string htmlBody,
            string? textBody = null,
            CancellationToken cancellationToken = default)
        {
            if (FailAll || FailFor.Contains(toEmail))
            {
                throw new InvalidOperationException("Brevo rejected the message.");
            }

            return Inner.SendAsync(toEmail, subject, htmlBody, textBody, cancellationToken);
        }
    }
}
```

`PayrollSummaryService` must call `TimeProvider.GetUtcNow()` inside `SendDueSummariesAsync`, not in the constructor.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~PayrollSummaryIntegrationTests`

Expected: build fails because `PayrollSummaryService` does not exist.

- [ ] **Step 3: Implement the service**

Create `backend/Services/IPayrollSummaryService.cs`:

```csharp
namespace VisionPaint.Services;

public interface IPayrollSummaryService
{
    Task SendDueSummariesAsync(CancellationToken cancellationToken);
}
```

Create `backend/Services/PayrollSummaryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class PayrollSummaryService : IPayrollSummaryService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly TimeProvider _time;
    private readonly ILogger<PayrollSummaryService> _logger;

    public PayrollSummaryService(
        AppDbContext db,
        IEmailSender email,
        TimeProvider time,
        ILogger<PayrollSummaryService> logger)
    {
        _db = db;
        _email = email;
        _time = time;
        _logger = logger;
    }

    public async Task SendDueSummariesAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var companies = await _db.Companies.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var company in companies)
        {
            var timeZone = TimeZoneHelper.Resolve(company.Timezone);
            if (!PayrollWeek.TryGetSendableWeek(now, timeZone, out var weekStart))
            {
                continue;
            }

            if (!await TryClaimAsync(company.Id, weekStart, now, cancellationToken))
            {
                continue;
            }

            var summary = await BuildSummaryAsync(company, timeZone, weekStart, now, cancellationToken);
            var recipients = await LoadRecipientsAsync(company.Id, cancellationToken);
            if (recipients.Count == 0)
            {
                await ReleaseAsync(company.Id, weekStart, cancellationToken);
                continue;
            }

            var (subject, html, text) = PayrollSummaryMail.Render(summary);
            var sent = 0;
            foreach (var recipient in recipients)
            {
                try
                {
                    await _email.SendAsync(recipient, subject, html, text, cancellationToken);
                    sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Payroll summary email failed for {Recipient} company {CompanyId} week {WeekStart}",
                        recipient,
                        company.Id,
                        weekStart);
                }
            }

            if (sent == 0)
            {
                await ReleaseAsync(company.Id, weekStart, cancellationToken);
                continue;
            }

            await _db.PayrollSummarySends
                .Where(row => row.CompanyId == company.Id && row.WeekStart == weekStart)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SentAt, now), cancellationToken);
            _db.ChangeTracker.Clear();
            _logger.LogInformation(
                "Payroll summary sent for company {CompanyId} week {WeekStart} to {RecipientCount} recipients",
                company.Id,
                weekStart,
                sent);
        }
    }

    private async Task<bool> TryClaimAsync(
        int companyId,
        DateOnly weekStart,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _db.PayrollSummarySends
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.CompanyId == companyId && row.WeekStart == weekStart, cancellationToken);

        if (existing is { SentAt: not null })
        {
            return false;
        }

        if (existing is not null && existing.ClaimedAt > now.AddMinutes(-15))
        {
            return false;
        }

        if (existing is null)
        {
            _db.PayrollSummarySends.Add(new PayrollSummarySend
            {
                CompanyId = companyId,
                WeekStart = weekStart,
                ClaimedAt = now
            });
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                return false;
            }
        }

        var claimedAt = existing.ClaimedAt;
        var updated = await _db.PayrollSummarySends
            .Where(row => row.CompanyId == companyId
                && row.WeekStart == weekStart
                && row.SentAt == null
                && row.ClaimedAt == claimedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ClaimedAt, now), cancellationToken);
        return updated == 1;
    }

    private async Task ReleaseAsync(int companyId, DateOnly weekStart, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Payroll summary was not sent for company {CompanyId} week {WeekStart}",
            companyId,
            weekStart);
        await _db.PayrollSummarySends
            .Where(row => row.CompanyId == companyId && row.WeekStart == weekStart && row.SentAt == null)
            .ExecuteDeleteAsync(cancellationToken);
        _db.ChangeTracker.Clear();
    }

    private async Task<PayrollSummaryModel> BuildSummaryAsync(
        Company company,
        TimeZoneInfo timeZone,
        DateOnly weekStart,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var weekEnd = weekStart.AddDays(6);
        var queryFrom = TimeZoneHelper.ToUtcStartOfDay(weekStart, timeZone).AddDays(-1);
        var queryTo = TimeZoneHelper.ToUtcEndOfDay(weekEnd, timeZone).AddDays(1);
        var weekDays = Enumerable.Range(0, 7).Select(offset => weekStart.AddDays(offset)).ToHashSet();

        var people = await (
            from member in _db.CompanyMembers.AsNoTracking()
            join person in _db.People.AsNoTracking() on member.PersonId equals person.Id
            where member.CompanyId == company.Id && member.Status == "active"
            orderby person.Name, person.Id
            select new { person.Id, person.Name }
        ).ToListAsync(cancellationToken);

        var entries = await _db.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.ClockInAt >= queryFrom && entry.ClockInAt <= queryTo)
            .Join(
                _db.Jobs.AsNoTracking().Where(job => job.CompanyId == company.Id),
                entry => entry.JobId,
                job => job.Id,
                (entry, _) => entry)
            .ToListAsync(cancellationToken);
        entries = entries
            .Where(entry => weekDays.Contains(TimeZoneHelper.ToLocalDate(entry.ClockInAt, timeZone)))
            .ToList();

        var entryIds = entries.Select(entry => entry.Id).ToList();
        var breaks = await _db.TimeBreaks
            .AsNoTracking()
            .Where(row => entryIds.Contains(row.TimeEntryId))
            .ToListAsync(cancellationToken);

        var personRows = new List<PayrollPersonModel>();
        foreach (var person in people)
        {
            var personEntries = entries.Where(entry => entry.PersonId == person.Id).ToList();
            var dayMinutes = weekDays.ToDictionary(day => day, _ => (Work: 0, Break: 0));
            foreach (var entry in personEntries)
            {
                var entryBreaks = breaks.Where(row => row.TimeEntryId == entry.Id).ToList();
                var minutes = TimeEntryMinutes.Compute(entry, entryBreaks, now);
                var day = TimeZoneHelper.ToLocalDate(entry.ClockInAt, timeZone);
                var current = dayMinutes[day];
                dayMinutes[day] = (current.Work + minutes.WorkMinutes, current.Break + minutes.BreakMinutes);
            }

            var days = Enumerable.Range(0, 7)
                .Select(offset => weekStart.AddDays(offset))
                .Select(date => new PayrollDayModel(date, dayMinutes[date].Work, dayMinutes[date].Break))
                .ToList();
            personRows.Add(new PayrollPersonModel(
                person.Name,
                days.Sum(day => day.WorkMinutes),
                days.Sum(day => day.BreakMinutes),
                personEntries.Any(entry => entry.ClockOutAt is null),
                days));
        }

        return new PayrollSummaryModel(
            company.Name,
            weekStart,
            weekEnd,
            personRows.Sum(person => person.WorkMinutes),
            personRows.Sum(person => person.BreakMinutes),
            personRows);
    }

    private async Task<List<string>> LoadRecipientsAsync(int companyId, CancellationToken cancellationToken)
    {
        var emails = await (
            from member in _db.CompanyMembers.AsNoTracking()
            join person in _db.People.AsNoTracking() on member.PersonId equals person.Id
            join auth in _db.AuthUsers.AsNoTracking() on person.AuthUserId equals auth.Id
            where member.CompanyId == companyId
                && member.Status == "active"
                && auth.IsActive
                && (member.Role.ToLower() == "owner" || member.Role.ToLower() == "admin")
            select auth.Email
        ).ToListAsync(cancellationToken);

        return emails
            .Select(email => email.Trim())
            .Where(email => email.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```

Register in `Program.cs` next to the email sender:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PayrollSummaryService>();
builder.Services.AddScoped<IPayrollSummaryService>(sp => sp.GetRequiredService<PayrollSummaryService>());
```

- [ ] **Step 4: Run the integration tests**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~PayrollSummaryIntegrationTests`

Expected: PASS. Requires `VISIONPAINT_TEST_PGADMIN` the same way the other integration tests do.

- [ ] **Step 5: Commit**

```bash
git add backend/Services/IPayrollSummaryService.cs backend/Services/PayrollSummaryService.cs backend/Program.cs backend.Tests/Infrastructure/FixedTimeProvider.cs backend.Tests/IntegrationTests/PayrollSummaryIntegrationTests.cs
git commit -m "Send the Monday payroll summary once per company week"
```

---

### Task 5: Production scheduler

**Files:**
- Create: `backend/Services/PayrollSummaryHost.cs`
- Create: `backend/Services/PayrollSummaryScheduler.cs`
- Modify: `backend/Program.cs`
- Create: `backend.Tests/PayrollSummarySchedulerTests.cs`
- Modify: `docs/superpowers/specs/2026-10-24-monday-payroll-summary-design.md`
- Modify: `docs/backlog/reporting-and-notifications.md`

**Interfaces:**
- Consumes: `IPayrollSummaryService.SendDueSummariesAsync`
- Produces: `bool PayrollSummaryHost.ShouldRun(IHostEnvironment environment)` and `PayrollSummaryScheduler` registered only when that returns true

- [ ] **Step 1: Write the failing scheduler tests**

Create `backend.Tests/PayrollSummarySchedulerTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class PayrollSummarySchedulerTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("E2E", false)]
    public void ShouldRun_is_production_only(string environmentName, bool expected)
    {
        var environment = new StubEnvironment { EnvironmentName = environmentName };
        Assert.Equal(expected, PayrollSummaryHost.ShouldRun(environment));
    }

    [Fact]
    public async Task Scheduler_checks_once_before_waiting()
    {
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton<IPayrollSummaryService>(new CallbackSummaryService(() =>
        {
            calls++;
            gate.TrySetResult();
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider();
        var scheduler = new PayrollSummaryScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PayrollSummaryScheduler>.Instance,
            TimeSpan.FromHours(1));

        await scheduler.StartAsync(CancellationToken.None);
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        await scheduler.StopAsync(CancellationToken.None);
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "VisionPaint.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CallbackSummaryService : IPayrollSummaryService
    {
        private readonly Func<Task> _send;

        public CallbackSummaryService(Func<Task> send)
        {
            _send = send;
        }

        public Task SendDueSummariesAsync(CancellationToken cancellationToken) => _send();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~PayrollSummarySchedulerTests`

Expected: build fails because `PayrollSummaryHost` and `PayrollSummaryScheduler` do not exist.

- [ ] **Step 3: Implement the gate and the loop**

Create `backend/Services/PayrollSummaryHost.cs`:

```csharp
using Microsoft.Extensions.Hosting;

namespace VisionPaint.Services;

public static class PayrollSummaryHost
{
    public static bool ShouldRun(IHostEnvironment environment) => environment.IsProduction();
}
```

Create `backend/Services/PayrollSummaryScheduler.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace VisionPaint.Services;

public sealed class PayrollSummaryScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PayrollSummaryScheduler> _logger;
    private readonly TimeSpan _interval;

    public PayrollSummaryScheduler(
        IServiceScopeFactory scopes,
        ILogger<PayrollSummaryScheduler> logger,
        TimeSpan? interval = null)
    {
        _scopes = scopes;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPayrollSummaryService>();
                await service.SendDueSummariesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Payroll summary check failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
```

In `Program.cs`, after the `PayrollSummaryService` registration from Task 4:

```csharp
if (PayrollSummaryHost.ShouldRun(builder.Environment))
{
    builder.Services.AddHostedService<PayrollSummaryScheduler>();
}
```

- [ ] **Step 4: Run the scheduler tests and the payroll integration tests**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~PayrollSummary`

Expected: PASS

- [ ] **Step 5: Point the docs at the finished plan**

In the spec, change the status line to `Approved for implementation planning (2026-10-24)`.

In `docs/backlog/reporting-and-notifications.md`, under item 1, add:

`Plan: [2026-10-24-monday-payroll-summary.md](../superpowers/plans/2026-10-24-monday-payroll-summary.md)`

- [ ] **Step 6: Commit**

```bash
git add backend/Services/PayrollSummaryHost.cs backend/Services/PayrollSummaryScheduler.cs backend/Program.cs backend.Tests/PayrollSummarySchedulerTests.cs docs/superpowers/specs/2026-10-24-monday-payroll-summary-design.md docs/backlog/reporting-and-notifications.md
git commit -m "Run the payroll summary check in production"
```
