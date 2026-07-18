namespace VisionPaint.Services;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int PasswordResetTokenHours { get; set; } = 1;
}
