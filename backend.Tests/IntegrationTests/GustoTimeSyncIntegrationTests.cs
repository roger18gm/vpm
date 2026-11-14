using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
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
}