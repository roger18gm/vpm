namespace VisionPaint.Models;

public sealed class JobChecklistItem
{
    public int JobId { get; set; }

    public int TemplateItemId { get; set; }

    public string Status { get; set; } = "pending";

    public int? CompletedByPersonId { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Notes { get; set; }
}
