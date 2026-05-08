using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;

namespace VisionPaint.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<CurrentUserContext?> GetAsync(CancellationToken cancellationToken = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var authUserIdText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(authUserIdText, out var authUserId))
        {
            return null;
        }

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
}
