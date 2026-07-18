using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace VisionPaint.Services;

public sealed class BrevoEmailSender : IEmailSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BrevoOptions _options;

    public BrevoEmailSender(IHttpClientFactory httpClientFactory, IOptions<BrevoOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _options.ApiKey = Environment.GetEnvironmentVariable("VISIONPAINT_BREVO_API_KEY") ?? string.Empty;
        }
    }

    public async Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Set Brevo:ApiKey or VISIONPAINT_BREVO_API_KEY to send email.");
        }

        if (string.IsNullOrWhiteSpace(_options.SenderEmail))
        {
            throw new InvalidOperationException("Set Brevo:SenderEmail to a verified Brevo sender.");
        }

        var client = _httpClientFactory.CreateClient("Brevo");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.ApiBaseUrl.TrimEnd('/')}/v3/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);
        request.Content = JsonContent.Create(new BrevoSendRequest(
            new BrevoSender(_options.SenderName, _options.SenderEmail),
            new[] { new BrevoRecipient(toEmail) },
            subject,
            htmlBody,
            textBody));

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Brevo send failed with {(int)response.StatusCode}: {body}");
        }
    }

    private sealed record BrevoSendRequest(
        [property: JsonPropertyName("sender")] BrevoSender Sender,
        [property: JsonPropertyName("to")] IReadOnlyList<BrevoRecipient> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("htmlContent")] string HtmlContent,
        [property: JsonPropertyName("textContent")] string? TextContent);

    private sealed record BrevoSender(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email);

    private sealed record BrevoRecipient(
        [property: JsonPropertyName("email")] string Email);
}
