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

    [HttpPatch("{templateId:int}")]
    public async Task<ActionResult<ChecklistTemplateDto>> Update(
        int templateId,
        [FromBody] UpdateChecklistTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var (user, error) = await RequireManagerAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (request.Name is null && request.IsDefault is null)
        {
            return BadRequest(new { message = "Provide a name and/or default." });
        }

        if (request.IsDefault == false)
        {
            return BadRequest(new { message = "Set another template as the default." });
        }

        var template = await _db.ChecklistTemplates.FirstOrDefaultAsync(
            row => row.Id == templateId && row.CompanyId == user!.CompanyId,
            cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (request.Name is not null)
        {
            var name = NormalizeLabel(request.Name, "Name", out var nameError);
            if (name is null)
            {
                return BadRequest(new { message = nameError });
            }

            if (await NameTakenAsync(user!.CompanyId, name, template.Id, cancellationToken))
            {
                return BadRequest(new { message = "A template with that name already exists." });
            }

            template.Name = name;
        }

        if (request.IsDefault == true)
        {
            await ClearDefaultAsync(user!.CompanyId, template.Id, cancellationToken);
            template.IsDefault = true;
        }

        template.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        var items = await LoadItemsAsync([template.Id], cancellationToken);
        return Ok(ToDto(template, items));
    }

    [HttpDelete("{templateId:int}")]
    public async Task<IActionResult> Delete(int templateId, CancellationToken cancellationToken)
    {
        var (user, error) = await RequireManagerAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var template = await _db.ChecklistTemplates.FirstOrDefaultAsync(
            row => row.Id == templateId && row.CompanyId == user!.CompanyId,
            cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        var inUse = await _db.JobChecklistItems.AnyAsync(
            row => _db.ChecklistTemplateItems.Any(item =>
                item.Id == row.TemplateItemId && item.ChecklistTemplateId == template.Id),
            cancellationToken);
        if (inUse)
        {
            return BadRequest(new { message = "This template is still on a job." });
        }

        if (template.IsDefault)
        {
            var next = await _db.ChecklistTemplates
                .Where(row => row.CompanyId == user!.CompanyId && row.Id != template.Id)
                .OrderBy(row => row.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.IsDefault = true;
            }
        }

        _db.ChecklistTemplates.Remove(template);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{templateId:int}/items")]
    public async Task<ActionResult<ChecklistTemplateItemDto>> CreateItem(
        int templateId,
        [FromBody] CreateChecklistTemplateItemRequest request,
        CancellationToken cancellationToken)
    {
        var (template, error) = await LoadCompanyTemplateAsync(templateId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var title = NormalizeLabel(request.Title, "Title", out var titleError);
        if (title is null)
        {
            return BadRequest(new { message = titleError });
        }

        if (await TitleTakenAsync(template!.Id, title, null, cancellationToken))
        {
            return BadRequest(new { message = "An item with that title already exists on this template." });
        }

        var maxSort = await _db.ChecklistTemplateItems
            .Where(item => item.ChecklistTemplateId == template.Id)
            .Select(item => (int?)item.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;
        var item = new ChecklistTemplateItem
        {
            ChecklistTemplateId = template.Id,
            Title = title,
            SortOrder = maxSort + 1,
            IsRequired = request.IsRequired
        };
        _db.ChecklistTemplateItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/checklist-templates/{template.Id}/items/{item.Id}", ToItemDto(item));
    }

    [HttpPatch("{templateId:int}/items/{itemId:int}")]
    public async Task<ActionResult<ChecklistTemplateItemDto>> UpdateItem(
        int templateId,
        int itemId,
        [FromBody] UpdateChecklistTemplateItemRequest request,
        CancellationToken cancellationToken)
    {
        var (template, error) = await LoadCompanyTemplateAsync(templateId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (request.Title is null && request.IsRequired is null)
        {
            return BadRequest(new { message = "Provide a title and/or required flag." });
        }

        var item = await _db.ChecklistTemplateItems.FirstOrDefaultAsync(
            row => row.Id == itemId && row.ChecklistTemplateId == template!.Id,
            cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (request.Title is not null)
        {
            var title = NormalizeLabel(request.Title, "Title", out var titleError);
            if (title is null)
            {
                return BadRequest(new { message = titleError });
            }

            if (await TitleTakenAsync(template!.Id, title, item.Id, cancellationToken))
            {
                return BadRequest(new { message = "An item with that title already exists on this template." });
            }

            item.Title = title;
        }

        if (request.IsRequired is not null)
        {
            item.IsRequired = request.IsRequired.Value;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToItemDto(item));
    }

    [HttpDelete("{templateId:int}/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int templateId, int itemId, CancellationToken cancellationToken)
    {
        var (template, error) = await LoadCompanyTemplateAsync(templateId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var item = await _db.ChecklistTemplateItems.FirstOrDefaultAsync(
            row => row.Id == itemId && row.ChecklistTemplateId == template!.Id,
            cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var inUse = await _db.JobChecklistItems.AnyAsync(row => row.TemplateItemId == item.Id, cancellationToken);
        if (inUse)
        {
            return BadRequest(new { message = "This item is still on a job." });
        }

        _db.ChecklistTemplateItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(ChecklistTemplate? Template, ActionResult? Error)> LoadCompanyTemplateAsync(
        int templateId,
        CancellationToken cancellationToken)
    {
        var (user, error) = await RequireManagerAsync(cancellationToken);
        if (error is not null)
        {
            return (null, error);
        }

        var template = await _db.ChecklistTemplates.FirstOrDefaultAsync(
            row => row.Id == templateId && row.CompanyId == user!.CompanyId,
            cancellationToken);
        if (template is null)
        {
            return (null, NotFound());
        }

        return (template, null);
    }

    private Task<bool> TitleTakenAsync(int templateId, string title, int? exceptItemId, CancellationToken cancellationToken)
    {
        var lowered = title.ToLowerInvariant();
        return _db.ChecklistTemplateItems.AnyAsync(
            item => item.ChecklistTemplateId == templateId
                && (exceptItemId == null || item.Id != exceptItemId)
                && item.Title.ToLower() == lowered,
            cancellationToken);
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
