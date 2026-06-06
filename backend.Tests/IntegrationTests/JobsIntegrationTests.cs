using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobsIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public JobsIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateJob_normalizes_fields_and_writes_status_history()
    {
        var tokens = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new Job
        {
            Title = "  Exterior touch-up  ",
            Description = "Refresh exterior trim",
            Status = "In Progress",
            Priority = "High",
            City = "Denver"
        });

        created.EnsureSuccessStatusCode();

        var job = await created.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(job);
        Assert.Equal("Exterior touch-up", job!.Title);
        Assert.Equal("in_progress", job.Status);
        Assert.Equal("high", job.Priority);
        Assert.Equal("Denver", job.City);
        Assert.Equal(1, job.CompanyId);
        Assert.NotNull(job.CreatedByPersonId);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var persistedJob = await db.Jobs.SingleAsync();
        Assert.Equal(job.Id, persistedJob.Id);
        Assert.Equal("in_progress", persistedJob.Status);
        Assert.Equal("high", persistedJob.Priority);
        Assert.Equal(1, await db.JobStatusHistories.CountAsync());

        var history = await db.JobStatusHistories.SingleAsync();
        Assert.Equal(job.Id, history.JobId);
        Assert.Null(history.FromStatus);
        Assert.Equal("in_progress", history.ToStatus);
    }

    [Fact]
    public async Task UpdateJob_to_completed_sets_completed_at()
    {
        var tokens = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Finish me", status = "scheduled" });
        created.EnsureSuccessStatusCode();
        var job = await created.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(job);
        Assert.Null(job!.CompletedAt);

        var updated = await _fixture.Client.PutAsJsonAsync($"/api/jobs/{job.Id}", new
        {
            title = job.Title,
            status = "completed"
        });
        updated.EnsureSuccessStatusCode();

        var body = await updated.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(body!.CompletedAt);
    }

    [Fact]
    public async Task CreateJob_in_progress_sets_started_at()
    {
        var tokens = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Already started", status = "in_progress" });
        created.EnsureSuccessStatusCode();

        var job = await created.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(job!.StartedAt);
    }

    [Fact]
    public async Task UpdateJob_status_only_preserves_schedule_dates()
    {
        var tokens = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        var start = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
        var due = new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new
        {
            title = "Scheduled repaint",
            status = "scheduled",
            scheduledStartAt = start,
            scheduledEndAt = end,
            dueAt = due
        });
        created.EnsureSuccessStatusCode();
        var job = await created.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(job);

        var updated = await _fixture.Client.PutAsJsonAsync($"/api/jobs/{job!.Id}", new
        {
            title = job.Title,
            status = "in_progress"
        });
        updated.EnsureSuccessStatusCode();

        var body = await updated.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(body);
        Assert.Equal(start, body!.ScheduledStartAt);
        Assert.Equal(end, body.ScheduledEndAt);
        Assert.Equal(due, body.DueAt);
    }

    [Fact]
    public async Task UpdateJob_can_clear_due_date_when_explicitly_sent()
    {
        var tokens = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        var due = new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new
        {
            title = "Clear due",
            status = "scheduled",
            dueAt = due
        });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        var updated = await _fixture.Client.PutAsJsonAsync($"/api/jobs/{job!.Id}", new
        {
            title = job.Title,
            status = job.Status,
            dueAt = (DateTimeOffset?)null
        });
        updated.EnsureSuccessStatusCode();

        var body = await updated.Content.ReadFromJsonAsync<Job>();
        Assert.Null(body!.DueAt);
    }
}
