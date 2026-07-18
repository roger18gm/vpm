using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class PasswordResetService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AuthUser> _passwordHasher;
    private readonly IEmailSender _emailSender;
    private readonly AppOptions _app;
    private readonly AuthOptions _auth;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        AppDbContext db,
        IPasswordHasher<AuthUser> passwordHasher,
        IEmailSender emailSender,
        IOptions<AppOptions> app,
        IOptions<AuthOptions> auth,
        ILogger<PasswordResetService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _emailSender = emailSender;
        _app = app.Value;
        _auth = auth.Value;
        _logger = logger;
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await _db.AuthUsers.FirstOrDefaultAsync(
            u => u.Email == normalized && u.IsActive,
            cancellationToken);
        if (user is null)
        {
            return;
        }

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var hash = HashToken(raw);
        var now = DateTimeOffset.UtcNow;

        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            AuthUserId = user.Id,
            TokenHash = hash,
            ExpiresAt = now.AddHours(Math.Max(1, _auth.PasswordResetTokenHours)),
            CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        var link =
            $"{_app.FrontendBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(raw)}";
        var hours = Math.Max(1, _auth.PasswordResetTokenHours);
        var html =
            $"<p>Reset your VisionPaint password:</p><p><a href=\"{link}\">{link}</a></p><p>This link expires in {hours} hour(s). If you did not request this, you can ignore this email.</p>";
        var text = $"Reset your VisionPaint password: {link}\nThis link expires in {hours} hour(s).";

        try
        {
            await _emailSender.SendAsync(
                user.Email,
                "Reset your VisionPaint password",
                html,
                text,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email for user {AuthUserId}", user.Id);
        }
    }

    public async Task<(bool Ok, string? Error)> ResetPasswordAsync(
        string rawToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return (false, "Password must be at least 8 characters.");
        }

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return (false, "Invalid or expired reset link.");
        }

        var hash = HashToken(rawToken);
        var token = await _db.PasswordResetTokens.FirstOrDefaultAsync(
            t => t.TokenHash == hash,
            cancellationToken);
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return (false, "Invalid or expired reset link.");
        }

        var user = await _db.AuthUsers.FirstOrDefaultAsync(
            u => u.Id == token.AuthUserId && u.IsActive,
            cancellationToken);
        if (user is null)
        {
            return (false, "Invalid or expired reset link.");
        }

        var now = DateTimeOffset.UtcNow;
        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        user.UpdatedAt = now;
        token.UsedAt = now;

        var siblings = await _db.PasswordResetTokens
            .Where(t => t.AuthUserId == user.Id && t.UsedAt == null && t.Id != token.Id)
            .ToListAsync(cancellationToken);
        foreach (var sibling in siblings)
        {
            sibling.UsedAt = now;
        }

        var refreshTokens = await _db.RefreshTokens
            .Where(t => t.AuthUserId == user.Id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var refresh in refreshTokens)
        {
            refresh.RevokedAt = now;
            refresh.RevokeReason = "password_reset";
        }

        await _db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
