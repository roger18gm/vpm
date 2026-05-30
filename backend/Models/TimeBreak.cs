namespace VisionPaint.Models;

public sealed class TimeBreak
{
    public int Id { get; set; }

    public int TimeEntryId { get; set; }

    public DateTimeOffset BreakStartAt { get; set; }

    public DateTimeOffset? BreakEndAt { get; set; }

    public string BreakType { get; set; } = "rest";

    public string? Notes { get; set; }
}
