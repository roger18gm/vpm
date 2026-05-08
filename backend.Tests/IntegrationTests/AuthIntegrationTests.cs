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
    public async Task Bootstrap_creates_auth_records_and_authenticates_the_session()
    {
        var status = await _fixture.AuthClient.GetStatusAsync();

        Assert.False(status.IsAuthenticated);
        Assert.True(status.CanBootstrap);
        Assert.False(string.IsNullOrWhiteSpace(status.CsrfToken));

        var user = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        Assert.Equal("owner@example.com", user.Email);
        Assert.False(string.IsNullOrWhiteSpace(user.CsrfToken));

        var authenticatedStatus = await _fixture.AuthClient.GetStatusAsync();
        Assert.True(authenticatedStatus.IsAuthenticated);
        Assert.NotNull(authenticatedStatus.User);
        Assert.Equal(user.AuthUserId, authenticatedStatus.User!.AuthUserId);
        Assert.Equal(user.PersonId, authenticatedStatus.User.PersonId);
        Assert.Equal(user.CompanyId, authenticatedStatus.User.CompanyId);
        Assert.Equal(user.CompanyRole, authenticatedStatus.User.CompanyRole);
        Assert.Equal(user.PersonName, authenticatedStatus.User.PersonName);
        Assert.Equal(user.Email, authenticatedStatus.User.Email);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        Assert.Equal(1, await db.AuthUsers.CountAsync());
        Assert.Equal(1, await db.People.CountAsync());
        Assert.Equal(1, await db.CompanyMembers.CountAsync());
    }

    [Fact]
    public async Task Login_restores_the_session_after_logout()
    {
        await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            "Owner@Example.com",
            "Password123!"));

        await _fixture.AuthClient.LogoutAsync();

        var loggedOutStatus = await _fixture.AuthClient.GetStatusAsync();
        Assert.False(loggedOutStatus.IsAuthenticated);

        var user = await _fixture.AuthClient.LoginAsync(new LoginRequest(
            "Owner@Example.com",
            "Password123!"));

        Assert.Equal("owner@example.com", user.Email);

        var authenticatedStatus = await _fixture.AuthClient.GetStatusAsync();
        Assert.True(authenticatedStatus.IsAuthenticated);
        Assert.NotNull(authenticatedStatus.User);
        Assert.Equal(user.AuthUserId, authenticatedStatus.User!.AuthUserId);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);

        var authUser = await db.AuthUsers.SingleAsync();
        Assert.NotNull(authUser.LastLoginAt);
        Assert.Equal("owner@example.com", authUser.Email);
    }
}
