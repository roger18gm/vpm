using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs/{jobId:int}/status-history")]
public sealed class JobStatusHistoryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IJobAccessService _jobAccess;

    public JobStatusHistoryController(
        AppDbContext db,
        ICurrentUserService currentUserService,
        IJobAccessService jobAccess)
    {
        _db = db;
        _currentUserService = currentUserService;
        _jobAccess = jobAccess;
    }

    [HttpGet]
    public async Task<ActionResult<List<JobStatusHistoryDto>>> List(int jobId, CancellationToken cancellationToken)
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

        var rows = await (
            from history in _db.JobStatusHistories.AsNoTracking()
            join person in _db.People.AsNoTracking() on history.ChangedByPersonId equals person.Id into people
            from person in people.DefaultIfEmpty()
            where history.JobId == jobId
            orderby history.ChangedAt descending
            select new JobStatusHistoryDto(
                history.FromStatus,
                history.ToStatus,
                history.ChangedAt,
                person != null ? person.Name : "System",
                history.Reason)
        ).ToListAsync(cancellationToken);

        return Ok(rows);
    }
}
