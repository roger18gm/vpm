using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class JobsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyAuthorizationService _companyAuthorization;
    private readonly IJobAccessService _jobAccess;

    public JobsController(
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
    public async Task<ActionResult<List<JobListItemResponse>>> GetJobs(
        [FromQuery] bool includeCancelled,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (includeCancelled && !_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can include cancelled jobs." });
        }

        var jobs = await _jobAccess
            .FilterJobsForUser(_db.Jobs.AsNoTracking(), currentUser, includeCancelled)
            .OrderByDescending(job => job.UpdatedAt)
            .ThenByDescending(job => job.Id)
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(job => job.Id).ToList();
        var photoCounts = jobIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.JobPhotos
                .AsNoTracking()
                .Where(photo => jobIds.Contains(photo.JobId))
                .GroupBy(photo => photo.JobId)
                .Select(group => new { JobId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.JobId, item => item.Count, cancellationToken);

        var result = jobs
            .Select(job => JobAssignmentHelper.ToListItemResponse(job, photoCounts.GetValueOrDefault(job.Id, 0)))
            .ToList();

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<JobDetailResponse>> GetJob(int id, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!await _jobAccess.CanViewJobAsync(id, currentUser, cancellationToken))
        {
            return NotFound();
        }

        var job = await _jobAccess.GetCompanyJobAsync(id, currentUser, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var assignments = await JobAssignmentHelper.LoadActiveAssignmentsAsync(_db, id, cancellationToken);
        return Ok(JobAssignmentHelper.ToDetailResponse(job, assignments));
    }

    [HttpPost]
    public async Task<ActionResult<Job>> CreateJob([FromBody] Job request, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can create jobs." });
        }

        var now = DateTimeOffset.UtcNow;
        var status = NormalizeStatus(request.Status);
        var priority = NormalizePriority(request.Priority);

        if (!IsValidStatus(status))
        {
            return BadRequest(new { message = "Invalid job status." });
        }

        if (!IsValidPriority(priority))
        {
            return BadRequest(new { message = "Invalid job priority." });
        }

        var job = new Job
        {
            CompanyId = currentUser.CompanyId,
            ClientId = request.ClientId,
            CreatedByPersonId = currentUser.PersonId,
            Title = request.Title.Trim(),
            Description = request.Description,
            Status = status,
            Priority = priority,
            AddressLine1 = request.AddressLine1,
            AddressLine2 = request.AddressLine2,
            City = request.City,
            StateRegion = request.StateRegion,
            PostalCode = request.PostalCode,
            CountryCode = request.CountryCode,
            ScheduledStartAt = request.ScheduledStartAt,
            ScheduledEndAt = request.ScheduledEndAt,
            DueAt = request.DueAt,
            StartedAt = request.StartedAt,
            CompletedAt = request.CompletedAt,
            ClosedAt = request.ClosedAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        ApplyStatusSideEffects(job, string.Empty, now);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        _db.JobStatusHistories.Add(new JobStatusHistory
        {
            JobId = job.Id,
            FromStatus = null,
            ToStatus = job.Status,
            ChangedByPersonId = currentUser.PersonId,
            ChangedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Job>> UpdateJob(int id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can update jobs." });
        }

        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == id && j.CompanyId == currentUser.CompanyId, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var previousStatus = job.Status;
        var now = DateTimeOffset.UtcNow;

        var patchError = JobUpdateApplier.Apply(job, body);
        if (patchError is not null)
        {
            return BadRequest(new { message = patchError });
        }

        job.UpdatedAt = now;

        ApplyStatusSideEffects(job, previousStatus, now);

        if (!string.Equals(previousStatus, job.Status, StringComparison.OrdinalIgnoreCase))
        {
            _db.JobStatusHistories.Add(new JobStatusHistory
            {
                JobId = job.Id,
                FromStatus = previousStatus,
                ToStatus = job.Status,
                ChangedByPersonId = currentUser.PersonId,
                ChangedAt = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(job);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> ArchiveJob(int id, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can archive jobs." });
        }

        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == id && j.CompanyId == currentUser.CompanyId, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var previousStatus = job.Status;
        var now = DateTimeOffset.UtcNow;

        job.Status = "cancelled";
        job.ClosedAt = now;
        job.UpdatedAt = now;

        if (!string.Equals(previousStatus, job.Status, StringComparison.OrdinalIgnoreCase))
        {
            _db.JobStatusHistories.Add(new JobStatusHistory
            {
                JobId = job.Id,
                FromStatus = previousStatus,
                ToStatus = job.Status,
                ChangedByPersonId = currentUser.PersonId,
                ChangedAt = now,
                Reason = "archived"
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "scheduled";
        }

        return status.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string NormalizePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
        {
            return "normal";
        }

        return priority.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static bool IsValidStatus(string status)
    {
        return status is "scheduled" or "in_progress" or "completed" or "cancelled";
    }

    private static bool IsValidPriority(string priority)
    {
        return priority is "low" or "normal" or "high" or "urgent";
    }

    private static void ApplyStatusSideEffects(Job job, string previousStatus, DateTimeOffset now)
    {
        if (string.Equals(job.Status, "in_progress", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(previousStatus, "in_progress", StringComparison.OrdinalIgnoreCase)
            && job.StartedAt is null)
        {
            job.StartedAt = now;
        }

        if (string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(previousStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            job.CompletedAt ??= now;
        }

        if (string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(previousStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            job.ClosedAt ??= now;
        }
    }
}
