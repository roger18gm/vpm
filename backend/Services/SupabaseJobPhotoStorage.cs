using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace VisionPaint.Services;

public sealed class SupabaseJobPhotoStorage : IJobPhotoStorage
{
    private readonly HttpClient _httpClient;
    private readonly JobPhotoStorageOptions _options;

    public SupabaseJobPhotoStorage(HttpClient httpClient, IOptions<JobPhotoStorageOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> SaveAsync(
        int companyId,
        int jobId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var storagePath = $"{companyId}/jobs/{jobId}/{Guid.NewGuid():N}{extension}";
        var url = $"{_options.Url.TrimEnd('/')}/storage/v1/object/{_options.Bucket}/{storagePath}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Supabase upload failed: {(int)response.StatusCode} {body}");
        }

        return storagePath;
    }

    public async Task<string> GetReadUrlAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var url = $"{_options.Url.TrimEnd('/')}/storage/v1/object/sign/{_options.Bucket}/{storagePath}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { expiresIn = 3600 }),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("signedURL", out var signedUrl))
        {
            var path = signedUrl.GetString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : $"{_options.Url.TrimEnd('/')}{path}";
            }
        }

        throw new InvalidOperationException("Supabase sign response did not include signedURL.");
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.ServiceRoleKey))
        {
            throw new InvalidOperationException("Supabase storage is not configured.");
        }
    }
}
