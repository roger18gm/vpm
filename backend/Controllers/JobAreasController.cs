using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs/{jobId:int}/areas")]
public sealed class JobAreasController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "not_started",
        "in_progress",
        "completed",
        "blocked"
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyAuthorizationService _companyAuthorization;
    private readonly IJobAccessService _jobAccess;

    public JobAreasController(
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
    public async Task<ActionResult<List<JobAreaDto>>> List(int jobId, CancellationToken cancellationToken)
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

        var areas = await _db.JobAreas
            .AsNoTracking()
            .Where(area => area.JobId == jobId)
            .OrderBy(area => area.SortOrder)
            .ThenBy(area => area.Id)
            .ToListAsync(cancellationToken);

        return Ok(areas.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<JobAreaDto>> Create(
        int jobId,
        [FromBody] CreateJobAreaRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can manage job areas." });
        }

        var job = await _jobAccess.GetCompanyJobAsync(jobId, currentUser, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        if (string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Cannot update areas on a cancelled job." });
        }

        var name = NormalizeName(request.Name, out var nameError);
        if (name is null)
        {
            return BadRequest(new { message = nameError });
        }

        if (await NameTakenAsync(jobId, name, exceptAreaId: null, cancellationToken))
        {
            return BadRequest(new { message = "An area with that name already exists on this job." });
        }

        var maxSort = await _db.JobAreas
            .Where(area => area.JobId == jobId)
            .Select(area => (int?)area.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var area = new JobArea
        {
            JobId = jobId,
            ParentJobAreaId = null,
            Name = name,
            Status = "not_started",
            SortOrder = maxSort + 1
        };

        _db.JobAreas.Add(area);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), new { jobId }, ToDto(area));
    }

    [HttpPatch("{areaId:int}")]
    public async Task<ActionResult<JobAreaDto>> Update(
        int jobId,
        int areaId,
        [FromBody] UpdateJobAreaRequest request,
        CancellationToken cancellationToken)
    {
        var (area, error) = await LoadMutableAreaAsync(jobId, areaId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (area is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(new { message = "Provide a name and/or status." });
        }

        if (request.Name is not null)
        {
            var name = NormalizeName(request.Name, out var nameError);
            if (name is null)
            {
                return BadRequest(new { message = nameError });
            }

            if (await NameTakenAsync(jobId, name, area.Id, cancellationToken))
            {
                return BadRequest(new { message = "An area with that name already exists on this job." });
            }

            area.Name = name;
        }

        if (request.Status is not null)
        {
            var status = request.Status.Trim().ToLowerInvariant();
            if (!AllowedStatuses.Contains(status))
            {
                return BadRequest(new { message = "Invalid area status." });
            }

            ApplyStatusTimestamps(area, status, DateTimeOffset.UtcNow);
            area.Status = status;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(area));
    }

    [HttpDelete("{areaId:int}")]
    public async Task<IActionResult> Delete(int jobId, int areaId, CancellationToken cancellationToken)
    {
        var (area, error) = await LoadMutableAreaAsync(jobId, areaId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (area is null)
        {
            return NotFound();
        }

        _db.JobAreas.Remove(area);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(JobArea? Area, ActionResult? Error)> LoadMutableAreaAsync(
        int jobId,
        int areaId,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return (null, Unauthorized());
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can manage job areas." }));
        }

        var job = await _jobAccess.GetCompanyJobAsync(jobId, currentUser, cancellationToken);
        if (job is null)
        {
            return (null, NotFound());
        }

        if (string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return (null, BadRequest(new { message = "Cannot update areas on a cancelled job." }));
        }

        var area = await _db.JobAreas.FirstOrDefaultAsync(
            row => row.Id == areaId && row.JobId == jobId,
            cancellationToken);
        if (area is null)
        {
            return (null, NotFound());
        }

        return (area, null);
    }

    private static void ApplyStatusTimestamps(JobArea area, string status, DateTimeOffset now)
    {
        if (status == "in_progress" && area.StartedAt is null)
        {
            area.StartedAt = now;
        }

        if (status == "completed")
        {
            area.CompletedAt = now;
        }
        else if (string.Equals(area.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            area.CompletedAt = null;
        }
    }

    private static JobAreaDto ToDto(JobArea area) =>
        new(area.Id, area.JobId, area.Name, area.Status, area.SortOrder, area.StartedAt, area.CompletedAt);

    private static string? NormalizeName(string? value, out string error)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            error = "Name is required.";
            return null;
        }

        if (trimmed.Length > 80)
        {
            error = "Name must be 80 characters or fewer.";
            return null;
        }

        error = string.Empty;
        return trimmed;
    }

    private Task<bool> NameTakenAsync(int jobId, string name, int? exceptAreaId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();
        return _db.JobAreas.AnyAsync(
            area => area.JobId == jobId
                && (exceptAreaId == null || area.Id != exceptAreaId)
                && area.Name.ToLower() == lowered,
            cancellationToken);
    }
}
