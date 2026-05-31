using System.Net;
using System.Net.Http.Json;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class UsersIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public UsersIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetUsers_returns_login_users_for_admin()
    {
        var owner = await BootstrapOwnerAsync();
        await TestDataFactory.CreateCrewUserAsync(_fixture, $"crew-{Guid.NewGuid():N}@example.com");

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var users = await _fixture.Client.GetFromJsonAsync<List<UserAdminDto>>("/api/users");

        Assert.NotNull(users);
        Assert.Contains(users!, user => user.CompanyRole == "crew");
    }

    [Fact]
    public async Task Manager_cannot_list_users()
    {
        var owner = await BootstrapOwnerAsync();
        var managerEmail = $"manager-{Guid.NewGuid():N}@example.com";

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        using var create = await _fixture.Client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Demo Manager",
            managerEmail,
            "Password123!",
            "manager"));
        create.EnsureSuccessStatusCode();

        var manager = await _fixture.AuthClient.LoginAsync(new LoginRequest(managerEmail, "Password123!"));
        _fixture.AuthClient.SetBearerToken(manager.AccessToken);
        using var response = await _fixture.Client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_allows_login()
    {
        var owner = await BootstrapOwnerAsync();
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        var email = $"newcrew-{Guid.NewGuid():N}@example.com";
        using var create = await _fixture.Client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "New Crew",
            email,
            "Password123!",
            "crew"));
        create.EnsureSuccessStatusCode();

        _fixture.AuthClient.SetBearerToken(null);
        var login = await _fixture.AuthClient.LoginAsync(new LoginRequest(email, "Password123!"));
        Assert.Equal("crew", login.User.CompanyRole);
    }

    [Fact]
    public async Task PatchRole_updates_company_member()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(_fixture, $"patch-{Guid.NewGuid():N}@example.com");

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        using var patch = await _fixture.Client.PatchAsJsonAsync(
            $"/api/users/{crewPersonId}",
            new UpdateUserRoleRequest("manager"));
        patch.EnsureSuccessStatusCode();

        var body = await patch.Content.ReadFromJsonAsync<UserAdminDto>();
        Assert.Equal("manager", body!.CompanyRole);
    }

    [Fact]
    public async Task Cannot_patch_own_role()
    {
        var owner = await BootstrapOwnerAsync();
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        using var patch = await _fixture.Client.PatchAsJsonAsync(
            $"/api/users/{owner.User.PersonId}",
            new UpdateUserRoleRequest("admin"));
        Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
    }

    private async Task<AuthTokenResponse> BootstrapOwnerAsync()
    {
        return await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));
    }
}
