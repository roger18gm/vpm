using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class DashboardIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public DashboardIntegrationTests(BackendIntegrationFixture fixture)
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

    private static string CurrentWeekStartSunday()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date);
        return today.AddDays(-(int)today.DayOfWeek).ToString("yyyy-MM-dd");
    }

    [Fact]
    public async Task GetSummary_crew_returns_forbidden()
    {
        var owner = await BootstrapOwnerAsync();
        var (crewTokens, _) = await TestDataFactory.CreateCrewUserAsync(
            _fixture, $"crew-{Guid.NewGuid():N}@example.com");

        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);
        var response = await _fixture.Client.GetAsync("/api/dashboard/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = owner;
    }

    [Fact]
    public async Task GetSummary_manager_returns_hours_completed_and_clocked_in()
    {
        var owner = await BootstrapOwnerAsync();
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Dashboard job A" });
        var jobA = await created.Content.ReadFromJsonAsync<Job>();
        created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Dashboard job B" });
        var jobB = await created.Content.ReadFromJsonAsync<Job>();

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var ownerPersonId = await db.People
            .Where(p => p.AuthUserId == owner.User.AuthUserId)
            .Select(p => p.Id)
            .SingleAsync();

        var weekStart = CurrentWeekStartSunday();
        var monday = DateOnly.Parse(weekStart).AddDays(1);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var shiftStart = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monday.ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Unspecified), tz);
        var shiftEnd = shiftStart.AddHours(3);

        db.TimeEntries.Add(new TimeEntry
        {
            JobId = jobA!.Id,
            PersonId = ownerPersonId,
            ClockInAt = shiftStart,
            ClockOutAt = shiftEnd,
            BreakMinutes = 0,
            CreatedAt = shiftStart,
            UpdatedAt = shiftEnd
        });

        var openClockIn = DateTimeOffset.UtcNow.AddHours(-1);
        db.TimeEntries.Add(new TimeEntry
        {
            JobId = jobB!.Id,
            PersonId = ownerPersonId,
            ClockInAt = openClockIn,
            BreakMinutes = 0,
            CreatedAt = openClockIn,
            UpdatedAt = openClockIn
        });

        var completedAt = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monday.ToDateTime(new TimeOnly(17, 0)), DateTimeKind.Unspecified), tz);
        db.Jobs.Add(new Job
        {
            CompanyId = 1,
            Title = "Completed this week",
            Status = "completed",
            CompletedAt = completedAt,
            CreatedAt = completedAt,
            UpdatedAt = completedAt
        });
        await db.SaveChangesAsync();

        var summary = await _fixture.Client.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary");

        Assert.NotNull(summary);
        Assert.InRange(summary!.HoursThisWeekMinutes, 180, 245);
        Assert.Equal(1, summary.CompletedThisWeekCount);
        Assert.Single(summary.ClockedInWorkers);
        Assert.Equal(jobB.Id, summary.ClockedInWorkers[0].JobId);
        Assert.Equal("Dashboard job B", summary.ClockedInWorkers[0].JobTitle);
        Assert.False(summary.ClockedInWorkers[0].OnBreak);
    }
}
