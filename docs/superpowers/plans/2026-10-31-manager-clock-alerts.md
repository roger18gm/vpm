# Manager clock alerts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Workspace:** Implement in the current workspace. Do not create a git worktree.
>
> **Commits:** Skip every `git commit` step unless the human has asked for commits in this session.

**Goal:** Email each job's lead or supervisor when a break runs past its threshold, or when someone is still clocked in after 7:00 PM on a weekday, then send one weekday-morning follow-up if it is still true.

**Architecture:** A production-only background timer wakes every five minutes and calls `ClockAlertService`. The service claims one company slot (`morning` or `evening`), builds one email per recipient, and records each break or shift in `clock_alert_send`. Tests call the service with a fixed `TimeProvider` and do not wait for the timer.

**Tech Stack:** ASP.NET Core, EF Core, PostgreSQL, xUnit, existing `IEmailSender`, `RecordingEmailSender`, and `FixedTimeProvider`

**Spec:** [docs/superpowers/specs/2026-10-31-manager-clock-alerts-design.md](../specs/2026-10-31-manager-clock-alerts-design.md)

## Global Constraints

- Recipients are current `job_assignment` rows (`unassigned_at` null) with `assignment_role` `lead` or `supervisor`, ordinal-ignore-case, excluding the person the alert is about. They must have `company_member.status = active`, an active `auth_user`, and a non-blank trimmed `auth_user.email`.
- If no job recipient remains, fall back to active owners and admins under those same email rules. Drop the person the alert is about from that fallback too. Company role `manager` is not a fallback. Assignment roles `crew` and `prep` are not job recipients.
- One message per recipient person per slot. An owner who is also a lead gets one email containing the jobs they lead and the fallback jobs. Two people who share an email each get their own message.
- A long break is an open `time_break` (`break_end_at` null) on an open `time_entry` (`clock_out_at` null). `lunch` is due at 45 whole minutes, ordinal-ignore-case. `rest` and `other` are due at 20. Whole minutes truncate toward zero with `(int)` of total minutes.
- After-hours is an open shift on a weekday `evening` pass only. Saturday and Sunday passes do not add `Clocked in since` and do not insert an after-hours row.
- Slots, company local time: before 7:00 AM is the previous date `evening`; 7:00 AM inclusive until 7:00 PM is that date `morning`; 7:00 PM inclusive is that date `evening`. Unknown timezones use `TimeZoneHelper.Resolve` (`America/Denver`).
- A pass whose slot row already has `sent_at` does nothing. A break that becomes due at 7:30 PM waits for the next slot.
- Follow-up instant: convert `first_sent_at` to local time. If that time is at or after 7:00 AM, start the next calendar date; otherwise start that same date. Move forward over Saturday and Sunday. The instant is 7:00 AM on the date that remains.
- A weekday morning pass sends a follow-up whose instant equals that slot's 7:00 AM when the break is still open, on an open shift, and still at or past its threshold, or when the after-hours shift is still open. A follow-up whose instant is earlier, or whose condition is gone, sets `follow_up_sent_at` and is left out of the email. Weekend passes do not send follow-ups. Evening passes do not send or mark follow-ups.
- Weekday morning does not create a first after-hours row.
- Subject is `Clock alerts for {MMM d}, 7:00 AM` or `7:00 PM`, invariant culture, naming the slot's 7:00 even when the pass runs later. Break length uses the actual send instant. `Clocked in since {h:mm tt}` uses company-local `clock_in_at`. The word is `minutes` for every count.
- Body order: company name, subject, jobs by title ordinal-ignore-case then job id, people by name ordinal-ignore-case then person id. Break lines ordered by `break_start_at` then break id, then the clocked-in line. Link is `{FrontendBaseUrl}/jobs/{id}` using `AppOptions.FrontendBaseUrl.TrimEnd('/')`.
- Replace `\r\n`, then `\r`, then `\n` in a job title or person name with a space. HTML-encode the company name, subject, job title, person name, link `href`, and link text. Plain text keeps `<`.
- Break lines are `Lunch break open for {n} minutes`, `Rest break open for {n} minutes`, and `Other break open for {n} minutes`.
- Claim matches payroll: stop when `sent_at` is set or `claimed_at` is less than 15 minutes old; insert otherwise; a 15-minute-old claim updates only when `claimed_at` still equals the value read.
- Insert `clock_alert_send` only after at least one recipient for that alert succeeds. `first_sent_at` is not null. Set `follow_up_sent_at` when that follow-up email succeeds or when the pass marks it done without email.
- Zero successes, or every due alert has no recipient, deletes the slot row. One success keeps the slot sent, marks alerts that reached someone, and leaves a fully failed alert unmarked for a later slot. A pass with nothing to send still sets `sent_at`.
- Scheduler is registered only in Production, checks once at startup, then every five minutes. No HTTP endpoint. No app UI. Do not change payroll mail, password-reset mail, or job-completion mail.

---

## File map

| File | Responsibility |
|------|----------------|
| `backend/Services/ClockAlertSchedule.cs` | Open slot, follow-up instant, thresholds, break labels, whole minutes |
| `backend/Services/ClockAlertMail.cs` | Subject, HTML, and plain text |
| `backend/Services/IClockAlertService.cs` | `SendDueAlertsAsync` |
| `backend/Services/ClockAlertService.cs` | Claim, load, recipients, send, mark |
| `backend/Services/ClockAlertHost.cs` | Production gate |
| `backend/Services/ClockAlertScheduler.cs` | Five-minute hosted service |
| `backend/Models/ClockAlertSlot.cs` | Slot claim row |
| `backend/Models/ClockAlertSend.cs` | Per-break and per-shift memory |
| `backend/Data/AppDbContext.cs` | Map both tables |
| `backend/Program.cs` | Register the service, and the scheduler in Production |
| `database/migrations/20261031_170000_clock_alert.sql` | Create both tables |
| `database/schema.sql` | Same tables in the schema draft |
| `backend.Tests/Infrastructure/TestDatabaseInitializer.cs` | Truncate both tables |
| `backend.Tests/ClockAlertScheduleTests.cs` | Slot and follow-up math |
| `backend.Tests/ClockAlertMailTests.cs` | Subject and body |
| `backend.Tests/IntegrationTests/ClockAlertIntegrationTests.cs` | Spec scenarios |
| `backend.Tests/ClockAlertSchedulerTests.cs` | Host gate and one wake |
| `docs/superpowers/specs/2026-10-31-manager-clock-alerts-design.md` | Mark implemented when the tasks pass |
| `docs/backlog/reporting-and-notifications.md` | Mark item 3 implemented when the tasks pass |

---

### Task 1: Slot math and email text

