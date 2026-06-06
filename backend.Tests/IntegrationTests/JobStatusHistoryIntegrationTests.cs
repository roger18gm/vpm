using System.Net;
using System.Net.Http.Json;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobStatusHistoryIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public JobStatusHistoryIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetStatusHistory_returns_entries_newest_first()
    {
        var tokens = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "History job", status = "scheduled" });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        await _fixture.Client.PutAsJsonAsync($"/api/jobs/{job!.Id}", new { title = job.Title, status = "in_progress" });

        var history = await _fixture.Client.GetFromJsonAsync<List<JobStatusHistoryDto>>($"/api/jobs/{job.Id}/status-history");
        Assert.NotNull(history);
        Assert.True(history!.Count >= 2);
        Assert.Equal("in_progress", history[0].ToStatus);
        Assert.Equal("scheduled", history[1].ToStatus);
        Assert.False(string.IsNullOrWhiteSpace(history[0].ChangedByName));
    }

    [Fact]
    public async Task Crew_unassigned_gets_404_on_status_history()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Private" });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        var (crewTokens, _) = await TestDataFactory.CreateCrewUserAsync(_fixture);
        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);

        using var response = await _fixture.Client.GetAsync($"/api/jobs/{job!.Id}/status-history");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
