using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class GustoTimeSyncIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public GustoTimeSyncIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
        _fixture.Factory.Services.GetRequiredService<StubGustoHandler>().Reset();
        _fixture.Factory.Services.GetRequiredService<GustoAccessTokenCache>().Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Schema_can_store_a_link_and_a_sync_row()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Schema job" });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var personId = await db.People
            .Where(p => p.AuthUserId == owner.User.AuthUserId)
            .Select(p => p.Id)
            .SingleAsync();
        var companyId = await db.CompanyMembers
            .Where(m => m.PersonId == personId)
            .Select(m => m.CompanyId)
            .SingleAsync();
        var now = DateTimeOffset.UtcNow;
        var entry = new TimeEntry
        {
            JobId = job!.Id,
            PersonId = personId,
            ClockInAt = now.AddHours(-2),
            ClockOutAt = now.AddHours(-1),
            BreakMinutes = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        db.GustoEmployeeLinks.Add(new GustoEmployeeLink
        {
            PersonId = personId,
            CompanyId = companyId,
            GustoEmployeeUuid = "emp-1",
            GustoJobUuid = "job-1",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.GustoTimeSheetSyncs.Add(new GustoTimeSheetSync
        {
            TimeEntryId = entry.Id,
            Status = "pending",
            HoursWorked = 1.0000m,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var link = await db.GustoEmployeeLinks.SingleAsync(row => row.PersonId == personId);
        var sync = await db.GustoTimeSheetSyncs.SingleAsync(row => row.TimeEntryId == entry.Id);
        Assert.Equal("emp-1", link.GustoEmployeeUuid);
        Assert.Equal("pending", sync.Status);
    }

    [Fact]
    public async Task Manager_sends_two_closed_shifts_as_regular_hours()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, personId, companyId) = await SeedJobAsync(owner, "North house");
        await LinkPersonAsync(personId, companyId);
        var weekStart = CurrentWeekStartSunday();
        var first = MondayShift(weekStart, 8, 240);
        var second = MondayShift(weekStart, 13, 30);
        var firstId = await AddShiftAsync(jobId, personId, first.Start, first.End);
        var secondId = await AddShiftAsync(jobId, personId, second.Start, second.End);
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        var sawPending = false;
        handler.OnTimeSheet = _ =>
        {
            using var db = CreateDb();
            sawPending = db.GustoTimeSheetSyncs.Any(row => row.Status == "pending");
        };

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/time/weekly/gusto-sync",
            new { weekStart, personId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GustoWeekSyncResultDto>();
        Assert.Equal(2, body!.Sent);
        Assert.Equal(0, body.AlreadySynced);
        Assert.True(sawPending);
        Assert.Equal(2, handler.TimeSheetBodies.Count);
        Assert.Contains(handler.TimeSheetBodies, json => SheetMatches(json, firstId, 4.0000m, "North house"));
        Assert.Contains(handler.TimeSheetBodies, json => SheetMatches(json, secondId, 0.5000m, "North house"));

        await using var db = CreateDb();
        var rows = await db.GustoTimeSheetSyncs.OrderBy(row => row.TimeEntryId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("synced", row.Status));
        Assert.Contains(rows, row => row.TimeEntryId == firstId && row.HoursWorked == 4.0000m);
        Assert.Contains(rows, row => row.TimeEntryId == secondId && row.HoursWorked == 0.5000m);

        var namedClient = _fixture.Factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(GustoTimeClient.HttpClientName);
        Assert.Equal(TimeSpan.FromSeconds(30), namedClient.Timeout);

        var sheet = await _fixture.Client.GetFromJsonAsync<WeeklyTimesheetDto>(
            $"/api/time/weekly?weekStart={weekStart}&personId={personId}");
        var sessions = sheet!.Days.SelectMany(day => day.Sessions).ToList();
        Assert.Contains(sessions, session => session.TimeEntryId == firstId && session.GustoSyncStatus == "synced");
        Assert.Contains(sessions, session => session.TimeEntryId == secondId && session.GustoSyncStatus == "synced");
    }

    [Fact]
    public async Task Second_send_skips_synced_shifts_even_after_an_edit()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, personId, companyId) = await SeedJobAsync(owner, "Edit house");
        await LinkPersonAsync(personId, companyId);
        var weekStart = CurrentWeekStartSunday();
        var shift = MondayShift(weekStart, 8, 60);
        var entryId = await AddShiftAsync(jobId, personId, shift.Start, shift.End);
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        await _fixture.Client.PostAsJsonAsync("/api/time/weekly/gusto-sync", new { weekStart, personId });
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        handler.Reset();

        await using (var db = CreateDb())
        {
            var entry = await db.TimeEntries.SingleAsync(row => row.Id == entryId);
            entry.ClockOutAt = entry.ClockInAt.AddHours(3);
            await db.SaveChangesAsync();
        }

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/time/weekly/gusto-sync",
            new { weekStart, personId });
        var body = await response.Content.ReadFromJsonAsync<GustoWeekSyncResultDto>();
        Assert.Equal(0, body!.Sent);
        Assert.Equal(1, body.AlreadySynced);
        Assert.Empty(handler.TimeSheetBodies);
    }

    [Fact]
    public async Task Open_and_zero_hour_shifts_are_skipped()
    {
        var owner = await BootstrapOwnerAsync();
        var (jobId, personId, companyId) = await SeedJobAsync(owner, "Skip house");
        await LinkPersonAsync(personId, companyId);
        var weekStart = CurrentWeekStartSunday();
        var open = MondayShift(weekStart, 8, 60);
        var zero = MondayShift(weekStart, 13, 60);
        await AddShiftAsync(jobId, personId, open.Start, null);
        await AddShiftAsync(jobId, personId, zero.Start, zero.End, breakMinutes: 60);
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/time/weekly/gusto-sync",
            new { weekStart, personId });
        var body = await response.Content.ReadFromJsonAsync<GustoWeekSyncResultDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body!.Sent);
        Assert.Equal(1, body.SkippedOpen);
        Assert.Equal(1, body.SkippedZero);
        Assert.Empty(handler.TimeSheetBodies);
        await using var db = CreateDb();
        Assert.Empty(await db.GustoTimeSheetSyncs.ToListAsync());
    }

    [Fact]
    public async Task Crew_cannot_send()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, _, _) = await SeedJobAsync(owner, "Crew house");
        var (crew, _) = await TestDataFactory.CreateCrewUserAsync(_fixture, $"crew-{Guid.NewGuid():N}@example.com");
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        _fixture.AuthClient.SetBearerToken(crew.AccessToken);

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/time/weekly/gusto-sync",
            new { weekStart = CurrentWeekStartSunday(), personId = crew.User.PersonId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(handler.TimeSheetBodies);
    }

    [Fact]
    public async Task Unlinked_worker_returns_422()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, personId, _) = await SeedJobAsync(owner, "Unlinked house");
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/time/weekly/gusto-sync",
            new { weekStart = CurrentWeekStartSunday(), personId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("not linked to Gusto", text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.TimeSheetBodies);
    }

    [Fact]
    public async Task Week_start_must_be_sunday_and_person_must_exist()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, personId, _) = await SeedJobAsync(owner, "Validation house");
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var monday = DateOnly.Parse(CurrentWeekStartSunday()).AddDays(1).ToString("yyyy-MM-dd");

        var badWeek = await _fixture.Client.PostAsJsonAsync(
            "/api/time/weekly/gusto-sync",
            new { weekStart = monday, personId });
        var missing = await _fixture.Client.PostAsJsonAsync(
            "/api/time/weekly/gusto-sync",
            new { weekStart = CurrentWeekStartSunday(), personId = 999999 });

        Assert.Equal(HttpStatusCode.BadRequest, badWeek.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

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

    private async Task<(int JobId, int PersonId, int CompanyId)> SeedJobAsync(AuthTokenResponse owner, string title)
    {
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title });
        var job = await created.Content.ReadFromJsonAsync<Job>();
        await using var db = CreateDb();
        var personId = await db.People.Where(p => p.AuthUserId == owner.User.AuthUserId).Select(p => p.Id).SingleAsync();
        var companyId = await db.CompanyMembers.Where(m => m.PersonId == personId).Select(m => m.CompanyId).SingleAsync();
        return (job!.Id, personId, companyId);
    }

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.Database.ConnectionString).Options);

    private async Task<int> AddShiftAsync(
        int jobId,
        int personId,
        DateTimeOffset clockIn,
        DateTimeOffset? clockOut,
        int breakMinutes = 0)
    {
        await using var db = CreateDb();
        var entry = new TimeEntry
        {
            JobId = jobId,
            PersonId = personId,
            ClockInAt = clockIn,
            ClockOutAt = clockOut,
            BreakMinutes = breakMinutes,
            CreatedAt = clockIn,
            UpdatedAt = clockOut ?? clockIn
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task LinkPersonAsync(int personId, int companyId)
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.GustoEmployeeLinks.Add(new GustoEmployeeLink
        {
            PersonId = personId,
            CompanyId = companyId,
            GustoEmployeeUuid = "emp-1",
            GustoJobUuid = "comp-job-1",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static bool SheetMatches(string json, int timeEntryId, decimal hours, string jobTitle)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var entry = root.GetProperty("entries")[0];
        var metadata = root.GetProperty("metadata");
        return entry.GetProperty("hours_worked").GetDecimal() == hours
            && entry.GetProperty("pay_classification").GetString() == "Regular"
            && root.GetProperty("time_zone").GetString() == "America/Denver"
            && metadata.GetProperty("visionpaint_time_entry_id").GetString() == timeEntryId.ToString()
            && metadata.GetProperty("job_title").GetString() == jobTitle;
    }

    private static (DateTimeOffset Start, DateTimeOffset End) MondayShift(string weekStart, int startHour, int minutes)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var monday = DateOnly.Parse(weekStart).AddDays(1);
        var start = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monday.ToDateTime(new TimeOnly(startHour, 0)), DateTimeKind.Unspecified),
            tz);
        return (start, start.AddMinutes(minutes));
    }
}
