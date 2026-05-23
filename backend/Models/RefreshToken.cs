namespace VisionPaint.Models;

public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid AuthUserId { get; set; }

    public Guid SessionId { get; set; }

    public Guid TokenId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokeReason { get; set; }

    public Guid? ReplacedByTokenId { get; set; }
}
