using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class PasswordResetIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private static readonly Regex TokenInLink = new(
        @"/reset-password\?token=([^&\s""']+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly BackendIntegrationFixture _fixture;

    public PasswordResetIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
        _fixture.EmailSender.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AppDbContext CreateDb()
    {
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_fixture.Database.ConnectionString)
                .Options);
    }

    private static string ExtractToken(string html)
    {
        var match = TokenInLink.Match(html);
        Assert.True(match.Success, "Reset email HTML did not contain a reset-password token link.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    [Fact]
    public async Task ForgotPassword_unknown_email_returns_generic_200_and_sends_nothing()
    {
        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest("nobody@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_fixture.EmailSender.Sent.IsEmpty);

        await using var db = CreateDb();
        Assert.Equal(0, await db.PasswordResetTokens.CountAsync());
    }

    [Fact]
    public async Task ForgotPassword_known_user_creates_token_and_sends_email()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest("Owner", email, "Password123!"));
        _fixture.EmailSender.Clear();

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_fixture.EmailSender.Sent.TryPeek(out var mail));
        Assert.Equal(email, mail.To, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("/reset-password?token=", mail.Html, StringComparison.Ordinal);

        await using var db = CreateDb();
        Assert.Equal(1, await db.PasswordResetTokens.CountAsync());
    }

    [Fact]
    public async Task ResetPassword_valid_token_updates_password_and_revokes_refresh()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var tokens = await _fixture.AuthClient.BootstrapAsync(
            new BootstrapRequest("Owner", email, "Password123!"));
        var oldRefresh = tokens.RefreshToken;
        _fixture.EmailSender.Clear();

        using var forgot = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        Assert.True(_fixture.EmailSender.Sent.TryDequeue(out var mail));
        var resetToken = ExtractToken(mail.Html);

        using var reset = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest(resetToken, "NewPassword123!"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var login = await _fixture.AuthClient.LoginAsync(new LoginRequest(email, "NewPassword123!"));
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));

        using var refresh = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(oldRefresh));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_bad_token_returns_400()
    {
        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest("not-a-real-token", "Password123!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_used_token_returns_400()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest("Owner", email, "Password123!"));
        _fixture.EmailSender.Clear();

        using var forgot = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        Assert.True(_fixture.EmailSender.Sent.TryDequeue(out var mail));
        var resetToken = ExtractToken(mail.Html);

        using var first = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest(resetToken, "NewPassword123!"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest(resetToken, "AnotherPassword123!"));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_expired_token_returns_400()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest("Owner", email, "Password123!"));
        _fixture.EmailSender.Clear();

        using var forgot = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        Assert.True(_fixture.EmailSender.Sent.TryDequeue(out var mail));
        var resetToken = ExtractToken(mail.Html);

        await using (var db = CreateDb())
        {
            var row = await db.PasswordResetTokens.SingleAsync();
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        using var reset = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest(resetToken, "NewPassword123!"));
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }
}
