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
public sealed class JobsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;

    public JobsController(AppDbContext db, ICurrentUserService currentUserService)
    {
        _db = db;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Job>>> GetJobs(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var jobs = await _db.Jobs
            .AsNoTracking()
            .Where(job => job.CompanyId == currentUser.CompanyId)
            .OrderByDescending(job => job.UpdatedAt)
            .ThenByDescending(job => job.Id)
            .ToListAsync(cancellationToken);

        return Ok(jobs);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Job>> GetJob(int id, CancellationToken cancellationToken)
    {
        var job = await LoadJobAsync(id, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost]
    public async Task<ActionResult<Job>> CreateJob([FromBody] Job request, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            CompanyId = currentUser.CompanyId,
            ClientId = request.ClientId,
            CreatedByPersonId = currentUser.PersonId,
            Title = request.Title.Trim(),
            Description = request.Description,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "scheduled" : request.Status,
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority,
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

        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Job>> UpdateJob(int id, [FromBody] Job request, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var job = await _db.Jobs.FirstOrDefaultAsync(job => job.Id == id && job.CompanyId == currentUser.CompanyId, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var previousStatus = job.Status;
        var now = DateTimeOffset.UtcNow;

        job.ClientId = request.ClientId;
        job.Title = request.Title.Trim();
        job.Description = request.Description;
        job.Status = string.IsNullOrWhiteSpace(request.Status) ? job.Status : request.Status;
        job.Priority = string.IsNullOrWhiteSpace(request.Priority) ? job.Priority : request.Priority;
        job.AddressLine1 = request.AddressLine1;
        job.AddressLine2 = request.AddressLine2;
        job.City = request.City;
        job.StateRegion = request.StateRegion;
        job.PostalCode = request.PostalCode;
        job.CountryCode = request.CountryCode;
        job.ScheduledStartAt = request.ScheduledStartAt;
        job.ScheduledEndAt = request.ScheduledEndAt;
        job.DueAt = request.DueAt;
        job.StartedAt = request.StartedAt;
        job.CompletedAt = request.CompletedAt;
        job.ClosedAt = request.ClosedAt;
        job.UpdatedAt = now;

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

        var job = await _db.Jobs.FirstOrDefaultAsync(job => job.Id == id && job.CompanyId == currentUser.CompanyId, cancellationToken);
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

    private async Task<Job?> LoadJobAsync(int id, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return null;
        }

        return await _db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(job => job.Id == id && job.CompanyId == currentUser.CompanyId, cancellationToken);
    }
}
