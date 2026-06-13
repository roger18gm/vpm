using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class TimeWeeklyIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public TimeWeeklyIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AuthTokenResponse> BootstrapOwnerAsync()
    {
        return await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));
    }

    private static async Task<(int JobId, int OwnerPersonId)> SeedJobAsync(
        BackendIntegrationFixture fixture,
        AuthTokenResponse owner)
    {
        fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Timesheet job" });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(fixture.Database.ConnectionString)
                .Options);

        var ownerPersonId = await db.People
            .Where(p => p.AuthUserId == owner.User.AuthUserId)
            .Select(p => p.Id)
            .SingleAsync();

        return (job!.Id, ownerPersonId);
    }

    private static string CurrentWeekStartSunday()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date);
        return today.AddDays(-(int)today.DayOfWeek).ToString("yyyy-MM-dd");
    }

    [Fact]
    public async Task GetWeekly_two_jobs_same_day_sums_day_minutes()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, personId) = await SeedJobAsync(_fixture, owner);
        var weekStart = CurrentWeekStartSunday();
        var monday = DateOnly.Parse(weekStart).AddDays(1);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var morningStart = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monday.ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Unspecified), tz);
        var morningEnd = morningStart.AddHours(4);
        var afternoonStart = morningEnd.AddMinutes(15);
        var afternoonEnd = afternoonStart.AddMinutes(30);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        db.TimeEntries.AddRange(
            new TimeEntry
            {
                JobId = jobId,
                PersonId = personId,
                ClockInAt = morningStart,
                ClockOutAt = morningEnd,
                BreakMinutes = 0,
                CreatedAt = morningStart,
                UpdatedAt = morningEnd
            },
            new TimeEntry
            {
                JobId = jobId,
                PersonId = personId,
                ClockInAt = afternoonStart,
                ClockOutAt = afternoonEnd,
                BreakMinutes = 0,
                CreatedAt = afternoonStart,
                UpdatedAt = afternoonEnd
            });
        await db.SaveChangesAsync();

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var sheet = await _fixture.Client.GetFromJsonAsync<WeeklyTimesheetDto>(
            $"/api/time/weekly?weekStart={weekStart}");

        Assert.NotNull(sheet);
        var mondayRow = sheet!.Days.Single(d => d.Date == monday.ToString("yyyy-MM-dd"));
        Assert.Equal(270, mondayRow.WorkMinutes);
        Assert.Equal(2, mondayRow.Sessions.Count);
    }

    [Fact]
    public async Task GetWeekly_returns_break_windows()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, personId) = await SeedJobAsync(_fixture, owner);
        var weekStart = CurrentWeekStartSunday();
        var tuesday = DateOnly.Parse(weekStart).AddDays(2);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var start = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(tuesday.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Unspecified), tz);
        var end = start.AddHours(8);
        var breakStart = start.AddHours(4);
        var breakEnd = breakStart.AddMinutes(30);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var entry = new TimeEntry
        {
            JobId = jobId,
            PersonId = personId,
            ClockInAt = start,
            ClockOutAt = end,
            BreakMinutes = 30,
            CreatedAt = start,
            UpdatedAt = end
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        db.TimeBreaks.Add(new TimeBreak
        {
            TimeEntryId = entry.Id,
            BreakStartAt = breakStart,
            BreakEndAt = breakEnd,
            BreakType = "lunch"
        });
        await db.SaveChangesAsync();

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var sheet = await _fixture.Client.GetFromJsonAsync<WeeklyTimesheetDto>(
            $"/api/time/weekly?weekStart={weekStart}");

        var tuesdayRow = sheet!.Days.Single(d => d.Date == tuesday.ToString("yyyy-MM-dd"));
        var session = Assert.Single(tuesdayRow.Sessions);
        var breakWindow = Assert.Single(session.Breaks);
        Assert.Equal(30, breakWindow.Minutes);
        Assert.Equal("lunch", breakWindow.BreakType);
        Assert.Equal(450, tuesdayRow.WorkMinutes);
        Assert.Equal(30, tuesdayRow.BreakMinutes);
    }

    [Fact]
    public async Task GetWeekly_late_saturday_shift_stays_on_saturday()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, personId) = await SeedJobAsync(_fixture, owner);
        var weekStart = CurrentWeekStartSunday();
        var saturday = DateOnly.Parse(weekStart).AddDays(6);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var clockIn = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(saturday.ToDateTime(new TimeOnly(23, 0)), DateTimeKind.Unspecified), tz);
        var clockOut = clockIn.AddHours(2);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        db.TimeEntries.Add(new TimeEntry
        {
            JobId = jobId,
            PersonId = personId,
            ClockInAt = clockIn,
            ClockOutAt = clockOut,
            BreakMinutes = 0,
            CreatedAt = clockIn,
            UpdatedAt = clockOut
        });
        await db.SaveChangesAsync();

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var sheet = await _fixture.Client.GetFromJsonAsync<WeeklyTimesheetDto>(
            $"/api/time/weekly?weekStart={weekStart}");

        var saturdayRow = sheet!.Days.Single(d => d.Date == saturday.ToString("yyyy-MM-dd"));
        Assert.Equal(120, saturdayRow.WorkMinutes);
        Assert.All(
            sheet.Days.Where(d => d.Date != saturday.ToString("yyyy-MM-dd")),
            day => Assert.Equal(0, day.WorkMinutes));
    }

    [Fact]
    public async Task Manager_can_view_other_worker_timesheet()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture, $"crew-{Guid.NewGuid():N}@example.com");

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var managerEmail = $"manager-{Guid.NewGuid():N}@example.com";
        using var create = await _fixture.Client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Demo Manager",
            managerEmail,
            "Password123!",
            "manager"));
        create.EnsureSuccessStatusCode();

        var manager = await _fixture.AuthClient.LoginAsync(new LoginRequest(managerEmail, "Password123!"));
        _fixture.AuthClient.SetBearerToken(manager.AccessToken);

        var sheet = await _fixture.Client.GetFromJsonAsync<WeeklyTimesheetDto>(
            $"/api/time/weekly?personId={crewPersonId}");

        Assert.NotNull(sheet);
        Assert.Equal(crewPersonId, sheet!.PersonId);
    }

    [Fact]
    public async Task Crew_cannot_view_other_worker_timesheet()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture, $"crew-a-{Guid.NewGuid():N}@example.com");
        var (crewTokens, _) = await TestDataFactory.CreateCrewUserAsync(
            _fixture, $"crew-b-{Guid.NewGuid():N}@example.com");

        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);
        using var response = await _fixture.Client.GetAsync(
            $"/api/time/weekly?personId={crewPersonId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWeekly_non_sunday_weekStart_returns_400()
    {
        var owner = await BootstrapOwnerAsync();
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        using var response = await _fixture.Client.GetAsync("/api/time/weekly?weekStart=2026-06-02");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWeekly_open_entry_is_in_progress()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, personId) = await SeedJobAsync(_fixture, owner);
        var weekStart = CurrentWeekStartSunday();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date);
        var start = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Unspecified), tz);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var entry = new TimeEntry
        {
            JobId = jobId,
            PersonId = personId,
            ClockInAt = start,
            CreatedAt = start,
            UpdatedAt = start
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        db.TimeBreaks.Add(new TimeBreak
        {
            TimeEntryId = entry.Id,
            BreakStartAt = start.AddMinutes(30),
            BreakType = "rest"
        });
        await db.SaveChangesAsync();

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var sheet = await _fixture.Client.GetFromJsonAsync<WeeklyTimesheetDto>(
            $"/api/time/weekly?weekStart={weekStart}");

        var todayRow = sheet!.Days.Single(d => d.Date == today.ToString("yyyy-MM-dd"));
        var session = Assert.Single(todayRow.Sessions);
        Assert.True(session.InProgress);
        Assert.InRange(session.WorkMinutes, 1, 600);
    }

    [Fact]
    public async Task GetWeekly_empty_week_returns_seven_zero_days()
    {
        var owner = await BootstrapOwnerAsync();
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var weekStart = CurrentWeekStartSunday();

        var sheet = await _fixture.Client.GetFromJsonAsync<WeeklyTimesheetDto>(
            $"/api/time/weekly?weekStart={weekStart}");

        Assert.NotNull(sheet);
        Assert.Equal(7, sheet!.Days.Count);
        Assert.All(sheet.Days, day =>
        {
            Assert.Equal(0, day.WorkMinutes);
            Assert.Empty(day.Sessions);
        });
    }
}
