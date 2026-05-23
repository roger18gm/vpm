using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class AuthIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public AuthIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Bootstrap_returns_tokens_and_authenticates_requests()
    {
        var status = await _fixture.AuthClient.GetStatusAsync();

        Assert.False(status.IsAuthenticated);
        Assert.True(status.CanBootstrap);

        var tokens = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        Assert.Equal("owner@example.com", tokens.User.Email);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));

        _fixture.AuthClient.SetBearerToken(tokens.AccessToken);

        var authenticatedStatus = await _fixture.AuthClient.GetStatusAsync();
        Assert.True(authenticatedStatus.IsAuthenticated);
        Assert.NotNull(authenticatedStatus.User);
        Assert.Equal(tokens.User.AuthUserId, authenticatedStatus.User!.AuthUserId);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        Assert.Equal(1, await db.AuthUsers.CountAsync());
        Assert.Equal(1, await db.People.CountAsync());
        Assert.Equal(1, await db.CompanyMembers.CountAsync());
    }

    [Fact]
    public async Task Login_and_refresh_restore_access_after_logout()
    {
        var bootstrap = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(bootstrap.AccessToken);
        await _fixture.AuthClient.LogoutAsync();

        var loggedOutStatus = await _fixture.AuthClient.GetStatusAsync();
        Assert.False(loggedOutStatus.IsAuthenticated);

        var login = await _fixture.AuthClient.LoginAsync(new LoginRequest(
            "Owner@Example.com",
            "Password123!"));

        Assert.Equal("owner@example.com", login.User.Email);
        _fixture.AuthClient.SetBearerToken(login.AccessToken);

        var authenticatedStatus = await _fixture.AuthClient.GetStatusAsync();
        Assert.True(authenticatedStatus.IsAuthenticated);

        var refreshed = await _fixture.AuthClient.RefreshAsync(login.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.NotEqual(login.AccessToken, refreshed.AccessToken);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var authUser = await db.AuthUsers.SingleAsync();
        Assert.NotNull(authUser.LastLoginAt);
    }

    [Fact]
    public async Task Refresh_returns_unauthorized_when_auth_user_is_inactive()
    {
        var bootstrap = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        await using (var db = new AppDbContext(
                         new DbContextOptionsBuilder<AppDbContext>()
                             .UseNpgsql(_fixture.Database.ConnectionString)
                             .Options))
        {
            var authUser = await db.AuthUsers.SingleAsync();
            authUser.IsActive = false;
            authUser.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(bootstrap.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_cannot_access_protected_endpoints()
    {
        var bootstrap = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(bootstrap.RefreshToken);

        using var response = await _fixture.Client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotation_revokes_the_previous_refresh_token()
    {
        var bootstrap = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        var rotated = await _fixture.AuthClient.RefreshAsync(bootstrap.RefreshToken);

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(bootstrap.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(rotated.RefreshToken));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_session()
    {
        var bootstrap = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(bootstrap.AccessToken);
        await _fixture.AuthClient.LogoutAsync();

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(bootstrap.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_entire_session_chain()
    {
        var bootstrap = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        var rotated = await _fixture.AuthClient.RefreshAsync(bootstrap.RefreshToken);

        using var replayResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(bootstrap.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        using var rotatedResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(rotated.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, rotatedResponse.StatusCode);
    }
}
