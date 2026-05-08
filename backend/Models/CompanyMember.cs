namespace VisionPaint.Models;

public sealed class CompanyMember
{
    public int CompanyId { get; set; }

    public int PersonId { get; set; }

    public string Role { get; set; } = "crew";

    public string Status { get; set; } = "active";

    public DateTimeOffset? InvitedAt { get; set; }

    public DateTimeOffset? JoinedAt { get; set; }
}
