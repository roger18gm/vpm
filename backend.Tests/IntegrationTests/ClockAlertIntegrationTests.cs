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
