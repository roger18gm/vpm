using System.Net;
using System.Net.Http.Json;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobsAuthorizationIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public JobsAuthorizationIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Crew_cannot_create_job()
    {
        var (crewTokens, _) = await TestDataFactory.CreateCrewUserAsync(_fixture);
        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);

        using var response = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Blocked" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetJobs_excludes_cancelled_by_default()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Archive me" });
        created.EnsureSuccessStatusCode();
        var job = await created.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(job);

        using var archive = await _fixture.Client.DeleteAsync($"/api/jobs/{job!.Id}");
        archive.EnsureSuccessStatusCode();

        var jobs = await _fixture.Client.GetFromJsonAsync<List<Job>>("/api/jobs");
        Assert.NotNull(jobs);
        Assert.DoesNotContain(jobs!, j => j.Id == job.Id);
    }
}
