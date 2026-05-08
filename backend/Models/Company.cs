using System.ComponentModel.DataAnnotations;

namespace VisionPaint.Models;

public sealed class Company
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Timezone { get; set; } = "America/Denver";

    [Required]
    public string LanguageCode { get; set; } = "en";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
