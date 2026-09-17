namespace VisionPaint.Models;

public sealed class JobArea
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public int? ParentJobAreaId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = "not_started";

    public int SortOrder { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
