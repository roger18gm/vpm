namespace VisionPaint.Models;

public sealed class PasswordResetToken
{
    public Guid Id { get; set; }

    public Guid AuthUserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
