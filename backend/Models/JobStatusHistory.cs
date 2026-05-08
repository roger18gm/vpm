namespace VisionPaint.Models;

public sealed class JobStatusHistory
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = string.Empty;

    public int? ChangedByPersonId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public string? Reason { get; set; }

    public string? Notes { get; set; }
}
