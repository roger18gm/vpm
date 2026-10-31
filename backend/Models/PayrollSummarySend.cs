namespace VisionPaint.Models;

public sealed class PayrollSummarySend
{
    public int CompanyId { get; set; }

    public DateOnly WeekStart { get; set; }

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
