using System.Net;
using System.Net.Http.Json;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class ChangePasswordIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public ChangePasswordIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ChangePassword_updates_hash_keeps_current_session_revokes_others()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var sessionA = await _fixture.AuthClient.BootstrapAsync(
            new BootstrapRequest("Owner", email, "Password123!"));
        var sessionB = await _fixture.AuthClient.LoginAsync(new LoginRequest(email, "Password123!"));

        _fixture.AuthClient.SetBearerToken(sessionA.AccessToken);
        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest("Password123!", "NewPassword123!"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var login = await _fixture.AuthClient.LoginAsync(new LoginRequest(email, "NewPassword123!"));
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));

        using var refreshB = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(sessionB.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshB.StatusCode);

        using var refreshA = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(sessionA.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshA.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_wrong_current_returns_400()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var tokens = await _fixture.AuthClient.BootstrapAsync(
            new BootstrapRequest("Owner", email, "Password123!"));
        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest("WrongPassword!", "NewPassword123!"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_short_new_password_returns_400()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var tokens = await _fixture.AuthClient.BootstrapAsync(
            new BootstrapRequest("Owner", email, "Password123!"));
        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest("Password123!", "short"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_unauthenticated_returns_401()
    {
        _fixture.AuthClient.SetBearerToken(null);
        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest("Password123!", "NewPassword123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
