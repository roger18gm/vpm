namespace VisionPaint.Models;

public sealed class ClockAlertSend
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int? TimeBreakId { get; set; }

    public int? TimeEntryId { get; set; }

    public DateTimeOffset FirstSentAt { get; set; }

    public DateTimeOffset? FollowUpSentAt { get; set; }
}
