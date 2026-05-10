using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly IAntiforgery _antiforgery;
    private readonly IPasswordHasher<AuthUser> _passwordHasher;

    public AuthController(AppDbContext db, ICurrentUserService currentUserService, IAntiforgery antiforgery, IPasswordHasher<AuthUser> passwordHasher)
    {
        _db = db;
        _currentUserService = currentUserService;
        _antiforgery = antiforgery;
        _passwordHasher = passwordHasher;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        var canBootstrap = !await _db.AuthUsers.AnyAsync(cancellationToken);
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new AuthStatusResponse(
            currentUser is not null,
            currentUser is null && canBootstrap,
            currentUser is null
                ? null
                : new AuthenticatedUserResponse(
                    currentUser.AuthUserId,
                    currentUser.PersonId,
                    currentUser.CompanyId,
                    currentUser.CompanyRole,
                    currentUser.PersonName,
                    currentUser.Email,
                    tokens.RequestToken ?? string.Empty),
            tokens.RequestToken ?? string.Empty));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthenticatedUserResponse>> Me(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new AuthenticatedUserResponse(
            currentUser.AuthUserId,
            currentUser.PersonId,
            currentUser.CompanyId,
            currentUser.CompanyRole,
            currentUser.PersonName,
            currentUser.Email,
            tokens.RequestToken ?? string.Empty));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<ActionResult<AuthenticatedUserResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
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

        await SignInAsync(authUser, currentUser);
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new AuthenticatedUserResponse(
            currentUser.AuthUserId,
            currentUser.PersonId,
            currentUser.CompanyId,
            currentUser.CompanyRole,
            currentUser.PersonName,
            currentUser.Email,
            tokens.RequestToken ?? string.Empty));
    }

    [HttpPost("logout")]
    [Authorize]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpPost("bootstrap")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<ActionResult<AuthenticatedUserResponse>> Bootstrap([FromBody] BootstrapRequest request, CancellationToken cancellationToken)
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
        await SignInAsync(authUser, currentUser);
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new AuthenticatedUserResponse(
            currentUser.AuthUserId,
            currentUser.PersonId,
            currentUser.CompanyId,
            currentUser.CompanyRole,
            currentUser.PersonName,
            currentUser.Email,
            tokens.RequestToken ?? string.Empty));
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
            .FirstAsync(user => user.Id == authUserId && user.IsActive, cancellationToken);

        return new CurrentUserContext(
            authUser.Id,
            person.Id,
            membership.CompanyId,
            membership.Role,
            person.Name,
            authUser.Email);
    }

    private async Task SignInAsync(AuthUser authUser, CurrentUserContext currentUser)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, authUser.Id.ToString()),
            new(ClaimTypes.Email, currentUser.Email),
            new(ClaimTypes.Name, currentUser.PersonName),
            new("person_id", currentUser.PersonId.ToString()),
            new("company_id", currentUser.CompanyId.ToString()),
            new("company_role", currentUser.CompanyRole)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14)
            });
    }
}