**Files:**
- Create: `backend/Services/ClockAlertSchedule.cs`
- Create: `backend/Services/ClockAlertMail.cs`
- Test: `backend.Tests/ClockAlertScheduleTests.cs`
- Test: `backend.Tests/ClockAlertMailTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `ClockAlertSchedule.Morning` = `"morning"`, `ClockAlertSchedule.Evening` = `"evening"`
  - `readonly record struct ClockAlertSchedule.Slot(DateOnly SlotOn, string Kind)`
  - `ClockAlertSchedule.OpenSlot(DateTimeOffset now, TimeZoneInfo zone)` returns `Slot`
  - `ClockAlertSchedule.FollowUpInstant(DateTimeOffset firstSentAt, TimeZoneInfo zone)` returns `DateTimeOffset`
  - `ClockAlertSchedule.AtLocal(DateOnly date, TimeOnly time, TimeZoneInfo zone)` returns `DateTimeOffset`
  - `ClockAlertSchedule.IsWeekend(DateOnly date)` returns `bool`
  - `ClockAlertSchedule.ThresholdMinutes(string breakType)` returns `int`
  - `ClockAlertSchedule.BreakLabel(string breakType)` returns `string`
  - `ClockAlertSchedule.WholeMinutes(DateTimeOffset start, DateTimeOffset now)` returns `int`
  - `ClockAlertMail.SubjectFor(DateOnly slotDate, bool morning)` returns `string`
  - `ClockAlertMail.BreakLine(string breakType, int minutes)` returns `string`
  - `ClockAlertMail.ClockedInSince(DateTimeOffset clockInAt, TimeZoneInfo zone)` returns `string`
  - `ClockAlertMail.Flatten(string value)` returns `string`
  - `ClockAlertMail.Render(ClockAlertMailModel model)` returns `(string Subject, string Html, string Text)`
  - `ClockAlertMailModel(string CompanyName, DateOnly SlotDate, bool Morning, IReadOnlyList<ClockAlertJobModel> Jobs)`
  - `ClockAlertJobModel(string Title, string Link, IReadOnlyList<ClockAlertPersonModel> People, int JobId)`
  - `ClockAlertPersonModel(string Name, IReadOnlyList<string> Lines, int PersonId)`

- [ ] **Step 1: Write the failing unit tests**

Create `backend.Tests/ClockAlertScheduleTests.cs`:

```csharp
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class ClockAlertScheduleTests
{
    private static readonly TimeZoneInfo Denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

    [Theory]
    [InlineData(2026, 10, 26, 6, 59, 2026, 10, 25, "evening")]
    [InlineData(2026, 10, 26, 7, 0, 2026, 10, 26, "morning")]
    [InlineData(2026, 10, 26, 18, 59, 2026, 10, 26, "morning")]
    [InlineData(2026, 10, 26, 19, 0, 2026, 10, 26, "evening")]
    public void OpenSlot_uses_the_7am_and_7pm_boundaries(
        int year, int month, int day, int hour, int minute,
        int slotYear, int slotMonth, int slotDay, string kind)
    {
        var slot = ClockAlertSchedule.OpenSlot(AtDenver(year, month, day, hour, minute), Denver);

        Assert.Equal(new DateOnly(slotYear, slotMonth, slotDay), slot.SlotOn);
        Assert.Equal(kind, slot.Kind);
    }

    [Theory]
    [InlineData(2026, 10, 27, 10, 0, 2026, 10, 28, 7, 0)]
    [InlineData(2026, 10, 27, 19, 5, 2026, 10, 28, 7, 0)]
    [InlineData(2026, 10, 30, 21, 0, 2026, 11, 2, 7, 0)]
    [InlineData(2026, 10, 31, 8, 0, 2026, 11, 2, 7, 0)]
    [InlineData(2026, 10, 31, 6, 0, 2026, 11, 2, 7, 0)]
    [InlineData(2026, 11, 2, 6, 0, 2026, 11, 2, 7, 0)]
    public void FollowUpInstant_is_the_next_weekday_at_7am(
        int year, int month, int day, int hour, int minute,
        int followYear, int followMonth, int followDay, int followHour, int followMinute)
    {
        var followUp = ClockAlertSchedule.FollowUpInstant(AtDenver(year, month, day, hour, minute), Denver);

        Assert.Equal(AtDenver(followYear, followMonth, followDay, followHour, followMinute), followUp);
    }

    [Fact]
    public void Threshold_and_whole_minutes()
    {
        Assert.Equal(45, ClockAlertSchedule.ThresholdMinutes("lunch"));
        Assert.Equal(45, ClockAlertSchedule.ThresholdMinutes("LUNCH"));
        Assert.Equal(20, ClockAlertSchedule.ThresholdMinutes("rest"));
        Assert.Equal(20, ClockAlertSchedule.ThresholdMinutes("other"));
        Assert.Equal("Lunch", ClockAlertSchedule.BreakLabel("lunch"));
        Assert.Equal("Rest", ClockAlertSchedule.BreakLabel("rest"));
        Assert.Equal("Other", ClockAlertSchedule.BreakLabel("other"));

        var start = AtDenver(2026, 10, 27, 18, 0);
        Assert.Equal(19, ClockAlertSchedule.WholeMinutes(start, start.AddMinutes(19.9)));
        Assert.Equal(20, ClockAlertSchedule.WholeMinutes(start, start.AddMinutes(20)));
    }

    private static DateTimeOffset AtDenver(int year, int month, int day, int hour, int minute) =>
        new(TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified),
            Denver));
}
```

Create `backend.Tests/ClockAlertMailTests.cs`:

```csharp
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class ClockAlertMailTests
{
    private static readonly TimeZoneInfo Denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

    [Theory]
    [InlineData(true, "Clock alerts for Oct 27, 7:00 AM")]
    [InlineData(false, "Clock alerts for Oct 27, 7:00 PM")]
    public void Subject_names_the_slot_clock(bool morning, string expected)
    {
        Assert.Equal(expected, ClockAlertMail.SubjectFor(new DateOnly(2026, 10, 27), morning));
    }

    [Fact]
    public void Render_encodes_html_and_keeps_plain_text()
    {
        var model = new ClockAlertMailModel(
            "VisionPaint",
            new DateOnly(2026, 10, 27),
            morning: false,
            new[]
            {
                new ClockAlertJobModel(
                    "North <house\nwest",
                    "http://localhost:5173/jobs/4",
                    new[]
                    {
                        new ClockAlertPersonModel(
                            "A<lex",
                            new[]
                            {
                                "Lunch break open for 45 minutes",
                                "Clocked in since 8:00 AM"
                            },
                            3)
                    },
                    4)
            });

        var (subject, html, text) = ClockAlertMail.Render(model);

        Assert.Equal("Clock alerts for Oct 27, 7:00 PM", subject);
        Assert.Contains("VisionPaint", text);
        Assert.Contains("North <house west", text);
        Assert.Contains("A<lex", text);
        Assert.Contains("http://localhost:5173/jobs/4", text);
        Assert.Contains("Lunch break open for 45 minutes", text);
        Assert.Contains("North &lt;house west", html);
        Assert.Contains("A&lt;lex", html);
        Assert.Contains("<h2>North &lt;house west</h2>", html);
        Assert.Contains("<strong>A&lt;lex</strong>", html);
        Assert.Contains("href=\"http://localhost:5173/jobs/4\"", html);
        Assert.DoesNotContain("North <house", html);
    }

    [Fact]
    public void Break_and_clock_lines()
    {
        Assert.Equal("Lunch break open for 45 minutes", ClockAlertMail.BreakLine("lunch", 45));
        Assert.Equal("Rest break open for 20 minutes", ClockAlertMail.BreakLine("rest", 20));
        Assert.Equal("Other break open for 30 minutes", ClockAlertMail.BreakLine("other", 30));
        var clockIn = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 10, 27, 16, 10, 0, DateTimeKind.Unspecified),
            Denver));
        Assert.Equal("Clocked in since 4:10 PM", ClockAlertMail.ClockedInSince(clockIn, Denver));
    }
}
```

- [ ] **Step 2: Run the unit tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~ClockAlertScheduleTests|FullyQualifiedName~ClockAlertMailTests"`

Expected: build fails because `ClockAlertSchedule` and `ClockAlertMail` do not exist.

- [ ] **Step 3: Implement the schedule and the mail builder**

Create `backend/Services/ClockAlertSchedule.cs`:

```csharp
namespace VisionPaint.Services;

public static class ClockAlertSchedule
{
    public const string Morning = "morning";
    public const string Evening = "evening";

    public readonly record struct Slot(DateOnly SlotOn, string Kind);

    public static Slot OpenSlot(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (time < new TimeOnly(7, 0))
        {
            return new Slot(date.AddDays(-1), Evening);
        }

        if (time < new TimeOnly(19, 0))
        {
            return new Slot(date, Morning);
        }

        return new Slot(date, Evening);
    }

    public static DateTimeOffset FollowUpInstant(DateTimeOffset firstSentAt, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(firstSentAt, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (time >= new TimeOnly(7, 0))
        {
            date = date.AddDays(1);
        }

        while (IsWeekend(date))
        {
            date = date.AddDays(1);
        }

        return AtLocal(date, new TimeOnly(7, 0), zone);
    }

    public static DateTimeOffset AtLocal(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static int ThresholdMinutes(string breakType) =>
        breakType.Equals("lunch", StringComparison.OrdinalIgnoreCase) ? 45 : 20;

    public static string BreakLabel(string breakType)
    {
        if (breakType.Equals("lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "Lunch";
        }

        if (breakType.Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            return "Other";
        }

        return "Rest";
    }

    public static int WholeMinutes(DateTimeOffset start, DateTimeOffset now) =>
        (int)Math.Max(0, (now - start).TotalMinutes);
}
```

Create `backend/Services/ClockAlertMail.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Text;

namespace VisionPaint.Services;

public sealed record ClockAlertPersonModel(string Name, IReadOnlyList<string> Lines, int PersonId);

public sealed record ClockAlertJobModel(
    string Title,
    string Link,
    IReadOnlyList<ClockAlertPersonModel> People,
    int JobId);

public sealed record ClockAlertMailModel(
    string CompanyName,
    DateOnly SlotDate,
    bool Morning,
    IReadOnlyList<ClockAlertJobModel> Jobs);

public static class ClockAlertMail
{
    public static string SubjectFor(DateOnly slotDate, bool morning)
    {
        var culture = CultureInfo.InvariantCulture;
        var clock = morning ? "7:00 AM" : "7:00 PM";
        return $"Clock alerts for {slotDate.ToString("MMM d", culture)}, {clock}";
    }

    public static string BreakLine(string breakType, int minutes) =>
        $"{ClockAlertSchedule.BreakLabel(breakType)} break open for {minutes} minutes";

    public static string ClockedInSince(DateTimeOffset clockInAt, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(clockInAt, zone);
        return "Clocked in since " + local.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    public static string Flatten(string value) =>
        value.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");

    public static (string Subject, string Html, string Text) Render(ClockAlertMailModel model)
    {
        var subject = SubjectFor(model.SlotDate, model.Morning);
        var text = new StringBuilder();
        text.AppendLine(Flatten(model.CompanyName));
        text.AppendLine(subject);
        text.AppendLine();

        var html = new StringBuilder();
        html.Append("<p>").Append(Encode(model.CompanyName)).Append("</p>");
        html.Append("<p>").Append(Encode(subject)).Append("</p>");
        foreach (var job in model.Jobs)
        {
            text.AppendLine(Flatten(job.Title));
            text.AppendLine(job.Link);
            var encodedLink = WebUtility.HtmlEncode(job.Link);
            html.Append("<h2>").Append(Encode(job.Title)).Append("</h2>");
            html.Append("<p><a href=\"").Append(encodedLink).Append("\">").Append(encodedLink).Append("</a></p>");
            foreach (var person in job.People)
            {
                text.AppendLine(Flatten(person.Name));
                html.Append("<p><strong>").Append(Encode(person.Name)).Append("</strong></p>");
                foreach (var line in person.Lines)
                {
                    text.AppendLine(line);
                    html.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
                }

                text.AppendLine();
            }
        }

        return (subject, html.ToString(), text.ToString());
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(Flatten(value));
}
```

