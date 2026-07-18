using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPasswordHasher<AuthUser> _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly PasswordResetService _passwordReset;

    public AuthController(
        AppDbContext db,
        ICurrentUserService currentUserService,
        IPasswordHasher<AuthUser> passwordHasher,
        ITokenService tokenService,
        PasswordResetService passwordReset)
    {
        _db = db;
        _currentUserService = currentUserService;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _passwordReset = passwordReset;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        var canBootstrap = !await _db.AuthUsers.AnyAsync(cancellationToken);

        return Ok(new AuthStatusResponse(
            currentUser is not null,
            currentUser is null && canBootstrap,
            currentUser is null ? null : ToUserResponse(currentUser)));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthenticatedUserResponse>> Me(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        return currentUser is null ? Unauthorized() : Ok(ToUserResponse(currentUser));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthTokenResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var authUser = await _db.AuthUsers.FirstOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);

        if (authUser is null || !authUser.IsActive)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var verification = _passwordHasher.VerifyHashedPassword(authUser, authUser.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var currentUser = await LoadCurrentUserAsync(authUser.Id, cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized(new { message = "This account is not linked to an active company membership." });
        }

        authUser.LastLoginAt = DateTimeOffset.UtcNow;
        authUser.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(await CreateTokenResponseAsync(currentUser, Guid.NewGuid(), cancellationToken));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _passwordReset.ForgotPasswordAsync(request.Email, cancellationToken);
        return Ok(new { message = "If an account exists for that email, we sent reset instructions." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var (ok, error) = await _passwordReset.ResetPasswordAsync(
            request.Token,
            request.NewPassword,
            cancellationToken);
        if (!ok)
        {
            return BadRequest(new { message = error });
        }

        return Ok(new { message = "Password updated. You can sign in." });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthTokenResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var authUserId = _tokenService.ValidateRefreshToken(request.RefreshToken);
        if (authUserId is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        var storedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(token =>
                token.AuthUserId == authUserId.AuthUserId
                && token.SessionId == authUserId.SessionId
                && token.TokenId == authUserId.TokenId,
                cancellationToken);

        if (storedToken is null || storedToken.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        if (storedToken.RevokedAt is not null || storedToken.ReplacedByTokenId is not null)
        {
            await RevokeRefreshTokenSessionAsync(
                authUserId.AuthUserId,
                authUserId.SessionId,
                "refresh_token_reuse",
                cancellationToken);
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        var currentUser = await LoadCurrentUserAsync(authUserId.AuthUserId, cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized(new { message = "This account is not linked to an active company membership." });
        }

        return Ok(await RotateRefreshTokenAsync(currentUser, storedToken, cancellationToken));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var authUserIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionIdText = User.FindFirstValue(TokenService.SessionIdClaimType);

        if (Guid.TryParse(authUserIdText, out var authUserId) && Guid.TryParse(sessionIdText, out var sessionId))
        {
            await RevokeRefreshTokenSessionAsync(authUserId, sessionId, "logout", cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("bootstrap")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthTokenResponse>> Bootstrap([FromBody] BootstrapRequest request, CancellationToken cancellationToken)
    {
        if (await _db.AuthUsers.AnyAsync(cancellationToken))
        {
            return Conflict(new { message = "An auth user already exists." });
        }

        var company = await _db.Companies.AsNoTracking().OrderBy(company => company.Id).FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return Conflict(new { message = "No company exists yet." });
        }

        var authUser = new AuthUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = string.Empty,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        authUser.PasswordHash = _passwordHasher.HashPassword(authUser, request.Password);

        var person = new Person
        {
            AuthUserId = authUser.Id,
            Name = request.Name.Trim(),
            Email = authUser.Email,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.AuthUsers.Add(authUser);
        _db.People.Add(person);
        await _db.SaveChangesAsync(cancellationToken);

        _db.CompanyMembers.Add(new CompanyMember
        {
            CompanyId = company.Id,
            PersonId = person.Id,
            Role = "owner",
            Status = "active",
            JoinedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var currentUser = new CurrentUserContext(authUser.Id, person.Id, company.Id, "owner", person.Name, authUser.Email);
        return Ok(await CreateTokenResponseAsync(currentUser, Guid.NewGuid(), cancellationToken));
    }

    private async Task<AuthTokenResponse> CreateTokenResponseAsync(
        CurrentUserContext currentUser,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var tokens = _tokenService.CreateTokenPair(currentUser, sessionId);
        _db.RefreshTokens.Add(new RefreshToken
        {
            AuthUserId = currentUser.AuthUserId,
            SessionId = tokens.SessionId,
            TokenId = tokens.RefreshTokenId,
            ExpiresAt = tokens.RefreshTokenExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return new AuthTokenResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            ToUserResponse(currentUser));
    }

    private async Task<AuthTokenResponse> RotateRefreshTokenAsync(
        CurrentUserContext currentUser,
        RefreshToken replacedToken,
        CancellationToken cancellationToken)
    {
        var tokens = _tokenService.CreateTokenPair(currentUser, replacedToken.SessionId);
        var now = DateTimeOffset.UtcNow;

        replacedToken.RevokedAt = now;
        replacedToken.RevokeReason = "rotated";
        replacedToken.ReplacedByTokenId = tokens.RefreshTokenId;

        _db.RefreshTokens.Add(new RefreshToken
        {
            AuthUserId = currentUser.AuthUserId,
            SessionId = tokens.SessionId,
            TokenId = tokens.RefreshTokenId,
            ExpiresAt = tokens.RefreshTokenExpiresAt,
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new AuthTokenResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            ToUserResponse(currentUser));
    }

    private static AuthenticatedUserResponse ToUserResponse(CurrentUserContext currentUser)
    {
        return new AuthenticatedUserResponse(
            currentUser.AuthUserId,
            currentUser.PersonId,
            currentUser.CompanyId,
            currentUser.CompanyRole,
            currentUser.PersonName,
            currentUser.Email);
    }

    private async Task<CurrentUserContext?> LoadCurrentUserAsync(Guid authUserId, CancellationToken cancellationToken)
    {
        var person = await _db.People
            .AsNoTracking()
            .FirstOrDefaultAsync(person => person.AuthUserId == authUserId && person.IsActive, cancellationToken);

        if (person is null)
        {
            return null;
        }

        var membership = await _db.CompanyMembers
            .AsNoTracking()
            .Where(member => member.PersonId == person.Id && member.Status == "active")
            .OrderByDescending(member => member.Role == "owner")
            .ThenByDescending(member => member.Role == "admin")
            .ThenByDescending(member => member.Role == "manager")
            .ThenByDescending(member => member.Role == "crew")
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return null;
        }

        var authUser = await _db.AuthUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == authUserId && user.IsActive, cancellationToken);

        if (authUser is null)
        {
            return null;
        }

        return new CurrentUserContext(
            authUser.Id,
            person.Id,
            membership.CompanyId,
            membership.Role,
            person.Name,
            authUser.Email);
    }

    private async Task RevokeRefreshTokenSessionAsync(
        Guid authUserId,
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var tokens = await _db.RefreshTokens
            .Where(token => token.AuthUserId == authUserId && token.SessionId == sessionId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokeReason = reason;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
