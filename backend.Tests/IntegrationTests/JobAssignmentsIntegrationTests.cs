using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobAssignmentsIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public JobAssignmentsIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PutAssignments_replaces_set_and_reopens_soft_unassigned()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(_fixture);
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Assign test" });
        created.EnsureSuccessStatusCode();
        var job = await created.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(job);

        var assign = await _fixture.Client.PutAsJsonAsync(
            $"/api/jobs/{job!.Id}/assignments",
            new ReplaceJobAssignmentsRequest(new[] { crewPersonId }));
        assign.EnsureSuccessStatusCode();

        var clear = await _fixture.Client.PutAsJsonAsync(
            $"/api/jobs/{job.Id}/assignments",
            new ReplaceJobAssignmentsRequest(Array.Empty<int>()));
        clear.EnsureSuccessStatusCode();

        var reassign = await _fixture.Client.PutAsJsonAsync(
            $"/api/jobs/{job.Id}/assignments",
            new ReplaceJobAssignmentsRequest(new[] { crewPersonId }));
        reassign.EnsureSuccessStatusCode();

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var rows = await db.JobAssignments.Where(a => a.JobId == job.Id && a.PersonId == crewPersonId).ToListAsync();
        Assert.Single(rows);
        Assert.Null(rows[0].UnassignedAt);
    }

    [Fact]
    public async Task Crew_cannot_view_unassigned_job()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Hidden" });
        var job = await created.Content.ReadFromJsonAsync<Job>();

        var (crewTokens, _) = await TestDataFactory.CreateCrewUserAsync(_fixture);
        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);

        using var response = await _fixture.Client.GetAsync($"/api/jobs/{job!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
