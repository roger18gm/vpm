namespace VisionPaint.Models;

public sealed class ClockAlertSlot
{
    public int CompanyId { get; set; }

    public DateOnly SlotOn { get; set; }

    public string SlotKind { get; set; } = "morning";

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
