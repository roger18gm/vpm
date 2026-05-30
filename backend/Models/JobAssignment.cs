namespace VisionPaint.Models;

public sealed class JobAssignment
{
    public int JobId { get; set; }

    public int PersonId { get; set; }

    public string AssignmentRole { get; set; } = "crew";

    public DateTimeOffset AssignedAt { get; set; }

    public DateTimeOffset? UnassignedAt { get; set; }
}
