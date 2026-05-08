using System.ComponentModel.DataAnnotations;

namespace VisionPaint.Models;

public sealed class Job
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int? ClientId { get; set; }

    public int? CreatedByPersonId { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public string Status { get; set; } = "scheduled";

    [Required]
    public string Priority { get; set; } = "normal";

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? StateRegion { get; set; }

    public string? PostalCode { get; set; }

    public string? CountryCode { get; set; }

    public DateTimeOffset? ScheduledStartAt { get; set; }

    public DateTimeOffset? ScheduledEndAt { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
