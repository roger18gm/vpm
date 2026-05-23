using VisionPaint.Models;

namespace VisionPaint.Services;

public interface ITokenService
{
    AuthTokenPair CreateTokenPair(CurrentUserContext user, Guid sessionId);
    RefreshTokenValidationResult? ValidateRefreshToken(string refreshToken);
}

public sealed record AuthTokenPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    Guid SessionId,
    Guid RefreshTokenId,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record RefreshTokenValidationResult(
    Guid AuthUserId,
    Guid SessionId,
    Guid TokenId);
