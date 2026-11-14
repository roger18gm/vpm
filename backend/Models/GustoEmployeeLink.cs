namespace VisionPaint.Models;

public sealed class GustoEmployeeLink
{
    public int PersonId { get; set; }

    public int CompanyId { get; set; }

    public string GustoEmployeeUuid { get; set; } = "";

    public string GustoJobUuid { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}