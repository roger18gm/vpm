namespace VisionPaint.Services;

public sealed class GustoOptions
{
    public const string SectionName = "Gusto";

    public const string DemoBaseUrl = "https://api.gusto-demo.com";

    public string BaseUrl { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public string CompanyUuid { get; set; } = "";

    public string ApiVersion { get; set; } = "2026-06-15";

    public bool IsDemoConfigured()
    {
        var baseUrl = BaseUrl.Trim().TrimEnd('/');
        return baseUrl == DemoBaseUrl
            && !string.IsNullOrWhiteSpace(ClientId)
            && !string.IsNullOrWhiteSpace(ClientSecret)
            && !string.IsNullOrWhiteSpace(RefreshToken)
            && !string.IsNullOrWhiteSpace(CompanyUuid)
            && ApiVersion.Trim() == "2026-06-15";
    }
}

public sealed class GustoNotConfiguredException : Exception
{
    public GustoNotConfiguredException()
        : base("The Gusto demo is not configured or could not be reached.")
    {
    }
}
