using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace VisionPaint.Services;

public sealed class GustoTimeClient : IGustoTimeClient
{
    public const string HttpClientName = "Gusto";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GustoOptions _options;
    private readonly GustoAccessTokenCache _cache;

    public GustoTimeClient(
        IHttpClientFactory httpClientFactory,
        IOptions<GustoOptions> options,
        GustoAccessTokenCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _cache = cache;
    }

    public Task PrepareAsync(CancellationToken cancellationToken) =>
        GetTokenAsync(forceRefresh: false, cancellationToken);

    public async Task<GustoTimeSheetResult> PostTimeSheetAsync(
        GustoTimeSheetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await GetTokenAsync(forceRefresh: false, cancellationToken);
            var response = await SendSheetAsync(token, request, cancellationToken);
            if (response.StatusCode != (int)System.Net.HttpStatusCode.Unauthorized)
            {
                return response;
            }

            token = await GetTokenAsync(forceRefresh: true, cancellationToken);
            response = await SendSheetAsync(token, request, cancellationToken);
            if (response.StatusCode == (int)System.Net.HttpStatusCode.Unauthorized)
            {
                return response with { Accepted = false, StopBatch = true };
            }

            return response;
        }
        catch (GustoNotConfiguredException ex)
        {
            return new GustoTimeSheetResult(false, null, 0, ex.Message, true);
        }
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!_options.IsDemoConfigured())
        {
            throw new GustoNotConfiguredException();
        }

        if (!forceRefresh && _cache.TryGet(DateTimeOffset.UtcNow, out var cached))
        {
            return cached;
        }

        if (forceRefresh)
        {
            _cache.Clear();
        }

        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.PostAsJsonAsync(
            $"{GustoOptions.DemoBaseUrl}/oauth/token",
            new
            {
                client_id = _options.ClientId,
                client_secret = _options.ClientSecret,
                refresh_token = _options.RefreshToken,
                grant_type = "refresh_token"
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new GustoNotConfiguredException();
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new GustoNotConfiguredException();
        }

        var expiresIn = payload.ExpiresIn <= 0 ? 7200 : payload.ExpiresIn;
        _cache.Set(payload.AccessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        return payload.AccessToken;
    }

    private async Task<GustoTimeSheetResult> SendSheetAsync(
        string token,
        GustoTimeSheetRequest request,
        CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl.Trim().TrimEnd('/')}/v1/companies/{_options.CompanyUuid}/time_tracking/time_sheets");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.TryAddWithoutValidation("X-Gusto-API-Version", _options.ApiVersion);
        message.Content = JsonContent.Create(new
        {
            entity_uuid = request.EmployeeUuid,
            entity_type = "Employee",
            job_uuid = request.CompensationJobUuid,
            time_zone = request.TimeZone,
            shift_started_at = request.ShiftStartedAt,
            shift_ended_at = request.ShiftEndedAt,
            entries = new[]
            {
                new { hours_worked = request.HoursWorked, pay_classification = "Regular" }
            },
            metadata = new Dictionary<string, string>
            {
                ["visionpaint_time_entry_id"] = request.TimeEntryId.ToString(),
                ["job_title"] = request.JobTitle
            }
        });

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GustoTimeSheetResult(false, null, 0, "The Gusto request timed out.", false);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if ((int)response.StatusCode is >= 200 and < 300)
            {
                string? uuid = null;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var document = JsonDocument.Parse(body);
                    if (document.RootElement.TryGetProperty("uuid", out var uuidProperty))
                    {
                        uuid = uuidProperty.GetString();
                    }
                }

                return new GustoTimeSheetResult(true, uuid, (int)response.StatusCode, null, false);
            }

            return new GustoTimeSheetResult(
                false,
                null,
                (int)response.StatusCode,
                ReadMessage(body),
                false);
        }
    }

    private static string ReadMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Gusto rejected the time sheet.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "Gusto rejected the time sheet.";
            }
        }
        catch (JsonException)
        {
        }

        return body.Length <= 500 ? body : body[..500];
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
