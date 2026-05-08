namespace VisionPaint.Models;

public class JobStatusHistory
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public int? ChangedByPersonId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}