- [ ] **Step 4: Run the unit tests to verify they pass**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~ClockAlertScheduleTests|FullyQualifiedName~ClockAlertMailTests"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/Services/ClockAlertSchedule.cs backend/Services/ClockAlertMail.cs backend.Tests/ClockAlertScheduleTests.cs backend.Tests/ClockAlertMailTests.cs
git commit -m "Add clock alert schedule and email text"
```

---

### Task 2: Claim tables

**Files:**
- Create: `database/migrations/20261031_170000_clock_alert.sql`
- Modify: `database/schema.sql` (append after `payroll_summary_send`)
- Create: `backend/Models/ClockAlertSlot.cs`
- Create: `backend/Models/ClockAlertSend.cs`
- Modify: `backend/Data/AppDbContext.cs`
- Modify: `backend.Tests/Infrastructure/TestDatabaseInitializer.cs`

**Interfaces:**
- Consumes: nothing from Task 1
- Produces: `AppDbContext.ClockAlertSlots`, `AppDbContext.ClockAlertSends`, `ClockAlertSlot` (`CompanyId`, `SlotOn`, `SlotKind`, `ClaimedAt`, `SentAt`), `ClockAlertSend` (`Id`, `CompanyId`, `TimeBreakId`, `TimeEntryId`, `FirstSentAt`, `FollowUpSentAt`)

- [ ] **Step 1: Add the migration and the schema draft**

`database/migrations/20261031_170000_clock_alert.sql`:

```sql
create table if not exists public.clock_alert_slot (
  company_id integer not null references public.company (id) on delete cascade,
  slot_on date not null,
  slot_kind text not null check (slot_kind in ('morning', 'evening')),
  claimed_at timestamptz not null,
  sent_at timestamptz,
  primary key (company_id, slot_on, slot_kind)
);

create table if not exists public.clock_alert_send (
  id integer generated by default as identity primary key,
  company_id integer not null references public.company (id) on delete cascade,
  time_break_id integer references public.time_break (id) on delete cascade,
  time_entry_id integer references public.time_entry (id) on delete cascade,
  first_sent_at timestamptz not null,
  follow_up_sent_at timestamptz,
  constraint clock_alert_send_one_source check (
    (time_break_id is not null and time_entry_id is null)
    or (time_break_id is null and time_entry_id is not null)
  )
);

create unique index if not exists clock_alert_send_time_break_id_uidx
  on public.clock_alert_send (time_break_id)
  where time_break_id is not null;

create unique index if not exists clock_alert_send_time_entry_id_uidx
  on public.clock_alert_send (time_entry_id)
  where time_entry_id is not null;
```

Append the same statements to `database/schema.sql`, using unqualified table names to match that file (`references company (id)`, `references time_break (id)`, `references time_entry (id)`).

- [ ] **Step 2: Map the entities**

Create `backend/Models/ClockAlertSlot.cs`:

```csharp
namespace VisionPaint.Models;

public sealed class ClockAlertSlot
{
    public int CompanyId { get; set; }

    public DateOnly SlotOn { get; set; }

    public string SlotKind { get; set; } = "morning";

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
```

Create `backend/Models/ClockAlertSend.cs`:

```csharp
namespace VisionPaint.Models;

public sealed class ClockAlertSend
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int? TimeBreakId { get; set; }

    public int? TimeEntryId { get; set; }

    public DateTimeOffset FirstSentAt { get; set; }

    public DateTimeOffset? FollowUpSentAt { get; set; }
}
```

In `AppDbContext`, add:

```csharp
public DbSet<ClockAlertSlot> ClockAlertSlots => Set<ClockAlertSlot>();

public DbSet<ClockAlertSend> ClockAlertSends => Set<ClockAlertSend>();
```

Inside `OnModelCreating`:

```csharp
modelBuilder.Entity<ClockAlertSlot>(entity =>
{
    entity.ToTable("clock_alert_slot");
    entity.HasKey(row => new { row.CompanyId, row.SlotOn, row.SlotKind });
    entity.Property(row => row.CompanyId).HasColumnName("company_id");
    entity.Property(row => row.SlotOn).HasColumnName("slot_on");
    entity.Property(row => row.SlotKind).HasColumnName("slot_kind");
    entity.Property(row => row.ClaimedAt).HasColumnName("claimed_at");
    entity.Property(row => row.SentAt).HasColumnName("sent_at");
});

