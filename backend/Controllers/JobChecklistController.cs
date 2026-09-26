using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs/{jobId:int}/checklist")]
public sealed class JobChecklistController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "pending",
        "done",
        "not_applicable"
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyAuthorizationService _companyAuthorization;
    private readonly IJobAccessService _jobAccess;

    public JobChecklistController(
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
    public async Task<ActionResult<JobChecklistDto>> Get(int jobId, CancellationToken cancellationToken)
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

        return Ok(await LoadDtoAsync(jobId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<JobChecklistDto>> Apply(
        int jobId,
        [FromBody] ApplyJobChecklistRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can apply a checklist." });
        }

        var job = await _jobAccess.GetCompanyJobAsync(jobId, currentUser, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        if (string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Cannot update a checklist on a cancelled job." });
        }

        var template = await _db.ChecklistTemplates.FirstOrDefaultAsync(
            row => row.Id == request.TemplateId && row.CompanyId == currentUser.CompanyId,
            cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        var templateItems = await _db.ChecklistTemplateItems
            .Where(item => item.ChecklistTemplateId == template.Id)
            .ToListAsync(cancellationToken);
        if (templateItems.Count == 0)
        {
            return BadRequest(new { message = "Add at least one item before applying this template." });
        }

        var existing = await _db.JobChecklistItems.Where(row => row.JobId == jobId).ToListAsync(cancellationToken);
        var existingTemplateIds = await (
            from row in _db.JobChecklistItems
            join item in _db.ChecklistTemplateItems on row.TemplateItemId equals item.Id
            where row.JobId == jobId
            select item.ChecklistTemplateId).Distinct().ToListAsync(cancellationToken);
        var sameTemplate = existingTemplateIds.Count == 1 && existingTemplateIds[0] == template.Id;

        if (existing.Count > 0 && !sameTemplate)
        {
            _db.JobChecklistItems.RemoveRange(existing);
            foreach (var item in templateItems)
            {
                _db.JobChecklistItems.Add(Pending(jobId, item.Id));
            }
        }
        else
        {
            var have = existing.Select(row => row.TemplateItemId).ToHashSet();
            foreach (var item in templateItems.Where(item => !have.Contains(item.Id)))
            {
                _db.JobChecklistItems.Add(Pending(jobId, item.Id));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(await LoadDtoAsync(jobId, cancellationToken));
    }

    [HttpPatch("items/{templateItemId:int}")]
    public async Task<ActionResult<JobChecklistItemDto>> UpdateStatus(
        int jobId,
        int templateItemId,
        [FromBody] UpdateJobChecklistItemRequest request,
        CancellationToken cancellationToken)
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

        var job = await _jobAccess.GetCompanyJobAsync(jobId, currentUser, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        if (string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Cannot update a checklist on a cancelled job." });
        }

        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedStatuses.Contains(status))
        {
            return BadRequest(new { message = "Invalid checklist status." });
        }

        var row = await _db.JobChecklistItems.FirstOrDefaultAsync(
            item => item.JobId == jobId && item.TemplateItemId == templateItemId,
            cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        row.Status = status;
        if (status == "pending")
        {
            row.CompletedByPersonId = null;
            row.CompletedAt = null;
        }
        else
        {
            row.CompletedByPersonId = currentUser.PersonId;
            row.CompletedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        var dto = await LoadDtoAsync(jobId, cancellationToken);
        return Ok(dto.Items.Single(item => item.TemplateItemId == templateItemId));
    }

    private static JobChecklistItem Pending(int jobId, int templateItemId) =>
        new()
        {
            JobId = jobId,
            TemplateItemId = templateItemId,
            Status = "pending"
        };

    private async Task<JobChecklistDto> LoadDtoAsync(int jobId, CancellationToken cancellationToken)
    {
        var rows = await (
            from row in _db.JobChecklistItems.AsNoTracking()
            join item in _db.ChecklistTemplateItems.AsNoTracking() on row.TemplateItemId equals item.Id
            join template in _db.ChecklistTemplates.AsNoTracking() on item.ChecklistTemplateId equals template.Id
            join person in _db.People.AsNoTracking() on row.CompletedByPersonId equals person.Id into people
            from person in people.DefaultIfEmpty()
            where row.JobId == jobId
            orderby item.SortOrder, item.Id
            select new
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                ItemId = item.Id,
                item.Title,
                item.SortOrder,
                item.IsRequired,
                row.Status,
                row.CompletedByPersonId,
                CompletedByName = person == null ? null : person.Name,
                row.CompletedAt
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new JobChecklistDto(null, null, []);
        }

        return new JobChecklistDto(
            rows[0].TemplateId,
            rows[0].TemplateName,
            rows.Select(row => new JobChecklistItemDto(
                row.ItemId,
                row.Title,
                row.SortOrder,
                row.IsRequired,
                row.Status,
                row.CompletedByPersonId,
                row.CompletedByName,
                row.CompletedAt)).ToList());
    }
}
