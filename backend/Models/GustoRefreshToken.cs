namespace VisionPaint.Models;

public sealed class GustoRefreshToken
{
    public int Id { get; set; }

    public string RefreshToken { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}
