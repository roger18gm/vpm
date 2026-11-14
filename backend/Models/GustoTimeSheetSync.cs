namespace VisionPaint.Models;

public sealed class GustoTimeSheetSync
{
    public int TimeEntryId { get; set; }

    public string? GustoTimeSheetUuid { get; set; }

    public string Status { get; set; } = "pending";

    public decimal HoursWorked { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}