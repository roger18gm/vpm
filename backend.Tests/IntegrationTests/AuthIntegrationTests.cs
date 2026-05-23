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

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var authUser = await db.AuthUsers.SingleAsync();
        Assert.NotNull(authUser.LastLoginAt);
    }
}
