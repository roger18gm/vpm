namespace VisionPaint.Services;

public sealed class GustoAccessTokenCache
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private string? _token;
    private string? _refreshToken;
    private DateTimeOffset _expiresAt;

    public Task WaitForRefreshAsync(CancellationToken cancellationToken) =>
        _refresh.WaitAsync(cancellationToken);

    public void ReleaseRefresh() => _refresh.Release();

    public bool TryGet(DateTimeOffset now, out string token)
    {
        lock (_gate)
        {
            if (_token is null || now >= _expiresAt.AddMinutes(-5))
            {
                token = "";
                return false;
            }

            token = _token;
            return true;
        }
    }

    public void Set(string token, DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            _token = token;
            _expiresAt = expiresAt;
        }
    }

    public void InvalidateAccessToken()
    {
        lock (_gate)
        {
            _token = null;
            _expiresAt = default;
        }
    }

    public string? PeekRefreshToken()
    {
        lock (_gate)
        {
            return string.IsNullOrWhiteSpace(_refreshToken) ? null : _refreshToken;
        }
    }

    public void RememberRefreshToken(string refreshToken)
    {
        lock (_gate)
        {
            _refreshToken = refreshToken;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _token = null;
            _refreshToken = null;
            _expiresAt = default;
        }
    }
}
