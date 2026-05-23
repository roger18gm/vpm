using System.Net.Http.Headers;
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

    public async Task<AuthTokenResponse> BootstrapAsync(BootstrapRequest request, CancellationToken cancellationToken = default)
    {
        return await SendAsync<AuthTokenResponse>("/api/auth/bootstrap", request, cancellationToken);
    }

    public async Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        return await SendAsync<AuthTokenResponse>("/api/auth/login", request, cancellationToken);
    }

    public async Task<AuthTokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        return await SendAsync<AuthTokenResponse>("/api/auth/refresh", new RefreshTokenRequest(refreshToken), cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _client.PostAsync("/api/auth/logout", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        SetBearerToken(null);
    }

    public void SetBearerToken(string? accessToken)
    {
        _client.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var tokens = await LoginAsync(request, cancellationToken);
        SetBearerToken(tokens.AccessToken);
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
