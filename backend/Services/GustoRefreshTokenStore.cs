using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public interface IGustoRefreshTokenStore
{
    Task<string?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(string refreshToken, CancellationToken cancellationToken);
}

public sealed class GustoRefreshTokenStore : IGustoRefreshTokenStore
{
    private readonly AppDbContext _db;

    public GustoRefreshTokenStore(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        var row = await _db.GustoRefreshTokens
            .SingleOrDefaultAsync(token => token.Id == 1, cancellationToken);
        return string.IsNullOrWhiteSpace(row?.RefreshToken) ? null : row.RefreshToken;
    }

    public async Task SaveAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var row = await _db.GustoRefreshTokens
            .SingleOrDefaultAsync(token => token.Id == 1, cancellationToken);
        if (row is null)
        {
            _db.GustoRefreshTokens.Add(new GustoRefreshToken
            {
                Id = 1,
                RefreshToken = refreshToken,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else if (!string.Equals(row.RefreshToken, refreshToken, StringComparison.Ordinal))
        {
            row.RefreshToken = refreshToken;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
