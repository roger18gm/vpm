using System.ComponentModel.DataAnnotations;

namespace VisionPaint.Models;

public sealed class Person
{
    public int Id { get; set; }

    public Guid? AuthUserId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
