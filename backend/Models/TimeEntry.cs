namespace VisionPaint.Models;

public sealed class TimeEntry
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public int PersonId { get; set; }

    public DateTimeOffset ClockInAt { get; set; }

    public DateTimeOffset? ClockOutAt { get; set; }

    public int BreakMinutes { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
