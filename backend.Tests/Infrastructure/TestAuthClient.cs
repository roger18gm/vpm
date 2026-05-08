using System.Net.Http.Json;
using VisionPaint.Models;

namespace VisionPaint.Tests.Infrastructure;

public sealed class TestAuthClient
{
    private readonly HttpClient _client;

    public TestAuthClient(HttpClient client)
    {
        _client = client;
    }

    public async Task<AuthStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _client.GetFromJsonAsync<AuthStatusResponse>("/api/auth/status", cancellationToken);
        if (status is null)
        {
            throw new InvalidOperationException("Auth status response was empty.");
        }

        return status;
    }

    public async Task<AuthenticatedUserResponse> BootstrapAsync(BootstrapRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureCsrfTokenAsync(cancellationToken);
        return await SendAsync<AuthenticatedUserResponse>("/api/auth/bootstrap", request, cancellationToken);
    }

    public async Task<AuthenticatedUserResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureCsrfTokenAsync(cancellationToken);
        return await SendAsync<AuthenticatedUserResponse>("/api/auth/login", request, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCsrfTokenAsync(cancellationToken);
        using var response = await _client.PostAsync("/api/auth/logout", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task RefreshCsrfTokenAsync(CancellationToken cancellationToken = default)
    {
        return EnsureCsrfTokenAsync(cancellationToken);
    }

    private async Task EnsureCsrfTokenAsync(CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(status.CsrfToken))
        {
            _client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            _client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", status.CsrfToken);
        }
    }

    private async Task<T> SendAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync(path, body, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException($"Response from {path} was empty.");
        }

        return payload;
    }
}
