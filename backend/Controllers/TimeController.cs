using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class TimeController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeEntryService _timeEntryService;

    public TimeController(ICurrentUserService currentUserService, TimeEntryService timeEntryService)
    {
        _currentUserService = currentUserService;
        _timeEntryService = timeEntryService;
    }

    [HttpGet("active")]
    public async Task<ActionResult<ActiveTimeEntryDto?>> GetActive(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var active = await _timeEntryService.GetActiveAsync(currentUser, cancellationToken);
        return Ok(active);
    }

    [HttpPost("clock-in")]
    public async Task<ActionResult<ActiveTimeEntryDto>> ClockIn(
        [FromBody] ClockInRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var (result, error, statusCode) = await _timeEntryService.ClockInAsync(currentUser, request.JobId, cancellationToken);
        if (error is not null)
        {
            return StatusCode(statusCode, new { message = error });
        }

        return Ok(result);
    }

    [HttpPost("clock-out")]
    public async Task<ActionResult<ClockOutSummaryDto>> ClockOut(
        [FromBody] ClockOutRequest? request,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var (result, error, statusCode) = await _timeEntryService.ClockOutAsync(
            currentUser,
            request?.Notes,
            cancellationToken);

        if (error is not null)
        {
            return StatusCode(statusCode, new { message = error });
        }

        return Ok(result);
    }

    [HttpPost("break/start")]
    public async Task<IActionResult> StartBreak(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var (success, error, statusCode) = await _timeEntryService.StartBreakAsync(currentUser, cancellationToken);
        if (!success)
        {
            return StatusCode(statusCode, new { message = error });
        }

        return NoContent();
    }

    [HttpPost("break/end")]
    public async Task<IActionResult> EndBreak(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var (success, error, statusCode) = await _timeEntryService.EndBreakAsync(currentUser, cancellationToken);
        if (!success)
        {
            return StatusCode(statusCode, new { message = error });
        }

        return NoContent();
    }

    [HttpGet("weekly")]
    public async Task<ActionResult<WeeklyTimesheetDto>> GetWeekly(
        [FromQuery] string? weekStart,
        [FromQuery] int? personId,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        DateOnly? parsedWeekStart = null;
        if (!string.IsNullOrWhiteSpace(weekStart))
        {
            if (!DateOnly.TryParse(weekStart, out var date))
            {
                return BadRequest(new { message = "weekStart must be YYYY-MM-DD." });
            }

            parsedWeekStart = date;
        }

        var (result, error, statusCode) = await _timeEntryService.GetWeeklyTimesheetAsync(
            currentUser,
            parsedWeekStart,
            personId,
            cancellationToken);

        if (error is not null)
        {
            return StatusCode(statusCode, new { message = error });
        }

        return Ok(result);
    }
}
