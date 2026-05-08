namespace VisionPaint.Models;

public class Job
{
    public int Id { get; set; }
    public int CompanyId { get; set; } = 1;
    public int? ClientId { get; set; }
    public int? CreatedByPersonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "scheduled";
    public string Priority { get; set; } = "normal";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? StateRegion { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public DateTime? ScheduledStartAt { get; set; }
    public DateTime? ScheduledEndAt { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
