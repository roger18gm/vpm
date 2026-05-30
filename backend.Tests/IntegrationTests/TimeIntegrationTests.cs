using System.Net;
using System.Net.Http.Json;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class TimeIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public TimeIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ClockIn_twice_returns_409()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Clock job" });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        var first = await _fixture.Client.PostAsJsonAsync("/api/time/clock-in", new ClockInRequest(job!.Id));
        first.EnsureSuccessStatusCode();

        using var second = await _fixture.Client.PostAsJsonAsync("/api/time/clock-in", new ClockInRequest(job.Id));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Crew_cannot_clock_in_unassigned_job()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "No assign" });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        var (crewTokens, _) = await TestDataFactory.CreateCrewUserAsync(_fixture);
        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);

        using var response = await _fixture.Client.PostAsJsonAsync("/api/time/clock-in", new ClockInRequest(job!.Id));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
