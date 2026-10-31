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
        var openBreaks = new[]
        {
            new TimeBreak { BreakStartAt = openClockIn.AddMinutes(75), BreakEndAt = null, BreakType = "rest" }
        };
        var openBefore = TimeEntryMinutes.Compute(open, openBreaks, before);
        var openAfter = TimeEntryMinutes.Compute(open, openBreaks, after);

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
