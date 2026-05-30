using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs/{jobId:int}/assignments")]
public sealed class JobAssignmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyAuthorizationService _companyAuthorization;
    private readonly IJobAccessService _jobAccess;

    public JobAssignmentsController(
        AppDbContext db,
        ICurrentUserService currentUserService,
        ICompanyAuthorizationService companyAuthorization,
        IJobAccessService jobAccess)
    {
        _db = db;
        _currentUserService = currentUserService;
        _companyAuthorization = companyAuthorization;
        _jobAccess = jobAccess;
    }

    [HttpGet]
    public async Task<ActionResult<JobAssignmentsResponse>> GetAssignments(int jobId, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!await _jobAccess.CanViewJobAsync(jobId, currentUser, cancellationToken))
        {
            return NotFound();
        }

        var assignments = await JobAssignmentHelper.LoadActiveAssignmentsAsync(_db, jobId, cancellationToken);
        return Ok(new JobAssignmentsResponse(jobId, assignments));
    }

    [HttpPut]
    public async Task<ActionResult<JobAssignmentsResponse>> ReplaceAssignments(
        int jobId,
        [FromBody] ReplaceJobAssignmentsRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can assign crew." });
        }

        var job = await _jobAccess.GetCompanyJobAsync(jobId, currentUser, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var role = string.IsNullOrWhiteSpace(request.AssignmentRole) ? "crew" : request.AssignmentRole.Trim().ToLowerInvariant();
        if (role is not ("lead" or "crew" or "prep" or "supervisor"))
        {
            return BadRequest(new { message = "Invalid assignment role." });
        }

        var personIds = (request.PersonIds ?? Array.Empty<int>()).Distinct().ToList();
        if (personIds.Count > 0)
        {
            var validCount = await _db.CompanyMembers.CountAsync(
                member => member.CompanyId == currentUser.CompanyId
                    && member.Status == "active"
                    && personIds.Contains(member.PersonId),
                cancellationToken);

            if (validCount != personIds.Count)
            {
                return BadRequest(new { message = "One or more people are not active company members." });
            }
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.JobAssignments
            .Where(assignment => assignment.JobId == jobId)
            .ToListAsync(cancellationToken);

        var requested = personIds.ToHashSet();

        foreach (var assignment in existing)
        {
            if (requested.Contains(assignment.PersonId))
            {
                assignment.UnassignedAt = null;
                assignment.AssignmentRole = role;
            }
            else if (assignment.UnassignedAt is null)
            {
                assignment.UnassignedAt = now;
            }
        }

        foreach (var personId in requested)
        {
            if (existing.Any(assignment => assignment.PersonId == personId))
            {
                continue;
            }

            _db.JobAssignments.Add(new JobAssignment
            {
                JobId = jobId,
                PersonId = personId,
                AssignmentRole = role,
                AssignedAt = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var assignments = await JobAssignmentHelper.LoadActiveAssignmentsAsync(_db, jobId, cancellationToken);
        return Ok(new JobAssignmentsResponse(jobId, assignments));
    }

    [HttpDelete("{personId:int}")]
    public async Task<IActionResult> Unassign(int jobId, int personId, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can unassign crew." });
        }

        var job = await _jobAccess.GetCompanyJobAsync(jobId, currentUser, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var assignment = await _db.JobAssignments
            .FirstOrDefaultAsync(a => a.JobId == jobId && a.PersonId == personId, cancellationToken);

        if (assignment is null || assignment.UnassignedAt is not null)
        {
            return NotFound();
        }

        assignment.UnassignedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
