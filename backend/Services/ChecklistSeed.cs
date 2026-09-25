using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public static class ChecklistSeed
{
    public static async Task EnsureDefaultAsync(AppDbContext db, int companyId, CancellationToken cancellationToken)
    {
        var exists = await db.ChecklistTemplates.AnyAsync(
            template => template.CompanyId == companyId,
            cancellationToken);
        if (exists)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var template = new ChecklistTemplate
        {
            CompanyId = companyId,
            Name = "Surface prep",
            IsDefault = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ChecklistTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        db.ChecklistTemplateItems.AddRange(
            new ChecklistTemplateItem { ChecklistTemplateId = template.Id, Title = "Sanding", SortOrder = 1, IsRequired = true },
            new ChecklistTemplateItem { ChecklistTemplateId = template.Id, Title = "Masking", SortOrder = 2, IsRequired = true },
            new ChecklistTemplateItem { ChecklistTemplateId = template.Id, Title = "Priming", SortOrder = 3, IsRequired = true },
            new ChecklistTemplateItem { ChecklistTemplateId = template.Id, Title = "Cleanup", SortOrder = 4, IsRequired = true });
        await db.SaveChangesAsync(cancellationToken);
    }
}
