namespace VisionPaint.Services;

public sealed class GustoAccessTokenCache
{
    private readonly object _gate = new();
    private string? _token;
    private DateTimeOffset _expiresAt;

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

    public void Clear()
    {
        lock (_gate)
        {
            _token = null;
            _expiresAt = default;
        }
    }
}
