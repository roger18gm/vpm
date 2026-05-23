using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class TokenService : ITokenService
{
    public const string RefreshTokenType = "refresh";
    public const string SessionIdClaimType = "session_id";
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException("JWT signing key is not configured.");
        }

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
    }

    public AuthTokenPair CreateTokenPair(CurrentUserContext user, Guid sessionId)
    {
        var accessExpires = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpires = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays);
        var refreshTokenId = Guid.NewGuid();
        var accessToken = CreateJwt(user, accessExpires, sessionId, tokenId: Guid.NewGuid(), tokenType: "access");
        var refreshToken = CreateJwt(user, refreshExpires, sessionId, refreshTokenId, tokenType: RefreshTokenType);
        return new AuthTokenPair(accessToken, refreshToken, accessExpires, sessionId, refreshTokenId, refreshExpires);
    }

    public RefreshTokenValidationResult? ValidateRefreshToken(string refreshToken)
    {
        if (!TryValidateToken(refreshToken, out var principal))
        {
            return null;
        }

        var tokenType = principal.FindFirstValue("token_type");
        if (!string.Equals(tokenType, RefreshTokenType, StringComparison.Ordinal))
        {
            return null;
        }

        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionId = principal.FindFirstValue(SessionIdClaimType);
        var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);

        return Guid.TryParse(sub, out var authUserId)
               && Guid.TryParse(sessionId, out var parsedSessionId)
               && Guid.TryParse(tokenId, out var parsedTokenId)
            ? new RefreshTokenValidationResult(authUserId, parsedSessionId, parsedTokenId)
            : null;
    }

    private string CreateJwt(CurrentUserContext user, DateTimeOffset expires, Guid sessionId, Guid tokenId, string tokenType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.AuthUserId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.PersonName),
            new("person_id", user.PersonId.ToString()),
            new("company_id", user.CompanyId.ToString()),
            new("company_role", user.CompanyRole),
            new(SessionIdClaimType, sessionId.ToString()),
            new("token_type", tokenType),
            new(JwtRegisteredClaimNames.Jti, tokenId.ToString())
        };

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private bool TryValidateToken(string token, out ClaimsPrincipal principal)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
            return true;
        }
        catch (SecurityTokenException)
        {
            principal = new ClaimsPrincipal();
            return false;
        }
    }
}
