using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class JobCompletionGapMailer
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly AppOptions _app;
    private readonly ILogger<JobCompletionGapMailer> _logger;

    public JobCompletionGapMailer(
        AppDbContext db,
        IEmailSender email,
        IOptions<AppOptions> app,
        ILogger<JobCompletionGapMailer> logger)
    {
        _db = db;
        _email = email;
        _app = app.Value;
        _logger = logger;
    }

    public async Task NotifyAsync(
        Job job,
        string? previousStatus,
        string? recipientEmail,
        CancellationToken cancellationToken)
    {
        if (!IsCompleted(job.Status) || IsCompleted(previousStatus))
        {
            return;
        }

        var openItems = await (
            from row in _db.JobChecklistItems.AsNoTracking()
            join item in _db.ChecklistTemplateItems.AsNoTracking() on row.TemplateItemId equals item.Id
            where row.JobId == job.Id && item.IsRequired && row.Status == "pending"
            orderby item.SortOrder, item.Id
            select item.Title).ToListAsync(cancellationToken);

        var hasPhoto = await _db.JobPhotos.AsNoTracking()
            .AnyAsync(photo => photo.JobId == job.Id, cancellationToken);
        if (openItems.Count == 0 && hasPhoto)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning(
                "Job completion gap email skipped because recipient address is blank for job {JobId}",
                job.Id);
            return;
        }

        var subjectTitle = job.Title.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        var subject = $"Completed with gaps: {subjectTitle}";
        var link = $"{_app.FrontendBaseUrl.TrimEnd('/')}/jobs/{job.Id}";
        var html = BuildHtml(job.Title, link, openItems, hasPhoto);
        var text = BuildText(job.Title, link, openItems, hasPhoto);
        try
        {
            await _email.SendAsync(recipientEmail, subject, html, text, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job completion gap email failed for job {JobId}", job.Id);
        }
    }

    private static bool IsCompleted(string? status) =>
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static string BuildHtml(string title, string link, IReadOnlyList<string> openItems, bool hasPhoto)
    {
        var html = new StringBuilder();
        var encodedLink = WebUtility.HtmlEncode(link);
        html.Append("<p>").Append(WebUtility.HtmlEncode(title)).Append("</p>");
        html.Append("<p><a href=\"").Append(encodedLink).Append("\">").Append(encodedLink).Append("</a></p>");
        if (openItems.Count > 0)
        {
            html.Append("<h2>Open required checklist items</h2><ul>");
            foreach (var item in openItems)
            {
                html.Append("<li>").Append(WebUtility.HtmlEncode(item)).Append("</li>");
            }

            html.Append("</ul>");
        }

        if (!hasPhoto)
        {
            html.Append("<p>This job has no photos.</p>");
        }

        return html.ToString();
    }

    private static string BuildText(string title, string link, IReadOnlyList<string> openItems, bool hasPhoto)
    {
        var text = new StringBuilder();
        text.Append(title).Append("\n\n").Append(link);
        if (openItems.Count > 0)
        {
            text.Append("\n\nOpen required checklist items");
            foreach (var item in openItems)
            {
                text.Append("\n- ").Append(item);
            }
        }

        if (!hasPhoto)
        {
            text.Append("\n\nThis job has no photos.");
        }

        return text.ToString();
    }
}
