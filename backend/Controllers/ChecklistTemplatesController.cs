using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/checklist-templates")]
public sealed class ChecklistTemplatesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyAuthorizationService _companyAuthorization;

    public ChecklistTemplatesController(
        AppDbContext db,
        ICurrentUserService currentUserService,
        ICompanyAuthorizationService companyAuthorization)
    {
        _db = db;
        _currentUserService = currentUserService;
        _companyAuthorization = companyAuthorization;
    }

    [HttpGet]
    public async Task<ActionResult<List<ChecklistTemplateDto>>> List(CancellationToken cancellationToken)
    {
        var (user, error) = await RequireManagerAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var templates = await _db.ChecklistTemplates
            .AsNoTracking()
            .Where(template => template.CompanyId == user!.CompanyId)
            .OrderBy(template => template.Name)
            .ThenBy(template => template.Id)
            .ToListAsync(cancellationToken);
        var items = await LoadItemsAsync(templates.Select(template => template.Id).ToList(), cancellationToken);
        return Ok(templates.Select(template => ToDto(template, items)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ChecklistTemplateDto>> Create(
        [FromBody] CreateChecklistTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var (user, error) = await RequireManagerAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var name = NormalizeLabel(request.Name, "Name", out var nameError);
        if (name is null)
        {
            return BadRequest(new { message = nameError });
        }

        if (await NameTakenAsync(user!.CompanyId, name, null, cancellationToken))
        {
            return BadRequest(new { message = "A template with that name already exists." });
        }

        var hasTemplate = await _db.ChecklistTemplates.AnyAsync(
            template => template.CompanyId == user.CompanyId,
            cancellationToken);
        var isDefault = !hasTemplate || request.IsDefault;
        if (isDefault)
        {
            await ClearDefaultAsync(user.CompanyId, null, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var template = new ChecklistTemplate
        {
            CompanyId = user.CompanyId,
            Name = name,
            IsDefault = isDefault,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.ChecklistTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/checklist-templates/{template.Id}", ToDto(template, []));
    }

    private async Task<(CurrentUserContext? User, ActionResult? Error)> RequireManagerAsync(CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return (null, Unauthorized());
        }

        if (!_companyAuthorization.IsManager(currentUser))
        {
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { message = "Only managers can manage checklist templates." }));
        }

        return (currentUser, null);
    }

    private async Task<List<ChecklistTemplateItem>> LoadItemsAsync(List<int> templateIds, CancellationToken cancellationToken)
    {
        if (templateIds.Count == 0)
        {
            return [];
        }

        return await _db.ChecklistTemplateItems
            .AsNoTracking()
            .Where(item => templateIds.Contains(item.ChecklistTemplateId))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task ClearDefaultAsync(int companyId, int? exceptTemplateId, CancellationToken cancellationToken)
    {
        var others = await _db.ChecklistTemplates
            .Where(template => template.CompanyId == companyId
                && template.IsDefault
                && (exceptTemplateId == null || template.Id != exceptTemplateId))
            .ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            other.IsDefault = false;
        }
    }

    private Task<bool> NameTakenAsync(int companyId, string name, int? exceptTemplateId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();
        return _db.ChecklistTemplates.AnyAsync(
            template => template.CompanyId == companyId
                && (exceptTemplateId == null || template.Id != exceptTemplateId)
                && template.Name.ToLower() == lowered,
            cancellationToken);
    }

    private static ChecklistTemplateDto ToDto(ChecklistTemplate template, IReadOnlyList<ChecklistTemplateItem> items) =>
        new(
            template.Id,
            template.Name,
            template.IsDefault,
            items.Where(item => item.ChecklistTemplateId == template.Id).Select(ToItemDto).ToList());

    private static ChecklistTemplateItemDto ToItemDto(ChecklistTemplateItem item) =>
        new(item.Id, item.Title, item.SortOrder, item.IsRequired);

    internal static string? NormalizeLabel(string? value, string label, out string error)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            error = $"{label} is required.";
            return null;
        }

        if (trimmed.Length > 80)
        {
            error = $"{label} must be 80 characters or fewer.";
            return null;
        }

        error = string.Empty;
        return trimmed;
    }
}