modelBuilder.Entity<ClockAlertSend>(entity =>
{
    entity.ToTable("clock_alert_send", table => table.HasCheckConstraint(
        "clock_alert_send_one_source",
        "(time_break_id is not null and time_entry_id is null) or (time_break_id is null and time_entry_id is not null)"));
    entity.HasKey(row => row.Id);
    entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
    entity.Property(row => row.CompanyId).HasColumnName("company_id");
    entity.Property(row => row.TimeBreakId).HasColumnName("time_break_id");
    entity.Property(row => row.TimeEntryId).HasColumnName("time_entry_id");
    entity.Property(row => row.FirstSentAt).HasColumnName("first_sent_at");
    entity.Property(row => row.FollowUpSentAt).HasColumnName("follow_up_sent_at");
    entity.HasIndex(row => row.TimeBreakId).IsUnique().HasFilter("time_break_id is not null");
    entity.HasIndex(row => row.TimeEntryId).IsUnique().HasFilter("time_entry_id is not null");
});
```

- [ ] **Step 3: Truncate the tables in test reset**

In `TestDatabaseInitializer.ResetAsync`, add `public.clock_alert_send` and `public.clock_alert_slot` as the first two tables in the `truncate table` list, before `public.payroll_summary_send`.

- [ ] **Step 4: Build**

Run: `dotnet build backend/VisionPaint.csproj --nologo`

Expected: build succeeds. If `VisionPaint.exe` is locked by a running API, stop that process and build again. The compile must not report a C# error.

- [ ] **Step 5: Commit**

```bash
git add database/migrations/20261031_170000_clock_alert.sql database/schema.sql backend/Models/ClockAlertSlot.cs backend/Models/ClockAlertSend.cs backend/Data/AppDbContext.cs backend.Tests/Infrastructure/TestDatabaseInitializer.cs
git commit -m "Add clock alert claim tables"
```

---

### Task 3: Send the alerts

**Files:**
- Create: `backend/Services/IClockAlertService.cs`
- Create: `backend/Services/ClockAlertService.cs`
- Create: `backend.Tests/IntegrationTests/ClockAlertIntegrationTests.cs`
- Modify: `backend/Program.cs` (register `ClockAlertService` and `IClockAlertService` only; the scheduler is Task 4)

**Interfaces:**
- Consumes: `ClockAlertSchedule`, `ClockAlertMail`, `ClockAlertSlot`, `ClockAlertSend`, `IEmailSender.SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default)`, `AppOptions.FrontendBaseUrl`, `TimeProvider.GetUtcNow()`, `FixedTimeProvider`
- Produces: `IClockAlertService.SendDueAlertsAsync(CancellationToken cancellationToken)` implemented by `ClockAlertService`

`ClockAlertService` must call `TimeProvider.GetUtcNow()` inside `SendDueAlertsAsync`, not in the constructor. The person the alert is about is removed from job recipients and from the owner/admin fallback.

- [ ] **Step 1: Write the failing integration tests**

Create `backend.Tests/IntegrationTests/ClockAlertIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class ClockAlertIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private const string OwnerEmail = "owner-one@example.com";
    private static readonly TimeZoneInfo Denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
    private readonly BackendIntegrationFixture _fixture;

    public ClockAlertIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() =>
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Spec_01_empty_morning_closes_the_slot_and_a_later_break_waits()
    {
        var email = new ScriptedEmailSender();
        await using var db = CreateDb();
        await SendAt(db, email, AtDenver(2026, 10, 27, 7, 0));

        Assert.Empty(email.Inner.Sent);
        var slot = await db.ClockAlertSlots.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 27), slot.SlotOn);
        Assert.Equal("morning", slot.SlotKind);
        Assert.NotNull(slot.SentAt);

        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 9, 0));
        await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 27, 9, 30));
        await SendAt(db, email, AtDenver(2026, 10, 27, 10, 0));

        Assert.Empty(email.Inner.Sent);
        Assert.Empty(await db.ClockAlertSends.ToListAsync());
    }

    [Fact]
    public async Task Spec_02_unsent_morning_emails_the_other_lead_and_supervisor()
    {
        await BootstrapOwnerAsync();
        await using var db = CreateDb();
        var riley = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var pat = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var sam = await AddMemberAsync(db, "Sam", "sam@example.com", "crew");
        await AddMemberAsync(db, "Morgan", "morgan@example.com", "manager");
        var jobId = await AddJobAsync(db, "North", (riley, "lead"), (pat, "lead"), (sam, "supervisor"));
        var entryId = await AddOpenShiftAsync(db, jobId, riley, AtDenver(2026, 10, 27, 9, 0));
        await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 27, 9, 30));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 27, 10, 0));

        var messages = email.Inner.Sent.OrderBy(message => message.To).ToArray();
        Assert.Equal(new[] { "pat@example.com", "sam@example.com" }, messages.Select(message => message.To).ToArray());
        Assert.All(messages, message => Assert.Equal("Clock alerts for Oct 27, 7:00 AM", message.Subject));
        Assert.All(messages, message => Assert.Contains("Rest break open for 30 minutes", message.Text));
        Assert.All(messages, message => Assert.DoesNotContain("Clocked in since", message.Text));
        Assert.DoesNotContain(email.Inner.Sent, message => message.To == OwnerEmail);
        Assert.DoesNotContain(email.Inner.Sent, message => message.To == "riley@example.com");
        Assert.DoesNotContain(email.Inner.Sent, message => message.To == "morgan@example.com");
        Assert.NotNull((await db.ClockAlertSends.SingleAsync()).FirstSentAt);
        Assert.NotNull((await db.ClockAlertSlots.SingleAsync()).SentAt);
    }

    [Fact]
    public async Task Spec_03_evening_lists_due_breaks_open_shifts_and_fallback_jobs()
    {
        await BootstrapOwnerAsync();
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var supervisor = await AddMemberAsync(db, "Sam", "sam@example.com", "crew");
        var alex = await AddMemberAsync(db, "A<lex", "alex@example.com", "crew");
        var blair = await AddMemberAsync(db, "Blair", "blair@example.com", "crew");
        var casey = await AddMemberAsync(db, "Casey", "casey@example.com", "crew");
        var erin = await AddMemberAsync(db, "Erin", "erin@example.com", "crew");
        var drew = await AddMemberAsync(db, "Drew", "drew@example.com", "crew");
        await AddMemberAsync(db, "Ada", "ada@example.com", "admin");
        await AddMemberAsync(db, "Morgan", "morgan@example.com", "manager");

        var north = await AddJobAsync(
            db,
            "North <house\nwest",
            (lead, "lead"),
            (supervisor, "supervisor"),
            (alex, "crew"),
            (blair, "crew"),
            (casey, "crew"),
            (erin, "crew"));
        await AddOpenShiftAsync(db, north, alex, AtDenver(2026, 10, 27, 8, 0));
        await AddBreakAsync(db, (await db.TimeEntries.SingleAsync(entry => entry.PersonId == alex)).Id, "lunch", AtDenver(2026, 10, 27, 18, 15));
        var blairEntry = await AddOpenShiftAsync(db, north, blair, AtDenver(2026, 10, 27, 10, 0));
        await AddBreakAsync(db, blairEntry, "rest", AtDenver(2026, 10, 27, 18, 41));
        var caseyEntry = await AddOpenShiftAsync(db, north, casey, AtDenver(2026, 10, 27, 9, 30));
        await AddBreakAsync(db, caseyEntry, "rest", AtDenver(2026, 10, 27, 18, 40));
        var erinEntry = await AddOpenShiftAsync(db, north, erin, AtDenver(2026, 10, 27, 7, 0));
        var erinRow = await db.TimeEntries.SingleAsync(entry => entry.Id == erinEntry);
        erinRow.ClockOutAt = AtDenver(2026, 10, 27, 15, 0);
        await db.SaveChangesAsync();

        var south = await AddJobAsync(db, "South", (drew, "lead"));
        await AddOpenShiftAsync(db, south, drew, AtDenver(2026, 10, 27, 16, 10));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 27, 19, 0));

        var northMessages = email.Inner.Sent.Where(message => message.To is "pat@example.com" or "sam@example.com").ToArray();
        Assert.Equal(2, northMessages.Length);
        Assert.All(northMessages, message =>
        {
            Assert.Equal("Clock alerts for Oct 27, 7:00 PM", message.Subject);
            Assert.Contains("North <house west", message.Text);
            Assert.Contains("A<lex", message.Text);
            Assert.Contains("Lunch break open for 45 minutes", message.Text);
            Assert.Contains("Clocked in since 8:00 AM", message.Text);
            Assert.Contains("Rest break open for 20 minutes", message.Text);
            Assert.Contains("Clocked in since 9:30 AM", message.Text);
            Assert.Contains("Clocked in since 10:00 AM", message.Text);
            Assert.DoesNotContain("Rest break open for 19", message.Text);
            Assert.DoesNotContain("Erin", message.Text);
            Assert.DoesNotContain("South", message.Text);
            Assert.Contains("North &lt;house west", message.Html);
            Assert.Contains("A&lt;lex", message.Html);
            Assert.Contains($"http://localhost:5173/jobs/{north}", message.Text);
            Assert.DoesNotContain("North <house\n", message.Html);
        });

        var fallback = email.Inner.Sent.Where(message => message.To is OwnerEmail or "ada@example.com").ToArray();
        Assert.Equal(2, fallback.Length);
        Assert.All(fallback, message =>
        {
            Assert.Contains("South", message.Text);
            Assert.Contains("Clocked in since 4:10 PM", message.Text);
            Assert.DoesNotContain("North", message.Text);
        });
        Assert.DoesNotContain(email.Inner.Sent, message => message.To == "drew@example.com");
        Assert.DoesNotContain(email.Inner.Sent, message => message.To == "morgan@example.com");
    }

    [Fact]
    public async Task Spec_04_and_05_follow_up_once_then_the_evening_is_quiet()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));
        var breakId = await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 27, 18, 0));
        await AddSendAsync(db, breakId, null, AtDenver(2026, 10, 27, 19, 0));
        await AddSendAsync(db, null, entryId, AtDenver(2026, 10, 27, 19, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 28, 7, 0));

        var message = Assert.Single(email.Inner.Sent);
        Assert.Equal("pat@example.com", message.To);
        Assert.Equal("Clock alerts for Oct 28, 7:00 AM", message.Subject);
        Assert.Contains("Rest break open for", message.Text);
        Assert.Contains("Clocked in since 8:00 AM", message.Text);
        Assert.Equal(2, await db.ClockAlertSends.CountAsync(row => row.FollowUpSentAt != null));

        await SendAt(db, email, AtDenver(2026, 10, 28, 19, 0));
        Assert.Single(email.Inner.Sent);
    }

    [Fact]
    public async Task Spec_06_cleared_follow_up_is_marked_without_email()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));
        var breakId = await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 27, 18, 0));
        await AddSendAsync(db, breakId, null, AtDenver(2026, 10, 27, 19, 0));
        await AddSendAsync(db, null, entryId, AtDenver(2026, 10, 27, 19, 0));
        await ClockOutAsync(db, entryId, AtDenver(2026, 10, 27, 21, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 28, 7, 0));

        Assert.Empty(email.Inner.Sent);
        Assert.Equal(2, await db.ClockAlertSends.CountAsync(row => row.FollowUpSentAt != null));
    }

    [Fact]
    public async Task Spec_07_saturday_evening_sends_the_break_only()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var onBreak = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var stillIn = await AddMemberAsync(db, "Casey", "casey@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (onBreak, "crew"), (stillIn, "crew"));
        var breakEntry = await AddOpenShiftAsync(db, jobId, onBreak, AtDenver(2026, 10, 31, 16, 0));
        await AddBreakAsync(db, breakEntry, "rest", AtDenver(2026, 10, 31, 18, 30));
        await AddOpenShiftAsync(db, jobId, stillIn, AtDenver(2026, 10, 31, 16, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 31, 19, 0));

        var message = Assert.Single(email.Inner.Sent);
        Assert.Contains("Rest break open for 30 minutes", message.Text);
        Assert.DoesNotContain("Casey", message.Text);
        Assert.DoesNotContain("Clocked in since", message.Text);
        Assert.Empty(await db.ClockAlertSends.Where(row => row.TimeEntryId != null).ToListAsync());
    }

    [Fact]
    public async Task Spec_08_weekend_follow_up_waits_for_monday()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 31, 16, 0));
        await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 31, 18, 30));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 31, 19, 0));
        await SendAt(db, email, AtDenver(2026, 11, 1, 7, 0));
        Assert.Single(email.Inner.Sent);

        await SendAt(db, email, AtDenver(2026, 11, 2, 7, 0));
        Assert.Equal(2, email.Inner.Sent.Count);
        var followUp = email.Inner.Sent.Single(message => message.Subject == "Clock alerts for Nov 2, 7:00 AM");
        Assert.Contains("Rest break open for", followUp.Text);
        Assert.DoesNotContain("Clocked in since", followUp.Text);
    }

    [Fact]
    public async Task Spec_09_friday_after_hours_follows_up_monday_morning_only()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 30, 16, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 30, 19, 0));
        await SendAt(db, email, AtDenver(2026, 11, 2, 7, 0));
        await SendAt(db, email, AtDenver(2026, 11, 2, 19, 0));

        Assert.Equal(2, email.Inner.Sent.Count);
        Assert.Contains(email.Inner.Sent, message => message.Subject == "Clock alerts for Oct 30, 7:00 PM");
        Assert.Contains(email.Inner.Sent, message => message.Subject == "Clock alerts for Nov 2, 7:00 AM");
    }

    [Fact]
    public async Task Spec_10_late_evening_wake_uses_the_7pm_subject_and_current_minutes()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 30, 16, 0));
        await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 30, 20, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 30, 21, 0));

        var message = Assert.Single(email.Inner.Sent);
        Assert.Equal("Clock alerts for Oct 30, 7:00 PM", message.Subject);
        Assert.Contains("Rest break open for 60 minutes", message.Text);
        Assert.Contains("Clocked in since 4:00 PM", message.Text);
    }

    [Fact]
    public async Task Spec_11_missed_friday_evening_is_left_behind()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 30, 16, 0));
        await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 31, 7, 30));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 31, 8, 0));

        var message = Assert.Single(email.Inner.Sent);
        Assert.Equal("Clock alerts for Oct 31, 7:00 AM", message.Subject);
        Assert.Contains("Rest break open for 30 minutes", message.Text);
        Assert.DoesNotContain("Clocked in since", message.Text);
        var slot = await db.ClockAlertSlots.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 31), slot.SlotOn);
        Assert.Equal("morning", slot.SlotKind);
    }

    [Fact]
    public async Task Spec_12_all_sends_failing_releases_the_claim()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));

        var email = new ScriptedEmailSender { FailAll = true };
        await SendAt(db, email, AtDenver(2026, 10, 27, 19, 0));
        Assert.Empty(await db.ClockAlertSlots.ToListAsync());
        Assert.Empty(await db.ClockAlertSends.ToListAsync());

        email.FailAll = false;
        await SendAt(db, email, AtDenver(2026, 10, 27, 20, 0));
        var message = Assert.Single(email.Inner.Sent);
        Assert.Equal("Clock alerts for Oct 27, 7:00 PM", message.Subject);
        Assert.NotNull((await db.ClockAlertSlots.SingleAsync()).SentAt);
    }

    [Fact]
    public async Task Spec_13_partial_failure_keeps_the_slot_sent()
    {
        await using var db = CreateDb();
        var pat = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var sam = await AddMemberAsync(db, "Sam", "sam@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (pat, "lead"), (sam, "supervisor"), (worker, "crew"));
        await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));

        var email = new ScriptedEmailSender();
        email.FailFor.Add("sam@example.com");
        await SendAt(db, email, AtDenver(2026, 10, 27, 19, 0));
        Assert.Equal(new[] { "pat@example.com" }, email.Inner.Sent.Select(message => message.To).ToArray());
        Assert.NotNull((await db.ClockAlertSlots.SingleAsync()).SentAt);

        email.FailFor.Clear();
        await SendAt(db, email, AtDenver(2026, 10, 27, 20, 0));
        Assert.Single(email.Inner.Sent);
    }

    [Fact]
    public async Task Spec_14_no_recipient_address_releases_the_claim()
    {
        var owner = await BootstrapOwnerAsync();
        await using var db = CreateDb();
        var auth = await db.AuthUsers.SingleAsync(user => user.Id == owner.AuthUserId);
        auth.Email = " ";
        await db.SaveChangesAsync();
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (worker, "lead"));
        await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 27, 19, 0));

        Assert.Empty(email.Inner.Sent);
        Assert.Empty(await db.ClockAlertSlots.ToListAsync());
    }

    [Fact]
    public async Task Spec_15_sent_slot_does_not_send_again()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 9, 0));
        await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 27, 9, 30));
        db.ClockAlertSlots.Add(new ClockAlertSlot
        {
            CompanyId = 1,
            SlotOn = new DateOnly(2026, 10, 27),
            SlotKind = "morning",
            ClaimedAt = AtDenver(2026, 10, 27, 7, 0),
            SentAt = AtDenver(2026, 10, 27, 7, 0)
        });
        await db.SaveChangesAsync();

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 27, 10, 0));
        Assert.Empty(email.Inner.Sent);
    }

    [Fact]
    public async Task Spec_16_a_new_break_after_follow_up_sends_again()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));
        await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 27, 18, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 27, 19, 0));
        await SendAt(db, email, AtDenver(2026, 10, 28, 7, 0));
        await ClockOutAsync(db, entryId, AtDenver(2026, 10, 28, 8, 0));
        var nextEntry = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 28, 18, 0));
        await AddBreakAsync(db, nextEntry, "rest", AtDenver(2026, 10, 28, 18, 40));
        email.Inner.Clear();

        await SendAt(db, email, AtDenver(2026, 10, 28, 19, 0));

        var message = Assert.Single(email.Inner.Sent);
        Assert.Contains("Rest break open for 20 minutes", message.Text);
        Assert.Contains("Clocked in since 6:00 PM", message.Text);
    }

    [Fact]
    public async Task Failed_job_is_first_sent_on_the_next_morning()
    {
        await using var db = CreateDb();
        var pat = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var sam = await AddMemberAsync(db, "Sam", "sam@example.com", "crew");
        var riley = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var casey = await AddMemberAsync(db, "Casey", "casey@example.com", "crew");
        var north = await AddJobAsync(db, "North", (pat, "lead"), (riley, "crew"));
        var south = await AddJobAsync(db, "South", (sam, "lead"), (casey, "crew"));
        var northEntry = await AddOpenShiftAsync(db, north, riley, AtDenver(2026, 10, 27, 8, 0));
        var southEntry = await AddOpenShiftAsync(db, south, casey, AtDenver(2026, 10, 27, 8, 0));
        await AddBreakAsync(db, northEntry, "rest", AtDenver(2026, 10, 27, 18, 0));
        await AddBreakAsync(db, southEntry, "rest", AtDenver(2026, 10, 27, 18, 0));

        var email = new ScriptedEmailSender();
        email.FailFor.Add("sam@example.com");
        await SendAt(db, email, AtDenver(2026, 10, 27, 19, 0));
        Assert.Contains(email.Inner.Sent, message => message.To == "pat@example.com");
        Assert.DoesNotContain(email.Inner.Sent, message => message.To == "sam@example.com");
        Assert.NotNull((await db.ClockAlertSlots.SingleAsync()).SentAt);

        email.FailFor.Clear();
        email.Inner.Clear();
        await SendAt(db, email, AtDenver(2026, 10, 28, 7, 0));
        Assert.Contains(email.Inner.Sent, message => message.To == "sam@example.com" && message.Subject == "Clock alerts for Oct 28, 7:00 AM");
        Assert.Contains(email.Inner.Sent, message => message.To == "pat@example.com");
    }

    [Fact]
    public async Task Owner_who_is_the_only_lead_does_not_receive_their_own_alert()
    {
        var owner = await BootstrapOwnerAsync();
        await using var db = CreateDb();
        await AddMemberAsync(db, "Ada", "ada@example.com", "admin");
        var jobId = await AddJobAsync(db, "North", (owner.PersonId, "lead"));
        await AddOpenShiftAsync(db, jobId, owner.PersonId, AtDenver(2026, 10, 27, 8, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 27, 19, 0));

        var message = Assert.Single(email.Inner.Sent);
        Assert.Equal("ada@example.com", message.To);
    }

    [Fact]
    public async Task Owner_lead_gets_one_email_for_lead_and_fallback_jobs()
    {
        var owner = await BootstrapOwnerAsync();
        await using var db = CreateDb();
        var riley = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var casey = await AddMemberAsync(db, "Casey", "casey@example.com", "crew");
        var north = await AddJobAsync(db, "North", (owner.PersonId, "lead"), (riley, "crew"));
        var south = await AddJobAsync(db, "South", (casey, "crew"));
        var entryId = await AddOpenShiftAsync(db, north, riley, AtDenver(2026, 10, 27, 8, 0));
        await AddBreakAsync(db, entryId, "lunch", AtDenver(2026, 10, 27, 18, 15));
        await AddOpenShiftAsync(db, south, casey, AtDenver(2026, 10, 27, 16, 10));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 27, 19, 0));

        var message = Assert.Single(email.Inner.Sent);
        Assert.Equal(OwnerEmail, message.To);
        Assert.Contains("North", message.Text);
        Assert.Contains("South", message.Text);
        Assert.Contains("Lunch break open for 45 minutes", message.Text);
        Assert.Contains("Clocked in since 4:10 PM", message.Text);
    }

    [Fact]
    public async Task Missed_follow_up_morning_is_marked_done_without_email()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));
        var breakId = await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 27, 18, 0));
        await AddSendAsync(db, breakId, null, AtDenver(2026, 10, 27, 19, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 29, 7, 0));

        Assert.Empty(email.Inner.Sent);
        Assert.NotNull((await db.ClockAlertSends.SingleAsync()).FollowUpSentAt);
    }

    [Fact]
    public async Task Fresh_claim_is_not_taken_over()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));
        var now = AtDenver(2026, 10, 27, 19, 0);
        db.ClockAlertSlots.Add(new ClockAlertSlot
        {
            CompanyId = 1,
            SlotOn = new DateOnly(2026, 10, 27),
            SlotKind = "evening",
            ClaimedAt = now.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var email = new ScriptedEmailSender();
        await SendAt(db, email, now);
        Assert.Empty(email.Inner.Sent);
        Assert.Null((await db.ClockAlertSlots.SingleAsync()).SentAt);
    }

    [Fact]
    public async Task Stale_claim_is_taken_over_and_sent()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));
        var now = AtDenver(2026, 10, 27, 19, 0);
        db.ClockAlertSlots.Add(new ClockAlertSlot
        {
            CompanyId = 1,
            SlotOn = new DateOnly(2026, 10, 27),
            SlotKind = "evening",
            ClaimedAt = now.AddMinutes(-16)
        });
        await db.SaveChangesAsync();

        var email = new ScriptedEmailSender();
        await SendAt(db, email, now);
        Assert.Single(email.Inner.Sent);
        Assert.NotNull((await db.ClockAlertSlots.SingleAsync()).SentAt);
    }

    [Fact]
    public async Task Evening_pass_does_not_mark_a_pending_follow_up()
    {
        await using var db = CreateDb();
        var lead = await AddMemberAsync(db, "Pat", "pat@example.com", "crew");
        var worker = await AddMemberAsync(db, "Riley", "riley@example.com", "crew");
        var jobId = await AddJobAsync(db, "North", (lead, "lead"), (worker, "crew"));
        var entryId = await AddOpenShiftAsync(db, jobId, worker, AtDenver(2026, 10, 27, 8, 0));
        var breakId = await AddBreakAsync(db, entryId, "rest", AtDenver(2026, 10, 27, 18, 0));
        await AddSendAsync(db, breakId, null, AtDenver(2026, 10, 27, 19, 0));
        await AddSendAsync(db, null, entryId, AtDenver(2026, 10, 27, 19, 0));

        var email = new ScriptedEmailSender();
        await SendAt(db, email, AtDenver(2026, 10, 28, 19, 0));

        Assert.Empty(email.Inner.Sent);
        Assert.Equal(2, await db.ClockAlertSends.CountAsync(row => row.FollowUpSentAt == null));
    }

    private async Task<AuthenticatedUserResponse> BootstrapOwnerAsync()
    {
        var tokens = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "Owner One",
            OwnerEmail,
            "Password123!"));
        return tokens.User;
    }

    private async Task SendAt(AppDbContext db, ScriptedEmailSender email, DateTimeOffset utcNow)
    {
        var service = CreateService(db, email, new FixedTimeProvider(utcNow));
        await service.SendDueAlertsAsync(CancellationToken.None);
    }

    private static ClockAlertService CreateService(AppDbContext db, IEmailSender email, FixedTimeProvider clock) =>
        new(db, email, clock, Options.Create(new AppOptions()), NullLogger<ClockAlertService>.Instance);

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.Database.ConnectionString).Options);

    private async Task<int> AddMemberAsync(AppDbContext db, string name, string emailAddress, string role)
    {
        var now = DateTimeOffset.UtcNow;
        var auth = new AuthUser
        {
            Id = Guid.NewGuid(),
            Email = emailAddress,
            PasswordHash = "not-used",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var person = new Person
        {
            AuthUserId = auth.Id,
            Name = name,
            Email = emailAddress,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AuthUsers.Add(auth);
        db.People.Add(person);
        await db.SaveChangesAsync();
        db.CompanyMembers.Add(new CompanyMember
        {
            CompanyId = 1,
            PersonId = person.Id,
            Role = role,
            Status = "active",
            JoinedAt = now
        });
        await db.SaveChangesAsync();
        return person.Id;
    }

    private async Task<int> AddJobAsync(AppDbContext db, string title, params (int PersonId, string Role)[] assignments)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            CompanyId = 1,
            Title = title,
            Status = "in_progress",
            Priority = "normal",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        foreach (var assignment in assignments)
        {
            db.JobAssignments.Add(new JobAssignment
            {
                JobId = job.Id,
                PersonId = assignment.PersonId,
                AssignmentRole = assignment.Role,
                AssignedAt = now
            });
        }

        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<int> AddOpenShiftAsync(AppDbContext db, int jobId, int personId, DateTimeOffset clockIn)
    {
        var entry = new TimeEntry
        {
            JobId = jobId,
            PersonId = personId,
            ClockInAt = clockIn,
            BreakMinutes = 0,
            CreatedAt = clockIn,
            UpdatedAt = clockIn
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<int> AddBreakAsync(AppDbContext db, int entryId, string type, DateTimeOffset start)
    {
        var row = new TimeBreak
        {
            TimeEntryId = entryId,
            BreakStartAt = start,
            BreakType = type
        };
        db.TimeBreaks.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task AddSendAsync(
        AppDbContext db,
        int? breakId,
        int? entryId,
        DateTimeOffset firstSentAt,
        DateTimeOffset? followUpSentAt = null)
    {
        db.ClockAlertSends.Add(new ClockAlertSend
        {
            CompanyId = 1,
            TimeBreakId = breakId,
            TimeEntryId = entryId,
            FirstSentAt = firstSentAt,
            FollowUpSentAt = followUpSentAt
        });
        await db.SaveChangesAsync();
    }

    private async Task ClockOutAsync(AppDbContext db, int entryId, DateTimeOffset at)
    {
        var entry = await db.TimeEntries.SingleAsync(row => row.Id == entryId);
        entry.ClockOutAt = at;
        entry.UpdatedAt = at;
        var breaks = await db.TimeBreaks.Where(row => row.TimeEntryId == entryId && row.BreakEndAt == null).ToListAsync();
        foreach (var row in breaks)
        {
            row.BreakEndAt = at;
        }

        await db.SaveChangesAsync();
    }

    private static DateTimeOffset AtDenver(int year, int month, int day, int hour, int minute) =>
        new(TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified),
            Denver));

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

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~ClockAlertIntegrationTests`

Expected: build fails because `ClockAlertService` does not exist.

- [ ] **Step 3: Implement the service**

Create `backend/Services/IClockAlertService.cs`:

```csharp
namespace VisionPaint.Services;

public interface IClockAlertService
{
    Task SendDueAlertsAsync(CancellationToken cancellationToken);
}
```

Create `backend/Services/ClockAlertService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class ClockAlertService : IClockAlertService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly TimeProvider _time;
    private readonly AppOptions _app;
    private readonly ILogger<ClockAlertService> _logger;

    public ClockAlertService(
        AppDbContext db,
        IEmailSender email,
        TimeProvider time,
        IOptions<AppOptions> app,
        ILogger<ClockAlertService> logger)
    {
        _db = db;
        _email = email;
        _time = time;
        _app = app.Value;
        _logger = logger;
    }

    public async Task SendDueAlertsAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var companies = await _db.Companies.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var company in companies)
        {
            var zone = TimeZoneHelper.Resolve(company.Timezone);
            var slot = ClockAlertSchedule.OpenSlot(now, zone);
            if (!await TryClaimAsync(company.Id, slot, now, cancellationToken))
            {
                continue;
            }

            var work = await LoadWorkAsync(company, zone, slot, now, cancellationToken);
            var sending = work.Where(alert => alert.Send).ToList();
            var consumes = work.Where(alert => !alert.Send).ToList();
            if (sending.Count == 0)
            {
                await MarkSentAsync(company.Id, Array.Empty<AlertWork>(), consumes, now, cancellationToken);
                await FinishSlotAsync(company.Id, slot, now, cancellationToken);
                continue;
            }

            var recipients = await LoadRecipientsAsync(company.Id, sending, cancellationToken);
            var delivered = new HashSet<AlertWork>();
            var succeeded = 0;
            foreach (var group in sending
                .SelectMany(alert => recipients[alert].Select(recipient => (recipient, alert)))
                .GroupBy(pair => pair.recipient.PersonId))
            {
                var recipient = group.First().recipient;
                var alerts = group.Select(pair => pair.alert).Distinct().ToList();
                var (subject, html, text) = ClockAlertMail.Render(BuildModel(company.Name, slot, alerts));
                try
                {
                    await _email.SendAsync(recipient.Email, subject, html, text, cancellationToken);
                    succeeded++;
                    foreach (var alert in alerts)
                    {
                        delivered.Add(alert);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Clock alert email failed for {Recipient} company {CompanyId} slot {SlotOn} {SlotKind}",
                        recipient.Email,
                        company.Id,
                        slot.SlotOn,
                        slot.Kind);
                }
            }

            if (succeeded == 0)
            {
                await ReleaseAsync(company.Id, slot, cancellationToken);
                continue;
            }

            await MarkSentAsync(company.Id, delivered.ToList(), consumes, now, cancellationToken);
            await FinishSlotAsync(company.Id, slot, now, cancellationToken);
            _logger.LogInformation(
                "Clock alerts sent for company {CompanyId} slot {SlotOn} {SlotKind} to {RecipientCount} recipients",
                company.Id,
                slot.SlotOn,
                slot.Kind,
                succeeded);
        }
    }

    private async Task<bool> TryClaimAsync(
        int companyId,
        ClockAlertSchedule.Slot slot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _db.ClockAlertSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.CompanyId == companyId && row.SlotOn == slot.SlotOn && row.SlotKind == slot.Kind,
                cancellationToken);
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
            _db.ClockAlertSlots.Add(new ClockAlertSlot
            {
                CompanyId = companyId,
                SlotOn = slot.SlotOn,
                SlotKind = slot.Kind,
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
        var updated = await _db.ClockAlertSlots
            .Where(row => row.CompanyId == companyId
                && row.SlotOn == slot.SlotOn
                && row.SlotKind == slot.Kind
                && row.SentAt == null
                && row.ClaimedAt == claimedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ClaimedAt, now), cancellationToken);
        return updated == 1;
    }

    private async Task ReleaseAsync(
        int companyId,
        ClockAlertSchedule.Slot slot,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Clock alerts were not sent for company {CompanyId} slot {SlotOn} {SlotKind}",
            companyId,
            slot.SlotOn,
            slot.Kind);
        await _db.ClockAlertSlots
            .Where(row => row.CompanyId == companyId
                && row.SlotOn == slot.SlotOn
                && row.SlotKind == slot.Kind
                && row.SentAt == null)
            .ExecuteDeleteAsync(cancellationToken);
        _db.ChangeTracker.Clear();
    }

    private async Task FinishSlotAsync(
        int companyId,
        ClockAlertSchedule.Slot slot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _db.ClockAlertSlots
            .Where(row => row.CompanyId == companyId
                && row.SlotOn == slot.SlotOn
                && row.SlotKind == slot.Kind
                && row.SentAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SentAt, now), cancellationToken);
        _db.ChangeTracker.Clear();
    }

    private async Task MarkSentAsync(
        int companyId,
        IReadOnlyList<AlertWork> delivered,
        IReadOnlyList<AlertWork> consumes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var alert in delivered.Where(alert => !alert.FollowUp))
        {
            _db.ClockAlertSends.Add(new ClockAlertSend
            {
                CompanyId = companyId,
                TimeBreakId = alert.TimeBreakId,
                TimeEntryId = alert.TimeEntryId,
                FirstSentAt = now
            });
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var followUpIds = delivered
            .Where(alert => alert.FollowUp)
            .Select(alert => alert.ExistingId!.Value)
            .Concat(consumes.Select(alert => alert.ExistingId!.Value))
            .Distinct()
            .ToList();
        if (followUpIds.Count == 0)
        {
            return;
        }

        await _db.ClockAlertSends
            .Where(row => followUpIds.Contains(row.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FollowUpSentAt, now), cancellationToken);
    }

    private string LinkFor(int jobId) => $"{_app.FrontendBaseUrl.TrimEnd('/')}/jobs/{jobId}";

    private async Task<List<AlertWork>> LoadWorkAsync(
        Company company,
        TimeZoneInfo zone,
        ClockAlertSchedule.Slot slot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sends = await _db.ClockAlertSends.AsNoTracking()
            .Where(row => row.CompanyId == company.Id)
            .ToListAsync(cancellationToken);
        var openEntries = await (
            from entry in _db.TimeEntries.AsNoTracking()
            join job in _db.Jobs.AsNoTracking() on entry.JobId equals job.Id
            join person in _db.People.AsNoTracking() on entry.PersonId equals person.Id
            where job.CompanyId == company.Id && entry.ClockOutAt == null
            select new EntryRow(entry.Id, entry.JobId, entry.PersonId, person.Name, job.Title, entry.ClockInAt, entry.ClockOutAt)
        ).ToListAsync(cancellationToken);

        var openIds = openEntries.Select(entry => entry.Id).ToList();
        var trackedBreakIds = sends.Where(row => row.TimeBreakId != null).Select(row => row.TimeBreakId!.Value).ToList();
        var breaks = await _db.TimeBreaks.AsNoTracking()
            .Where(row => openIds.Contains(row.TimeEntryId) || trackedBreakIds.Contains(row.Id))
            .ToListAsync(cancellationToken);
        var extraEntryIds = sends.Where(row => row.TimeEntryId != null).Select(row => row.TimeEntryId!.Value)
            .Concat(breaks.Select(row => row.TimeEntryId))
            .Distinct()
            .Except(openIds)
            .ToList();
        var extraEntries = extraEntryIds.Count == 0
            ? new List<EntryRow>()
            : await (
                from entry in _db.TimeEntries.AsNoTracking()
                join job in _db.Jobs.AsNoTracking() on entry.JobId equals job.Id
                join person in _db.People.AsNoTracking() on entry.PersonId equals person.Id
                where extraEntryIds.Contains(entry.Id)
                select new EntryRow(entry.Id, entry.JobId, entry.PersonId, person.Name, job.Title, entry.ClockInAt, entry.ClockOutAt)
            ).ToListAsync(cancellationToken);

        var entries = openEntries.Concat(extraEntries).ToDictionary(entry => entry.Id);
        var breakMap = breaks.ToDictionary(row => row.Id);
        var sentBreaks = sends.Where(row => row.TimeBreakId != null).Select(row => row.TimeBreakId!.Value).ToHashSet();
        var sentShifts = sends.Where(row => row.TimeEntryId != null).Select(row => row.TimeEntryId!.Value).ToHashSet();
        var slotSeven = ClockAlertSchedule.AtLocal(slot.SlotOn, new TimeOnly(7, 0), zone);
        var morning = slot.Kind == ClockAlertSchedule.Morning;
        var weekend = ClockAlertSchedule.IsWeekend(slot.SlotOn);
        var work = new List<AlertWork>();

        if (morning)
        {
            foreach (var send in sends.Where(row => row.FollowUpSentAt is null))
            {
                var followUp = ClockAlertSchedule.FollowUpInstant(send.FirstSentAt, zone);
                if (followUp > slotSeven)
                {
                    continue;
                }

                var still = TryFollowUp(send, entries, breakMap, zone, now, out var alert);
                if (!weekend && followUp == slotSeven && still)
                {
                    work.Add(alert!);
                }
                else
                {
                    work.Add(Consume(send));
                }
            }
        }

        foreach (var row in breaks.Where(row => row.BreakEndAt is null && !sentBreaks.Contains(row.Id)))
        {
            if (!entries.TryGetValue(row.TimeEntryId, out var entry) || entry.ClockOutAt is not null)
            {
                continue;
            }

            var minutes = ClockAlertSchedule.WholeMinutes(row.BreakStartAt, now);
            if (minutes < ClockAlertSchedule.ThresholdMinutes(row.BreakType))
            {
                continue;
            }

            work.Add(BreakAlert(entry, row, minutes, followUp: false, existingId: null));
        }

        if (slot.Kind == ClockAlertSchedule.Evening && !weekend)
        {
            foreach (var entry in openEntries.Where(entry => !sentShifts.Contains(entry.Id)))
            {
                work.Add(ShiftAlert(entry, zone, followUp: false, existingId: null));
            }
        }

        return work;
    }

    private bool TryFollowUp(
        ClockAlertSend send,
        Dictionary<int, EntryRow> entries,
        Dictionary<int, TimeBreak> breaks,
        TimeZoneInfo zone,
        DateTimeOffset now,
        out AlertWork? alert)
    {
        alert = null;
        if (send.TimeBreakId is int breakId)
        {
            if (!breaks.TryGetValue(breakId, out var row) || row.BreakEndAt is not null)
            {
                return false;
            }

            if (!entries.TryGetValue(row.TimeEntryId, out var entry) || entry.ClockOutAt is not null)
            {
                return false;
            }

            var minutes = ClockAlertSchedule.WholeMinutes(row.BreakStartAt, now);
            if (minutes < ClockAlertSchedule.ThresholdMinutes(row.BreakType))
            {
                return false;
            }

            alert = BreakAlert(entry, row, minutes, followUp: true, existingId: send.Id);
            return true;
        }

        if (send.TimeEntryId is int entryId
            && entries.TryGetValue(entryId, out var shift)
            && shift.ClockOutAt is null)
        {
            alert = ShiftAlert(shift, zone, followUp: true, existingId: send.Id);
            return true;
        }

        return false;
    }

    private AlertWork BreakAlert(EntryRow entry, TimeBreak row, int minutes, bool followUp, int? existingId) =>
        new()
        {
            TimeBreakId = row.Id,
            ExistingId = existingId,
            JobId = entry.JobId,
            PersonId = entry.PersonId,
            PersonName = entry.PersonName,
            JobTitle = entry.JobTitle,
            Link = LinkFor(entry.JobId),
            FollowUp = followUp,
            Send = true,
            Line = ClockAlertMail.BreakLine(row.BreakType, minutes),
            SortAt = row.BreakStartAt,
            SortId = row.Id
        };

    private AlertWork ShiftAlert(EntryRow entry, TimeZoneInfo zone, bool followUp, int? existingId) =>
        new()
        {
            TimeEntryId = entry.Id,
            ExistingId = existingId,
            JobId = entry.JobId,
            PersonId = entry.PersonId,
            PersonName = entry.PersonName,
            JobTitle = entry.JobTitle,
            Link = LinkFor(entry.JobId),
            FollowUp = followUp,
            Send = true,
            Line = ClockAlertMail.ClockedInSince(entry.ClockInAt, zone),
            SortAt = DateTimeOffset.MaxValue,
            SortId = entry.Id
        };

    private static AlertWork Consume(ClockAlertSend send) =>
        new()
        {
            ExistingId = send.Id,
            TimeBreakId = send.TimeBreakId,
            TimeEntryId = send.TimeEntryId,
            FollowUp = true,
            Send = false
        };

    private async Task<Dictionary<AlertWork, List<Recipient>>> LoadRecipientsAsync(
        int companyId,
        IReadOnlyList<AlertWork> alerts,
        CancellationToken cancellationToken)
    {
        var result = alerts.ToDictionary(alert => alert, _ => new List<Recipient>());
        var rows = await (
            from member in _db.CompanyMembers.AsNoTracking()
            join person in _db.People.AsNoTracking() on member.PersonId equals person.Id
            join auth in _db.AuthUsers.AsNoTracking() on person.AuthUserId equals auth.Id
            where member.CompanyId == companyId && member.Status == "active" && auth.IsActive
            select new { person.Id, member.Role, auth.Email }
        ).ToListAsync(cancellationToken);
        var directory = rows
            .Select(row => new DirectoryPerson(row.Id, row.Role, row.Email.Trim()))
            .Where(row => row.Email.Length > 0)
            .ToList();
        var jobIds = alerts.Select(alert => alert.JobId).Distinct().ToList();
        var assignments = await _db.JobAssignments.AsNoTracking()
            .Where(row => jobIds.Contains(row.JobId) && row.UnassignedAt == null)
            .ToListAsync(cancellationToken);
        var owners = directory.Where(person =>
            person.CompanyRole.Equals("owner", StringComparison.OrdinalIgnoreCase)
            || person.CompanyRole.Equals("admin", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var alert in alerts)
        {
            var leads = assignments
                .Where(row => row.JobId == alert.JobId
                    && row.PersonId != alert.PersonId
                    && IsLead(row.AssignmentRole))
                .Select(row => directory.FirstOrDefault(person => person.PersonId == row.PersonId))
                .Where(person => person is not null)
                .Select(person => new Recipient(person!.PersonId, person.Email))
                .ToList();
            if (leads.Count == 0)
            {
                leads = owners
                    .Where(person => person.PersonId != alert.PersonId)
                    .Select(person => new Recipient(person.PersonId, person.Email))
                    .ToList();
            }

            result[alert] = leads;
        }

        return result;
    }

    private static bool IsLead(string assignmentRole) =>
        assignmentRole.Equals("lead", StringComparison.OrdinalIgnoreCase)
        || assignmentRole.Equals("supervisor", StringComparison.OrdinalIgnoreCase);

    private static ClockAlertMailModel BuildModel(
        string companyName,
        ClockAlertSchedule.Slot slot,
        IReadOnlyList<AlertWork> alerts)
    {
        var jobs = alerts
            .GroupBy(alert => alert.JobId)
            .Select(group =>
            {
                var people = group
                    .GroupBy(alert => alert.PersonId)
                    .Select(person => new ClockAlertPersonModel(
                        person.First().PersonName,
                        person.OrderBy(alert => alert.SortAt).ThenBy(alert => alert.SortId).Select(alert => alert.Line!).ToList(),
                        person.Key))
                    .OrderBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(person => person.PersonId)
                    .ToList();
                return new ClockAlertJobModel(group.First().JobTitle, group.First().Link, people, group.Key);
            })
            .OrderBy(job => job.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.JobId)
            .ToList();
        return new ClockAlertMailModel(companyName, slot.SlotOn, slot.Kind == ClockAlertSchedule.Morning, jobs);
    }

    private sealed class AlertWork
    {
        public int? TimeBreakId { get; init; }
        public int? TimeEntryId { get; init; }
        public int? ExistingId { get; init; }
        public int JobId { get; init; }
        public int PersonId { get; init; }
        public string PersonName { get; init; } = "";
        public string JobTitle { get; init; } = "";
        public string Link { get; init; } = "";
        public bool FollowUp { get; init; }
        public bool Send { get; init; }
        public string? Line { get; init; }
        public DateTimeOffset SortAt { get; init; }
        public int SortId { get; init; }
    }

    private sealed record EntryRow(
        int Id,
        int JobId,
        int PersonId,
        string PersonName,
        string JobTitle,
        DateTimeOffset ClockInAt,
        DateTimeOffset? ClockOutAt);

    private sealed record DirectoryPerson(int PersonId, string CompanyRole, string Email);

    private sealed record Recipient(int PersonId, string Email);
}
```

In `backend/Program.cs`, register the service next to `PayrollSummaryService`. Do not register the hosted service in this task.

```csharp
builder.Services.AddScoped<ClockAlertService>();
builder.Services.AddScoped<IClockAlertService>(sp => sp.GetRequiredService<ClockAlertService>());
```

- [ ] **Step 4: Run the integration tests to verify they pass**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~ClockAlertIntegrationTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/Services/IClockAlertService.cs backend/Services/ClockAlertService.cs backend/Program.cs backend.Tests/IntegrationTests/ClockAlertIntegrationTests.cs
git commit -m "Send manager clock alerts on the morning and evening passes"
```

