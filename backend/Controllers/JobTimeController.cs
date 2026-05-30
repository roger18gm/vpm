using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs/{jobId:int}/time")]
public sealed class JobTimeController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeEntryService _timeEntryService;

    public JobTimeController(ICurrentUserService currentUserService, TimeEntryService timeEntryService)
    {
        _currentUserService = currentUserService;
        _timeEntryService = timeEntryService;
    }

    [HttpGet]
    public async Task<ActionResult<JobTimeSummaryDto>> GetJobTime(int jobId, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var summary = await _timeEntryService.GetJobSummaryAsync(jobId, currentUser, cancellationToken);
        if (summary is null)
        {
            return NotFound();
        }

        return Ok(summary);
    }
}
