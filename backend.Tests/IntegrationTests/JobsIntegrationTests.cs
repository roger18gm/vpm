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
}
