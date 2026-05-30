using System.Net.Http.Json;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class PeopleIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public PeopleIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetPeople_returns_active_company_members_for_manager()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        var (crewTokens, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(_fixture);
        _ = crewTokens;

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var people = await _fixture.Client.GetFromJsonAsync<List<PersonSummaryDto>>("/api/people");

        Assert.NotNull(people);
        Assert.Contains(people!, person => person.PersonId == crewPersonId);
    }
}
