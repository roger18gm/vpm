using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class TimeEntryMutationIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public TimeEntryMutationIntegrationTests(BackendIntegrationFixture fixture)
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
        var created = await fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Mutation job" });
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

    private static (DateTimeOffset ClockIn, DateTimeOffset ClockOut) CurrentWeekRange(int dayOffset = 1)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date);
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var day = weekStart.AddDays(dayOffset);
        var clockIn = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Unspecified),
            tz);
        return (clockIn, clockIn.AddHours(4));
    }

    [Fact]
    public async Task CreateEntry_crew_can_add_manual_time_current_week()
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
            15,
            "Forgot to clock in"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<WeeklyTimesheetSessionDto>();
        Assert.NotNull(session);
        Assert.Equal(225, session!.WorkMinutes);
        Assert.Equal(15, session.BreakMinutes);
    }

    [Fact]
    public async Task CreateEntry_crew_cannot_add_time_outside_current_week()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, _) = await SeedJobAsync(_fixture, owner);
        var (crewTokens, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture, $"crew-{Guid.NewGuid():N}@example.com");

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        db.JobAssignments.Add(new JobAssignment
        {
            JobId = jobId,
            PersonId = crewPersonId,
            AssignmentRole = "crew",
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date);
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var lastWeek = weekStart.AddDays(-1);
        var clockIn = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(lastWeek.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Unspecified),
            tz);

        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);
        using var response = await _fixture.Client.PostAsJsonAsync("/api/time/entries", new CreateTimeEntryRequest(
            jobId,
            clockIn,
            clockIn.AddHours(4),
            null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEntry_crew_can_change_job()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, personId) = await SeedJobAsync(_fixture, owner);
        var (clockIn, clockOut) = CurrentWeekRange();

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var secondJob = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Correct job" });
        var job2 = await secondJob.Content.ReadFromJsonAsync<Job>();

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
            BreakMinutes = 0,
            CreatedAt = clockIn,
            UpdatedAt = clockOut
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        using var response = await _fixture.Client.PutAsJsonAsync(
            $"/api/time/entries/{entry.Id}",
            new UpdateTimeEntryRequest(job2!.Id, clockIn, clockOut));

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<WeeklyTimesheetSessionDto>();
        Assert.Equal(job2.Id, session!.JobId);
        Assert.Equal("Correct job", session.JobTitle);
    }

    [Fact]
    public async Task Manager_can_update_other_worker_entry()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, _) = await SeedJobAsync(_fixture, owner);
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture, $"crew-{Guid.NewGuid():N}@example.com");
        var (clockIn, clockOut) = CurrentWeekRange();

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        db.JobAssignments.Add(new JobAssignment
        {
            JobId = jobId,
            PersonId = crewPersonId,
            AssignmentRole = "crew",
            AssignedAt = DateTimeOffset.UtcNow
        });

        var entry = new TimeEntry
        {
            JobId = jobId,
            PersonId = crewPersonId,
            ClockInAt = clockIn,
            ClockOutAt = clockOut,
            BreakMinutes = 0,
            CreatedAt = clockIn,
            UpdatedAt = clockOut
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        using var response = await _fixture.Client.PutAsJsonAsync(
            $"/api/time/entries/{entry.Id}",
            new UpdateTimeEntryRequest(jobId, clockIn, clockOut.AddMinutes(30), 0, "Manager correction"));

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<WeeklyTimesheetSessionDto>();
        Assert.Equal(270, session!.WorkMinutes);
    }

    [Fact]
    public async Task Crew_cannot_update_other_worker_entry()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, ownerPersonId) = await SeedJobAsync(_fixture, owner);
        var (crewTokens, _) = await TestDataFactory.CreateCrewUserAsync(
            _fixture, $"crew-{Guid.NewGuid():N}@example.com");
        var (clockIn, clockOut) = CurrentWeekRange();

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var entry = new TimeEntry
        {
            JobId = jobId,
            PersonId = ownerPersonId,
            ClockInAt = clockIn,
            ClockOutAt = clockOut,
            BreakMinutes = 0,
            CreatedAt = clockIn,
            UpdatedAt = clockOut
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);
        using var response = await _fixture.Client.PutAsJsonAsync(
            $"/api/time/entries/{entry.Id}",
            new UpdateTimeEntryRequest(jobId, clockIn, clockOut));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEntry_removes_session()
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
            BreakMinutes = 0,
            CreatedAt = clockIn,
            UpdatedAt = clockOut
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        using var response = await _fixture.Client.DeleteAsync($"/api/time/entries/{entry.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var remaining = await db.TimeEntries.CountAsync(e => e.Id == entry.Id);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task Crew_cannot_edit_in_progress_entry()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, _) = await SeedJobAsync(_fixture, owner);
        var (crewTokens, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture, $"crew-{Guid.NewGuid():N}@example.com");
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date);
        var clockIn = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Unspecified),
            tz);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        db.JobAssignments.Add(new JobAssignment
        {
            JobId = jobId,
            PersonId = crewPersonId,
            AssignmentRole = "crew",
            AssignedAt = DateTimeOffset.UtcNow
        });

        var entry = new TimeEntry
        {
            JobId = jobId,
            PersonId = crewPersonId,
            ClockInAt = clockIn,
            CreatedAt = clockIn,
            UpdatedAt = clockIn
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);
        using var response = await _fixture.Client.PutAsJsonAsync(
            $"/api/time/entries/{entry.Id}",
            new UpdateTimeEntryRequest(jobId, clockIn, clockIn.AddHours(1)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
