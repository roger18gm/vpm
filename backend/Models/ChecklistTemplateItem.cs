namespace VisionPaint.Models;

public sealed class ChecklistTemplateItem
{
    public int Id { get; set; }

    public int ChecklistTemplateId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsRequired { get; set; } = true;
}
