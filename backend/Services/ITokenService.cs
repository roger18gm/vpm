using VisionPaint.Models;

namespace VisionPaint.Services;

public interface ITokenService
{
    AuthTokenPair CreateTokenPair(CurrentUserContext user);
    Guid? ValidateRefreshToken(string refreshToken);
}

public sealed record AuthTokenPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);
