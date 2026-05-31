using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class UsersController : ControllerBase
{
    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "manager",
        "crew"
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyAuthorizationService _companyAuthorization;
    private readonly IPasswordHasher<AuthUser> _passwordHasher;

    public UsersController(
        AppDbContext db,
        ICurrentUserService currentUserService,
        ICompanyAuthorizationService companyAuthorization,
        IPasswordHasher<AuthUser> passwordHasher)
    {
        _db = db;
        _currentUserService = currentUserService;
        _companyAuthorization = companyAuthorization;
        _passwordHasher = passwordHasher;
    }

    private async Task<(CurrentUserContext? User, ActionResult? Failure)> GetAdminOrFailureAsync(
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return (null, Unauthorized());
        }

        if (!_companyAuthorization.IsAdmin(currentUser))
        {
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { message = "Only owners and admins can manage users." }));
        }

        return (currentUser, null);
    }

    [HttpGet]
    public async Task<ActionResult<List<UserAdminDto>>> GetUsers(CancellationToken cancellationToken)
    {
        var (admin, failure) = await GetAdminOrFailureAsync(cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var rows = await (
            from member in _db.CompanyMembers.AsNoTracking()
            join person in _db.People.AsNoTracking() on member.PersonId equals person.Id
            join auth in _db.AuthUsers.AsNoTracking() on person.AuthUserId equals auth.Id
            where member.CompanyId == admin!.CompanyId && person.AuthUserId != null
            orderby person.Name
            select new UserRow(
                person.Id,
                auth.Id,
                person.Name,
                auth.Email,
                member.Role,
                member.Status,
                person.IsActive,
                auth.LastLoginAt)
        ).ToListAsync(cancellationToken);

        var openByPerson = await LoadOpenEntriesAsync(rows.Select(row => row.PersonId).ToList(), cancellationToken);
        var jobTitles = await LoadJobTitlesAsync(openByPerson.Values.Select(entry => entry.JobId).Distinct().ToList(), cancellationToken);

        return Ok(rows.Select(row => ToDto(row, openByPerson, jobTitles)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<UserAdminDto>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var (admin, failure) = await GetAdminOrFailureAsync(cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var role = NormalizeRole(request.CompanyRole);
        if (!IsValidRole(role))
        {
            return BadRequest(new { message = "Invalid company role." });
        }

        if (IsOwnerRole(role) && !IsOwnerRole(admin!.CompanyRole))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only owners can assign the owner role." });
        }

        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Name, email, and password are required." });
        }

        if (request.Password.Length < 8)
        {
            return BadRequest(new { message = "Password must be at least 8 characters." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _db.AuthUsers.AnyAsync(user => user.Email == normalizedEmail, cancellationToken))
        {
            return Conflict(new { message = "A user with this email already exists." });
        }

        var now = DateTimeOffset.UtcNow;
        var authUser = new AuthUser
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = string.Empty,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        authUser.PasswordHash = _passwordHasher.HashPassword(authUser, request.Password);

        var person = new Person
        {
            AuthUserId = authUser.Id,
            Name = request.Name.Trim(),
            Email = normalizedEmail,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.AuthUsers.Add(authUser);
        _db.People.Add(person);
        await _db.SaveChangesAsync(cancellationToken);

        _db.CompanyMembers.Add(new CompanyMember
        {
            CompanyId = admin!.CompanyId,
            PersonId = person.Id,
            Role = role,
            Status = "active",
            JoinedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetUsers),
            new UserAdminDto(
                person.Id,
                authUser.Id,
                person.Name,
                authUser.Email,
                role,
                "active",
                person.IsActive,
                false,
                null,
                authUser.LastLoginAt));
    }

    [HttpPatch("{personId:int}")]
    public async Task<ActionResult<UserAdminDto>> UpdateRole(
        int personId,
        [FromBody] UpdateUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var (admin, failure) = await GetAdminOrFailureAsync(cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (personId == admin!.PersonId)
        {
            return Conflict(new { message = "You cannot change your own role." });
        }

        var role = NormalizeRole(request.CompanyRole);
        if (!IsValidRole(role))
        {
            return BadRequest(new { message = "Invalid company role." });
        }

        if (IsOwnerRole(role) && !IsOwnerRole(admin!.CompanyRole))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only owners can assign the owner role." });
        }

        var member = await _db.CompanyMembers
            .FirstOrDefaultAsync(
                m => m.PersonId == personId && m.CompanyId == admin!.CompanyId,
                cancellationToken);

        if (member is null)
        {
            return NotFound();
        }

        var person = await _db.People.FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person?.AuthUserId is null)
        {
            return NotFound();
        }

        var authUser = await _db.AuthUsers.AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == person.AuthUserId, cancellationToken);
        if (authUser is null)
        {
            return NotFound();
        }

        if (IsOwnerRole(member.Role) && !IsOwnerRole(role))
        {
            var ownerCount = await _db.CompanyMembers.CountAsync(
                m => m.CompanyId == admin!.CompanyId
                    && m.Status == "active"
                    && m.Role == "owner",
                cancellationToken);

            if (ownerCount <= 1)
            {
                return Conflict(new { message = "Cannot demote the last owner." });
            }
        }

        member.Role = role;
        await _db.SaveChangesAsync(cancellationToken);

        var openByPerson = await LoadOpenEntriesAsync([personId], cancellationToken);
        openByPerson.TryGetValue(personId, out var openEntry);
        string? clockedInJobTitle = null;
        if (openEntry is not null)
        {
            var titles = await LoadJobTitlesAsync([openEntry.JobId], cancellationToken);
            titles.TryGetValue(openEntry.JobId, out clockedInJobTitle);
        }

        return Ok(new UserAdminDto(
            person.Id,
            authUser.Id,
            person.Name,
            authUser.Email,
            member.Role,
            member.Status,
            person.IsActive,
            openEntry is not null,
            clockedInJobTitle,
            authUser.LastLoginAt));
    }

    private async Task<Dictionary<int, TimeEntry>> LoadOpenEntriesAsync(
        List<int> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
        {
            return new Dictionary<int, TimeEntry>();
        }

        var openEntries = await _db.TimeEntries
            .AsNoTracking()
            .Where(entry => personIds.Contains(entry.PersonId) && entry.ClockOutAt == null)
            .ToListAsync(cancellationToken);

        return openEntries
            .GroupBy(entry => entry.PersonId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(e => e.ClockInAt).First());
    }

    private async Task<Dictionary<int, string>> LoadJobTitlesAsync(
        List<int> jobIds,
        CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        return await _db.Jobs
            .AsNoTracking()
            .Where(job => jobIds.Contains(job.Id))
            .ToDictionaryAsync(job => job.Id, job => job.Title, cancellationToken);
    }

    private static UserAdminDto ToDto(
        UserRow row,
        IReadOnlyDictionary<int, TimeEntry> openByPerson,
        IReadOnlyDictionary<int, string> jobTitles)
    {
        openByPerson.TryGetValue(row.PersonId, out var openEntry);
        string? clockedInJobTitle = null;
        if (openEntry is not null && jobTitles.TryGetValue(openEntry.JobId, out var title))
        {
            clockedInJobTitle = title;
        }

        return new UserAdminDto(
            row.PersonId,
            row.AuthUserId,
            row.Name,
            row.Email,
            row.Role,
            row.MembershipStatus,
            row.IsActive,
            openEntry is not null,
            clockedInJobTitle,
            row.LastLoginAt);
    }

    private static string NormalizeRole(string? role) =>
        string.IsNullOrWhiteSpace(role) ? "crew" : role.Trim().ToLowerInvariant();

    private static bool IsValidRole(string role) => ValidRoles.Contains(role);

    private static bool IsOwnerRole(string role) =>
        string.Equals(role, "owner", StringComparison.OrdinalIgnoreCase);

    private sealed record UserRow(
        int PersonId,
        Guid AuthUserId,
        string Name,
        string Email,
        string Role,
        string MembershipStatus,
        bool IsActive,
        DateTimeOffset? LastLoginAt);
}
