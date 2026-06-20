using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class DashboardController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly DashboardService _dashboardService;

    public DashboardController(ICurrentUserService currentUserService, DashboardService dashboardService)
    {
        _currentUserService = currentUserService;
        _dashboardService = dashboardService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var (result, error, statusCode) = await _dashboardService.GetSummaryAsync(currentUser, cancellationToken);
        if (error is not null)
        {
            return StatusCode(statusCode, new { message = error });
        }

        return Ok(result);
    }
}
