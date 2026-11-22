using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace VisionPaint.Services;

public sealed class GustoTimeClient : IGustoTimeClient
{
    public const string HttpClientName = "Gusto";

    private const int PageSize = 100;
    private const int MaxPages = 20;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GustoOptions _options;
    private readonly GustoAccessTokenCache _cache;
    private readonly IGustoRefreshTokenStore? _refreshTokens;

    public GustoTimeClient(
        IHttpClientFactory httpClientFactory,
        IOptions<GustoOptions> options,
        GustoAccessTokenCache cache,
        IGustoRefreshTokenStore? refreshTokens = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _cache = cache;
        _refreshTokens = refreshTokens;
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

    public async Task<string> CreateFlowAsync(string flowType, CancellationToken cancellationToken)
    {
        var companyUuid = Uri.EscapeDataString(_options.CompanyUuid.Trim());
        var body = await PostDemoJsonAsync(
            $"/v1/companies/{companyUuid}/flows",
            new { flow_type = flowType },
            cancellationToken);
        return ReadFlowUrl(body);
    }

    public async Task<IReadOnlyList<GustoDirectoryEmployee>> ListEmployeesAsync(CancellationToken cancellationToken)
    {
        var employees = new List<GustoDirectoryEmployee>();
        var companyUuid = Uri.EscapeDataString(_options.CompanyUuid.Trim());
        for (var page = 1; page <= MaxPages; page++)
        {
            var body = await GetDemoJsonAsync(
                $"/v1/companies/{companyUuid}/employees?onboarded_active=true&page={page}&per={PageSize}",
                cancellationToken);
            var pageEmployees = ParseEmployees(body);
            foreach (var employee in pageEmployees)
            {
                var jobs = await ListJobsAsync(employee.Uuid, cancellationToken);
                employees.Add(employee with { Jobs = jobs });
            }

            if (pageEmployees.Count < PageSize)
            {
                return employees;
            }
        }

        throw new GustoNotConfiguredException();
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

        await _cache.WaitForRefreshAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cache.TryGet(DateTimeOffset.UtcNow, out cached))
            {
                return cached;
            }

            if (forceRefresh)
            {
                _cache.InvalidateAccessToken();
            }

            return await RequestTokenAsync(cancellationToken);
        }
        finally
        {
            _cache.ReleaseRefresh();
        }
    }

    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var refreshToken = _cache.PeekRefreshToken();
        if (string.IsNullOrWhiteSpace(refreshToken) && _refreshTokens is not null)
        {
            refreshToken = await _refreshTokens.GetAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            refreshToken = _options.RefreshToken;
        }

        var http = _httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(
                $"{GustoOptions.DemoBaseUrl}/oauth/token",
                new
                {
                    client_id = _options.ClientId,
                    client_secret = _options.ClientSecret,
                    refresh_token = refreshToken,
                    grant_type = "refresh_token"
                },
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new GustoNotConfiguredException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GustoNotConfiguredException();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new GustoNotConfiguredException();
            }

            TokenResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            }
            catch (JsonException)
            {
                throw new GustoNotConfiguredException();
            }
            catch (NotSupportedException)
            {
                throw new GustoNotConfiguredException();
            }
            catch (HttpRequestException)
            {
                throw new GustoNotConfiguredException();
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GustoNotConfiguredException();
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                throw new GustoNotConfiguredException();
            }

            var expiresIn = payload.ExpiresIn <= 0 ? 7200 : payload.ExpiresIn;
            _cache.Set(payload.AccessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
            if (!string.IsNullOrWhiteSpace(payload.RefreshToken))
            {
                _cache.RememberRefreshToken(payload.RefreshToken);
                if (_refreshTokens is not null)
                {
                    try
                    {
                        await _refreshTokens.SaveAsync(payload.RefreshToken, cancellationToken);
                    }
                    catch (Exception)
                    {
                        // The replacement stays in memory for this process. Do not log the token.
                    }
                }
            }

            return payload.AccessToken;
        }
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
        catch (HttpRequestException)
        {
            return new GustoTimeSheetResult(false, null, 0, "The Gusto request could not connect.", false);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if ((int)response.StatusCode is >= 200 and < 300)
            {
                string? uuid = null;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(body);
                        if (document.RootElement.ValueKind == JsonValueKind.Object
                            && document.RootElement.TryGetProperty("uuid", out var uuidProperty)
                            && uuidProperty.ValueKind == JsonValueKind.String)
                        {
                            uuid = uuidProperty.GetString();
                        }
                    }
                    catch (JsonException)
                    {
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

    private async Task<IReadOnlyList<GustoDirectoryJob>> ListJobsAsync(
        string employeeUuid,
        CancellationToken cancellationToken)
    {
        var jobs = new List<GustoDirectoryJob>();
        var escaped = Uri.EscapeDataString(employeeUuid);
        for (var page = 1; page <= MaxPages; page++)
        {
            var body = await GetDemoJsonAsync(
                $"/v1/employees/{escaped}/jobs?page={page}&per={PageSize}",
                cancellationToken);
            var pageJobs = ParseJobs(body);
            jobs.AddRange(pageJobs);
            if (pageJobs.Count < PageSize)
            {
                return jobs;
            }
        }

        throw new GustoNotConfiguredException();
    }

    private async Task<string> PostDemoJsonAsync(
        string relativeUrl,
        object payload,
        CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(forceRefresh: false, cancellationToken);
        var (status, body) = await SendJsonAsync(HttpMethod.Post, token, relativeUrl, payload, cancellationToken);
        if (status == (int)System.Net.HttpStatusCode.Unauthorized)
        {
            token = await GetTokenAsync(forceRefresh: true, cancellationToken);
            (status, body) = await SendJsonAsync(HttpMethod.Post, token, relativeUrl, payload, cancellationToken);
        }

        if (status == (int)System.Net.HttpStatusCode.UnprocessableEntity)
        {
            throw new GustoFlowNotReadyException();
        }

        if (status is < 200 or >= 300)
        {
            throw new GustoNotConfiguredException();
        }

        return body;
    }

    private async Task<(int Status, string Body)> SendJsonAsync(
        HttpMethod method,
        string token,
        string relativeUrl,
        object? payload,
        CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(method, $"{_options.BaseUrl.Trim().TrimEnd('/')}{relativeUrl}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.TryAddWithoutValidation("X-Gusto-API-Version", _options.ApiVersion);
        if (payload is not null)
        {
            message.Content = JsonContent.Create(payload);
        }

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException)
        {
            throw new GustoNotConfiguredException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GustoNotConfiguredException();
        }
    }

    private static string ReadFlowUrl(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("url", out var urlValue)
                || urlValue.ValueKind != JsonValueKind.String
                || !Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var url)
                || url.Scheme != Uri.UriSchemeHttps
                || !string.Equals(url.Host, "flows.gusto-demo.com", StringComparison.Ordinal))
            {
                throw new GustoNotConfiguredException();
            }

            return url.AbsoluteUri;
        }
        catch (JsonException)
        {
            throw new GustoNotConfiguredException();
        }
    }

    private async Task<string> GetDemoJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(forceRefresh: false, cancellationToken);
        var (status, body) = await GetJsonAsync(token, relativeUrl, cancellationToken);
        if (status == (int)System.Net.HttpStatusCode.Unauthorized)
        {
            token = await GetTokenAsync(forceRefresh: true, cancellationToken);
            (status, body) = await GetJsonAsync(token, relativeUrl, cancellationToken);
        }

        if (status is < 200 or >= 300)
        {
            throw new GustoNotConfiguredException();
        }

        return body;
    }

    private async Task<(int Status, string Body)> GetJsonAsync(
        string token,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.BaseUrl.Trim().TrimEnd('/')}{relativeUrl}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.TryAddWithoutValidation("X-Gusto-API-Version", _options.ApiVersion);
        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException)
        {
            throw new GustoNotConfiguredException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GustoNotConfiguredException();
        }
    }

    private static List<GustoDirectoryEmployee> ParseEmployees(string json)
    {
        using var document = ParseArray(json);
        var employees = new List<GustoDirectoryEmployee>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var uuid = ReadString(item, "uuid");
            if (string.IsNullOrWhiteSpace(uuid))
            {
                continue;
            }

            employees.Add(new GustoDirectoryEmployee(
                uuid,
                ReadString(item, "first_name"),
                ReadString(item, "last_name"),
                ReadString(item, "email"),
                Array.Empty<GustoDirectoryJob>()));
        }

        return employees;
    }

    private static List<GustoDirectoryJob> ParseJobs(string json)
    {
        using var document = ParseArray(json);
        var jobs = new List<GustoDirectoryJob>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var uuid = ReadString(item, "uuid");
            if (string.IsNullOrWhiteSpace(uuid))
            {
                continue;
            }

            jobs.Add(new GustoDirectoryJob(uuid, ReadString(item, "title"), ReadBool(item, "primary")));
        }

        return jobs;
    }

    private static JsonDocument ParseArray(string json)
    {
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                document.Dispose();
                throw new GustoNotConfiguredException();
            }

            return document;
        }
        catch (JsonException)
        {
            throw new GustoNotConfiguredException();
        }
    }

    private static string ReadString(JsonElement item, string name)
    {
        if (item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? "";
        }

        return "";
    }

    private static bool ReadBool(JsonElement item, string name) =>
        item.TryGetProperty(name, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
        && property.GetBoolean();

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }
}
