namespace VisionPaint.Models;

public sealed class JobPhoto
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public int? JobAreaId { get; set; }

    public string JobStatus { get; set; } = "in_progress";

    public int? UploadedByPersonId { get; set; }

    public string PhotoKind { get; set; } = "progress";

    public string StoragePath { get; set; } = string.Empty;

    public string? Caption { get; set; }

    public DateTimeOffset? TakenAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
