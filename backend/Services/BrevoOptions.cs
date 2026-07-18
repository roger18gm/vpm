namespace VisionPaint.Services;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public string ApiKey { get; set; } = string.Empty;

    public string SenderEmail { get; set; } = string.Empty;

    public string SenderName { get; set; } = "VisionPaint";

    public string ApiBaseUrl { get; set; } = "https://api.brevo.com";
}