---

### Task 4: Production scheduler

**Files:**
- Create: `backend/Services/ClockAlertHost.cs`
- Create: `backend/Services/ClockAlertScheduler.cs`
- Modify: `backend/Program.cs` (register the hosted service only when `ClockAlertHost.ShouldRun` is true)
- Test: `backend.Tests/ClockAlertSchedulerTests.cs`

**Interfaces:**
- Consumes: `IClockAlertService.SendDueAlertsAsync(CancellationToken cancellationToken)`
- Produces: `ClockAlertHost.ShouldRun(IHostEnvironment environment)` returns `bool`. `ClockAlertScheduler` constructor is `(IServiceScopeFactory scopes, ILogger<ClockAlertScheduler> logger, TimeSpan? interval = null)`.

- [ ] **Step 1: Write the failing scheduler tests**

Create `backend.Tests/ClockAlertSchedulerTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class ClockAlertSchedulerTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("E2E", false)]
    public void ShouldRun_is_production_only(string environmentName, bool expected)
    {
        var environment = new StubEnvironment { EnvironmentName = environmentName };
        Assert.Equal(expected, ClockAlertHost.ShouldRun(environment));
    }

    [Fact]
    public async Task Scheduler_checks_once_before_waiting()
    {
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton<IClockAlertService>(new CallbackAlertService(() =>
        {
            calls++;
            gate.TrySetResult();
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider();
        var scheduler = new ClockAlertScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ClockAlertScheduler>.Instance,
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

    private sealed class CallbackAlertService : IClockAlertService
    {
        private readonly Func<Task> _send;

        public CallbackAlertService(Func<Task> send)
        {
            _send = send;
        }

        public Task SendDueAlertsAsync(CancellationToken cancellationToken) => _send();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~ClockAlertSchedulerTests`

