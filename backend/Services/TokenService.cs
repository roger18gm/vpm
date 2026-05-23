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

    public AuthTokenPair CreateTokenPair(CurrentUserContext user)
    {
        var accessExpires = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var accessToken = CreateJwt(user, accessExpires, tokenType: "access");
        var refreshToken = CreateJwt(user, DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays), tokenType: RefreshTokenType);
        return new AuthTokenPair(accessToken, refreshToken, accessExpires);
    }

    public Guid? ValidateRefreshToken(string refreshToken)
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
        return Guid.TryParse(sub, out var authUserId) ? authUserId : null;
    }

    private string CreateJwt(CurrentUserContext user, DateTimeOffset expires, string tokenType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.AuthUserId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.PersonName),
            new("person_id", user.PersonId.ToString()),
            new("company_id", user.CompanyId.ToString()),
            new("company_role", user.CompanyRole),
            new("token_type", tokenType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
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
