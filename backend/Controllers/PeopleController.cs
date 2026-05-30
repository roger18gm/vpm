using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class PeopleController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyAuthorizationService _companyAuthorization;

    public PeopleController(
        AppDbContext db,
        ICurrentUserService currentUserService,
        ICompanyAuthorizationService companyAuthorization)
    {
        _db = db;
        _currentUserService = currentUserService;
        _companyAuthorization = companyAuthorization;
    }

    [HttpGet]
    public async Task<ActionResult<List<PersonSummaryDto>>> GetPeople(
        [FromQuery] string? role,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can list company people." });
        }

        var query = from member in _db.CompanyMembers.AsNoTracking()
                    join person in _db.People.AsNoTracking() on member.PersonId equals person.Id
                    where member.CompanyId == currentUser.CompanyId
                        && member.Status == "active"
                        && person.IsActive
                    select new { member, person };

        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedRole = role.Trim().ToLowerInvariant();
            query = query.Where(x => x.member.Role == normalizedRole);
        }

        var people = await query
            .OrderBy(x => x.person.Name)
            .Select(x => new PersonSummaryDto(
                x.person.Id,
                x.person.Name,
                x.person.Email,
                x.member.Role))
            .ToListAsync(cancellationToken);

        return Ok(people);
    }
}
