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