Expected: build fails because `ClockAlertHost` does not exist.

- [ ] **Step 3: Implement the host gate and scheduler**

Create `backend/Services/ClockAlertHost.cs`:

```csharp
using Microsoft.Extensions.Hosting;

namespace VisionPaint.Services;

public static class ClockAlertHost
{
    public static bool ShouldRun(IHostEnvironment environment) => environment.IsProduction();
}
```

Create `backend/Services/ClockAlertScheduler.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VisionPaint.Services;

public sealed class ClockAlertScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ClockAlertScheduler> _logger;
    private readonly TimeSpan _interval;

    public ClockAlertScheduler(
        IServiceScopeFactory scopes,
        ILogger<ClockAlertScheduler> logger,
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
                var service = scope.ServiceProvider.GetRequiredService<IClockAlertService>();
                await service.SendDueAlertsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Clock alert check failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
```

In `backend/Program.cs`, immediately after the `IClockAlertService` registration:

```csharp
if (ClockAlertHost.ShouldRun(builder.Environment))
{
    builder.Services.AddHostedService<ClockAlertScheduler>();
}
```

- [ ] **Step 4: Run the scheduler tests and the clock-alert suite**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~ClockAlert`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/Services/ClockAlertHost.cs backend/Services/ClockAlertScheduler.cs backend/Program.cs backend.Tests/ClockAlertSchedulerTests.cs
git commit -m "Run clock alerts from the production timer"
```

---

### Task 5: Mark the spec and backlog implemented

**Files:**
- Modify: `docs/superpowers/specs/2026-10-31-manager-clock-alerts-design.md`
- Modify: `docs/backlog/reporting-and-notifications.md`

**Interfaces:**
- Consumes: Tasks 1–4 passing
- Produces: the spec status and backlog item 3 say implemented

- [ ] **Step 1: Update the spec status**

Replace the status line in `docs/superpowers/specs/2026-10-31-manager-clock-alerts-design.md` with:

```markdown
**Status:** Implemented (2026-10-31)
```

- [ ] **Step 2: Update the backlog**

In `docs/backlog/reporting-and-notifications.md`, set the status line so item 3 is implemented and item 4 stays noted only:

```markdown
**Status:** Item 1 has a design pending review. Item 2 is implemented. Item 3 is implemented. Item 4 is noted only.
```

Keep the design and plan links under item 3.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-10-31-manager-clock-alerts-design.md docs/backlog/reporting-and-notifications.md
git commit -m "Mark manager clock alerts implemented"
```
