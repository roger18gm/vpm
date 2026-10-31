# Job closed with gaps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Workspace:** Implement in the current workspace. Do not create a git worktree.
>
> **Commits:** Skip every `git commit` step unless the human has asked for commits in this session.

**Goal:** When a job enters completed with a required checklist item still pending or with zero photos, email the person who saved it.

**Architecture:** `JobCompletionGapMailer` runs after the job row is stored. Create calls it after the transaction commits. Update calls it after `SaveChanges`. The mailer reads checklist rows and photos, builds one HTML body and one plain-text body, and sends through the existing `IEmailSender`. A send failure is logged and does not change the HTTP result.

**Tech Stack:** ASP.NET Core, EF Core, xUnit, existing `IEmailSender` and `RecordingEmailSender`

**Spec:** [docs/superpowers/specs/2026-10-31-job-closed-with-gaps-design.md](../specs/2026-10-31-job-closed-with-gaps-design.md)

## Global Constraints

- Recipient is `CurrentUserContext.Email` for the signed-in person who saved the job.
- A checklist gap exists only when `job_checklist_item` joined to `checklist_template_item` has `is_required` true and status `pending`. Order those titles by sort order, then template item id.
- A job with no checklist is not a checklist gap. A pending optional item does not count. A required item with status `not_applicable` does not count.
- A photo gap exists only when the job has zero `job_photo` rows. Any `photo_kind` counts as a photo.
- Both gaps produce one email. Omit a section that does not apply, including its heading.
- Subject is `Completed with gaps: {job title}`. Replace `\r\n`, then `\r`, then `\n` in the title with a single space each.
- Body order: job title, then link `{FrontendBaseUrl}/jobs/{id}` with one trailing slash removed from `AppOptions.FrontendBaseUrl`, then heading `Open required checklist items` and the titles, then the sentence `This job has no photos.`
- HTML-encode the job title, each item title, the link `href`, and the link text. Plain text uses the stored title and item titles. Each plain-text item line is `- {title}`.
- Send on `POST /api/jobs` when the created status is `completed`, and on `PUT /api/jobs/{id}` when the stored status becomes `completed`. Previous status null, whitespace, or anything other than `completed` counts as entering completed. Comparison is ordinal ignore case.
- Saving a job that is already `completed` sends nothing. Archive sends nothing.
- Call the mailer only after the job is stored. Create: after `CommitAsync`. The mailer does not call `SaveChanges`.
- `SendAsync` throwing is logged with the job id and `NotifyAsync` returns. Create still returns 201. Update still returns 200. That transition is not retried.
- A null, empty, or whitespace recipient is logged as a warning with the job id, and nothing is sent.
- No new table. No app UI change. Do not change password-reset mail or Monday payroll mail.

---

## File map

| File | Responsibility |
|------|----------------|
| `backend/Services/JobCompletionGapMailer.cs` | Decide, build, and send the one email |
| `backend/Controllers/JobsController.cs` | Call the mailer after create commits and after update saves |
| `backend/Program.cs` | Register the scoped mailer |
| `backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs` | Spec scenarios 1–10 and the blank-recipient case |
| `docs/superpowers/specs/2026-10-31-job-closed-with-gaps-design.md` | Mark implemented when the tasks pass |
| `docs/backlog/reporting-and-notifications.md` | Mark item 2 implemented when the tasks pass |

---

### Task 1: Photo gap on update

**Files:**
- Create: `backend/Services/JobCompletionGapMailer.cs`
- Modify: `backend/Controllers/JobsController.cs` (constructor, and `UpdateJob` after `SaveChanges`)
- Modify: `backend/Program.cs` (register the mailer next to `PayrollSummaryService`)
- Create: `backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs`

**Interfaces:**
- Consumes: `IEmailSender.SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default)`, `AppOptions.FrontendBaseUrl`, `Job`, `JobPhoto`
- Produces: `Task JobCompletionGapMailer.NotifyAsync(Job job, string? previousStatus, string? recipientEmail, CancellationToken cancellationToken)`
- Produces these test helpers, used by later tasks:
  - `Task<string> BootstrapOwnerAsync()` — returns the lowercase sign-in email and clears `EmailSender`
  - `Task<Job> CreateJobAsync(string title, string status)`
  - `Task<Job> SetStatusAsync(int jobId, string status)`
  - `Task UploadPhotoAsync(int jobId)`
  - `Task<JobChecklistDto> ApplyChecklistAsync(int jobId, params (string Title, bool IsRequired)[] items)`
  - `Task PatchItemAsync(int jobId, int templateItemId, string status)`
  - `Task CreateOwnerAsync(string email)`

This task sends the photo sentence only. It does not query checklist items. Checklist sections are Task 3. The create action is Task 2. `SendAsync` exceptions are Task 5.

- [ ] **Step 1: Write the failing tests**

Create `backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobCompletionGapIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private readonly BackendIntegrationFixture _fixture;

    public JobCompletionGapIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Update_to_completed_with_no_checklist_and_no_photos_sends_photo_line()
    {
        var email = await BootstrapOwnerAsync();
        var job = await CreateJobAsync("Bare close", "scheduled");

        var completed = await SetStatusAsync(job.Id, "completed");

        Assert.Equal("completed", completed.Status);
        var mail = Assert.Single(_fixture.EmailSender.Sent);
        Assert.Equal(email, mail.To);
        Assert.Equal("Completed with gaps: Bare close", mail.Subject);
        Assert.Contains("http://localhost:5173/jobs/" + job.Id, mail.Html);
        Assert.Contains("http://localhost:5173/jobs/" + job.Id, mail.Text);
        Assert.Contains("This job has no photos.", mail.Html);
        Assert.Contains("This job has no photos.", mail.Text);
        Assert.DoesNotContain("Open required checklist items", mail.Html);
        Assert.DoesNotContain("Open required checklist items", mail.Text);
    }

    [Fact]
    public async Task Update_to_completed_with_a_photo_and_no_checklist_sends_nothing()
    {
        await BootstrapOwnerAsync();
        var job = await CreateJobAsync("Has a photo", "scheduled");
        await UploadPhotoAsync(job.Id);
        _fixture.EmailSender.Clear();

        var completed = await SetStatusAsync(job.Id, "completed");

        Assert.Equal("completed", completed.Status);
        Assert.Empty(_fixture.EmailSender.Sent);
    }

    private async Task<string> BootstrapOwnerAsync()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            email,
            "Password123!"));
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        _fixture.EmailSender.Clear();
        return email;
    }

    private async Task<Job> CreateJobAsync(string title, string status)
    {
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title, status });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<Job>())!;
    }

    private async Task<Job> SetStatusAsync(int jobId, string status)
    {
        var updated = await _fixture.Client.PutAsJsonAsync($"/api/jobs/{jobId}", new { status });
        updated.EnsureSuccessStatusCode();
        return (await updated.Content.ReadFromJsonAsync<Job>())!;
    }

    private async Task UploadPhotoAsync(int jobId)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TinyPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "test.png");
        form.Add(new StringContent("progress"), "photoKind");
        using var upload = await _fixture.Client.PostAsync($"/api/jobs/{jobId}/photos", form);
        upload.EnsureSuccessStatusCode();
    }

    private async Task<JobChecklistDto> ApplyChecklistAsync(int jobId, params (string Title, bool IsRequired)[] items)
    {
        using var templateResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/checklist-templates",
            new CreateChecklistTemplateRequest($"Gap {Guid.NewGuid():N}", false));
        templateResponse.EnsureSuccessStatusCode();
        var template = (await templateResponse.Content.ReadFromJsonAsync<ChecklistTemplateDto>())!;

        foreach (var item in items)
        {
            using var itemResponse = await _fixture.Client.PostAsJsonAsync(
                $"/api/checklist-templates/{template.Id}/items",
                new CreateChecklistTemplateItemRequest(item.Title, item.IsRequired));
            itemResponse.EnsureSuccessStatusCode();
        }

        using var applied = await _fixture.Client.PostAsJsonAsync(
            $"/api/jobs/{jobId}/checklist",
            new ApplyJobChecklistRequest(template.Id));
        applied.EnsureSuccessStatusCode();
        return (await applied.Content.ReadFromJsonAsync<JobChecklistDto>())!;
    }

    private async Task PatchItemAsync(int jobId, int templateItemId, string status)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/jobs/{jobId}/checklist/items/{templateItemId}")
        {
            Content = JsonContent.Create(new UpdateJobChecklistItemRequest(status))
        };
        using var response = await _fixture.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task CreateOwnerAsync(string email)
    {
        using var create = await _fixture.Client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest("Other Owner", email, "Password123!", "owner"));
        create.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~JobCompletionGapIntegrationTests`

Expected: FAIL. `Update_to_completed_with_no_checklist_and_no_photos_sends_photo_line` fails because `EmailSender.Sent` is empty. The photo test can fail for the same reason or pass if no mail is sent. A passing photo test is acceptable at this step. The no-photo test must fail.

- [ ] **Step 3: Write the mailer, register it, and call it from update**

Create `backend/Services/JobCompletionGapMailer.cs`:

```csharp
using System.Net;
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

        var hasPhoto = await _db.JobPhotos.AsNoTracking()
            .AnyAsync(photo => photo.JobId == job.Id, cancellationToken);
        if (hasPhoto)
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
        var html = "<p>" + WebUtility.HtmlEncode(job.Title) + "</p><p><a href=\""
            + WebUtility.HtmlEncode(link) + "\">" + WebUtility.HtmlEncode(link) + "</a></p><p>This job has no photos.</p>";
        var text = job.Title + "\n\n" + link + "\n\nThis job has no photos.";
        await _email.SendAsync(recipientEmail, subject, html, text, cancellationToken);
    }

    private static bool IsCompleted(string? status) =>
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
}
```

In `backend/Program.cs`, immediately after `builder.Services.AddScoped<PayrollSummaryService>();`, add:

```csharp
builder.Services.AddScoped<JobCompletionGapMailer>();
```

In `JobsController`, add a field and constructor parameter. Do not call the mailer from `ArchiveJob`.

Field, next to `_jobAccess`:

```csharp
private readonly JobCompletionGapMailer _jobCompletionGapMailer;
```

Constructor parameter, last, and assign it:

```csharp
public JobsController(
    AppDbContext db,
    ICurrentUserService currentUserService,
    ICompanyAuthorizationService companyAuthorization,
    IJobAccessService jobAccess,
    JobCompletionGapMailer jobCompletionGapMailer)
{
    _db = db;
    _currentUserService = currentUserService;
    _companyAuthorization = companyAuthorization;
    _jobAccess = jobAccess;
    _jobCompletionGapMailer = jobCompletionGapMailer;
}
```

In `UpdateJob`, replace the save-and-return tail:

```csharp
        await _db.SaveChangesAsync(cancellationToken);
        await _jobCompletionGapMailer.NotifyAsync(job, previousStatus, currentUser.Email, cancellationToken);
        return Ok(job);
```

`previousStatus` is the status captured before `JobUpdateApplier.Apply`. Leave that capture where it is.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~JobCompletionGapIntegrationTests`

Expected: PASS. Both tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/Services/JobCompletionGapMailer.cs backend/Controllers/JobsController.cs backend/Program.cs backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs
git commit -m "feat: email when a completed job has no photos"
```

---

### Task 2: Create already completed

**Files:**
- Modify: `backend/Controllers/JobsController.cs` (`CreateJob`, after `CommitAsync`)
- Test: `backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs`

**Interfaces:**
- Consumes: `NotifyAsync(Job job, string? previousStatus, string? recipientEmail, CancellationToken cancellationToken)` and the Task 1 helpers `BootstrapOwnerAsync`, `CreateJobAsync`

- [ ] **Step 1: Write the failing test**

Add this test to `JobCompletionGapIntegrationTests`:

```csharp
    [Fact]
    public async Task Create_completed_with_no_photos_sends_photo_line_and_scheduled_create_does_not()
    {
        var email = await BootstrapOwnerAsync();

        var scheduled = await CreateJobAsync("Later", "scheduled");
        Assert.Empty(_fixture.EmailSender.Sent);

        var completed = await CreateJobAsync("Done now", "completed");

        Assert.Equal("completed", completed.Status);
        Assert.Equal("scheduled", scheduled.Status);
        var mail = Assert.Single(_fixture.EmailSender.Sent);
        Assert.Equal(email, mail.To);
        Assert.Equal("Completed with gaps: Done now", mail.Subject);
        Assert.Contains("This job has no photos.", mail.Html);
        Assert.Contains("This job has no photos.", mail.Text);
        Assert.Contains("http://localhost:5173/jobs/" + completed.Id, mail.Html);
        Assert.DoesNotContain("Open required checklist items", mail.Html);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~Create_completed_with_no_photos_sends_photo_line_and_scheduled_create_does_not`

Expected: FAIL because `EmailSender.Sent` is empty after the completed create.

- [ ] **Step 3: Call the mailer after the create transaction commits**

In `CreateJob`, `CommitAsync` is the last line before `return CreatedAtAction`. Replace that return with:

```csharp
        await transaction.CommitAsync(cancellationToken);
        await _jobCompletionGapMailer.NotifyAsync(job, null, currentUser.Email, cancellationToken);
        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
```

The call stays outside the transaction. Do not add a call in `ArchiveJob`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~JobCompletionGapIntegrationTests`

Expected: PASS. The Task 1 tests still pass.

- [ ] **Step 5: Commit**

```bash
git add backend/Controllers/JobsController.cs backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs
git commit -m "feat: email when a job is created already completed with no photos"
```

---

### Task 3: Checklist gaps

**Files:**
- Modify: `backend/Services/JobCompletionGapMailer.cs`
- Test: `backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 1 helpers `BootstrapOwnerAsync`, `CreateJobAsync`, `SetStatusAsync`, `UploadPhotoAsync`, `ApplyChecklistAsync`, `PatchItemAsync`, `CreateOwnerAsync`
- Produces: the same `NotifyAsync` signature. The body now includes pending required item titles.

A photo still suppresses the photo sentence. A pending required item now sends even when a photo exists. Task 1's early `if (hasPhoto) return;` must be removed so a checklist gap is still emailed.

- [ ] **Step 1: Write the failing tests**

Add these three tests to `JobCompletionGapIntegrationTests`:

```csharp
    [Fact]
    public async Task Update_to_completed_with_pending_required_item_and_a_photo_emails_the_saver()
    {
        var email = await BootstrapOwnerAsync();
        var otherEmail = $"other-{Guid.NewGuid():N}@example.com";
        await CreateOwnerAsync(otherEmail);
        var title = "Gap <job>\nline";
        var job = await CreateJobAsync(title, "scheduled");
        await ApplyChecklistAsync(job.Id, ("Mask <item>", true));
        await UploadPhotoAsync(job.Id);
        _fixture.EmailSender.Clear();

        var completed = await SetStatusAsync(job.Id, "completed");

        Assert.Equal("completed", completed.Status);
        var mail = Assert.Single(_fixture.EmailSender.Sent);
        Assert.Equal(email, mail.To);
        Assert.NotEqual(otherEmail, mail.To);
        Assert.Equal("Completed with gaps: Gap <job> line", mail.Subject);
        Assert.Contains("Gap &lt;job&gt;", mail.Html);
        Assert.Contains("Mask &lt;item&gt;", mail.Html);
        Assert.Contains("Open required checklist items", mail.Html);
        Assert.Contains("<li>Mask &lt;item&gt;</li>", mail.Html);
        Assert.Contains("http://localhost:5173/jobs/" + job.Id, mail.Html);
        Assert.DoesNotContain("This job has no photos.", mail.Html);
        Assert.Contains("Gap <job>\nline", mail.Text);
        Assert.Contains("Open required checklist items", mail.Text);
        Assert.Contains("- Mask <item>", mail.Text);
        Assert.DoesNotContain("This job has no photos.", mail.Text);
    }

    [Fact]
    public async Task Update_to_completed_with_pending_item_and_no_photos_lists_both_gaps()
    {
        await BootstrapOwnerAsync();
        var job = await CreateJobAsync("Both gaps", "scheduled");
        await ApplyChecklistAsync(job.Id, ("Masking", true), ("Sanding", true));
        _fixture.EmailSender.Clear();

        await SetStatusAsync(job.Id, "completed");

        var mail = Assert.Single(_fixture.EmailSender.Sent);
        Assert.Contains("Open required checklist items", mail.Html);
        Assert.Contains("<li>Masking</li>", mail.Html);
        Assert.Contains("<li>Sanding</li>", mail.Html);
        Assert.Contains("This job has no photos.", mail.Html);
        var text = mail.Text ?? "";
        var maskingAt = text.IndexOf("- Masking", StringComparison.Ordinal);
        var sandingAt = text.IndexOf("- Sanding", StringComparison.Ordinal);
        Assert.True(maskingAt >= 0 && sandingAt > maskingAt);
        Assert.Contains("This job has no photos.", text);
    }

    [Fact]
    public async Task Update_to_completed_ignores_done_not_applicable_and_optional_items()
    {
        await BootstrapOwnerAsync();
        var job = await CreateJobAsync("Closed checklist", "scheduled");
        var checklist = await ApplyChecklistAsync(
            job.Id,
            ("Sanding", true),
            ("Masking", true),
            ("Optional note", false));
        await PatchItemAsync(job.Id, checklist.Items.Single(item => item.Title == "Sanding").TemplateItemId, "done");
        await PatchItemAsync(job.Id, checklist.Items.Single(item => item.Title == "Masking").TemplateItemId, "not_applicable");
        await UploadPhotoAsync(job.Id);
        _fixture.EmailSender.Clear();

        var completed = await SetStatusAsync(job.Id, "completed");

        Assert.Equal("completed", completed.Status);
        Assert.Empty(_fixture.EmailSender.Sent);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~Update_to_completed_with_pending_required_item_and_a_photo_emails_the_saver|FullyQualifiedName~Update_to_completed_with_pending_item_and_no_photos_lists_both_gaps|FullyQualifiedName~Update_to_completed_ignores_done_not_applicable_and_optional_items"`

Expected: FAIL. The photo-and-pending-item test fails because no email is sent. The both-gaps test fails because the body has no checklist heading. The ignore test can pass already. A passing ignore test is acceptable at this step. The first two tests must fail.

- [ ] **Step 3: Load pending required items and render both sections**

Replace `backend/Services/JobCompletionGapMailer.cs` with:

```csharp
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
        await _email.SendAsync(recipientEmail, subject, html, text, cancellationToken);
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
```

Leave the controller calls unchanged.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~JobCompletionGapIntegrationTests`

Expected: PASS. Task 1 and Task 2 tests still pass. The photo sentence is still absent when a photo exists, and still present when it does not.

- [ ] **Step 5: Commit**

```bash
git add backend/Services/JobCompletionGapMailer.cs backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs
git commit -m "feat: include open required checklist items in the job gap email"
```

---

### Task 4: Already completed, and completing again

**Files:**
- Test: `backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs`
- Modify: `backend/Services/JobCompletionGapMailer.cs` only if Step 2 fails

**Interfaces:**
- Consumes: `NotifyAsync` from Task 3, and helpers `BootstrapOwnerAsync`, `CreateJobAsync`, `SetStatusAsync`, `UploadPhotoAsync`, `ApplyChecklistAsync`, `PatchItemAsync`

The status gate is already in `NotifyAsync`: return when `previousStatus` is `completed`. These tests lock that gate and the reopen behavior. Do not add a send table.

- [ ] **Step 1: Write the tests**

Add these three tests:

```csharp
    [Fact]
    public async Task Update_already_completed_job_sends_nothing()
    {
        await BootstrapOwnerAsync();
        var job = await CreateJobAsync("Stay closed", "scheduled");
        await ApplyChecklistAsync(job.Id, ("Sanding", true));
        await UploadPhotoAsync(job.Id);
        await SetStatusAsync(job.Id, "completed");
        _fixture.EmailSender.Clear();

        var again = await SetStatusAsync(job.Id, "completed");

        Assert.Equal("completed", again.Status);
        Assert.Empty(_fixture.EmailSender.Sent);
    }

    [Fact]
    public async Task Completing_again_after_reopen_sends_again_while_a_gap_remains()
    {
        await BootstrapOwnerAsync();
        var job = await CreateJobAsync("Reopen me", "scheduled");
        await ApplyChecklistAsync(job.Id, ("Sanding", true));
        await UploadPhotoAsync(job.Id);
        await SetStatusAsync(job.Id, "completed");
        _fixture.EmailSender.Clear();

        await SetStatusAsync(job.Id, "in_progress");
        Assert.Empty(_fixture.EmailSender.Sent);
        var completed = await SetStatusAsync(job.Id, "completed");

        Assert.Equal("completed", completed.Status);
        var mail = Assert.Single(_fixture.EmailSender.Sent);
        Assert.Contains("Open required checklist items", mail.Text);
        Assert.Contains("- Sanding", mail.Text);
        Assert.DoesNotContain("This job has no photos.", mail.Text);
    }

    [Fact]
    public async Task Completing_again_after_the_gap_is_cleared_sends_nothing()
    {
        await BootstrapOwnerAsync();
        var job = await CreateJobAsync("Fix then close", "scheduled");
        var checklist = await ApplyChecklistAsync(job.Id, ("Sanding", true));
        await UploadPhotoAsync(job.Id);
        await SetStatusAsync(job.Id, "completed");
        await PatchItemAsync(job.Id, checklist.Items.Single().TemplateItemId, "done");
        _fixture.EmailSender.Clear();

        await SetStatusAsync(job.Id, "in_progress");
        var completed = await SetStatusAsync(job.Id, "completed");

        Assert.Equal("completed", completed.Status);
        Assert.Empty(_fixture.EmailSender.Sent);
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~Update_already_completed_job_sends_nothing|FullyQualifiedName~Completing_again_after_reopen_sends_again_while_a_gap_remains|FullyQualifiedName~Completing_again_after_the_gap_is_cleared_sends_nothing"`

Expected: PASS.

If `Update_already_completed_job_sends_nothing` sends a message, the start of `NotifyAsync` is wrong. It must be:

```csharp
        if (!IsCompleted(job.Status) || IsCompleted(previousStatus))
        {
            return;
        }
```

with `IsCompleted` using `StringComparison.OrdinalIgnoreCase`. `UpdateJob` must pass the status captured before the update as `previousStatus`. Fix that and rerun this step.

- [ ] **Step 3: Run the whole fixture class**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~JobCompletionGapIntegrationTests`

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs backend/Services/JobCompletionGapMailer.cs
git commit -m "test: cover repeat completion for the job gap email"
```

---

### Task 5: Sender failure and a blank address

**Files:**
- Modify: `backend/Services/JobCompletionGapMailer.cs` (wrap `SendAsync`)
- Test: `backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs`

**Interfaces:**
- Consumes: `NotifyAsync(Job job, string? previousStatus, string? recipientEmail, CancellationToken cancellationToken)`
- The blank-address warning text is `Job completion gap email skipped because recipient address is blank for job {JobId}`
- The send-failure log is `Job completion gap email failed for job {JobId}` and includes the exception

- [ ] **Step 1: Write the failing tests**

Add these usings to the test file:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisionPaint.Data;
using VisionPaint.Services;
```

Add these tests and the private sender class:

```csharp
    [Fact]
    public async Task NotifyAsync_returns_when_the_sender_throws()
    {
        await BootstrapOwnerAsync();
        var created = await CreateJobAsync("Throw on send", "in_progress");
        var completed = await SetStatusAsync(created.Id, "completed");
        Assert.Equal("completed", completed.Status);

        await using var db = OpenDb();
        var job = await db.Jobs.SingleAsync(row => row.Id == created.Id);
        var mailer = new JobCompletionGapMailer(
            db,
            new ThrowingEmailSender(),
            Options.Create(new AppOptions { FrontendBaseUrl = "http://localhost:5173" }),
            NullLogger<JobCompletionGapMailer>.Instance);

        await mailer.NotifyAsync(job, "in_progress", "owner@example.com", CancellationToken.None);

        var status = await db.Jobs.Where(row => row.Id == created.Id).Select(row => row.Status).SingleAsync();
        Assert.Equal("completed", status);
    }

    [Fact]
    public async Task NotifyAsync_skips_a_blank_recipient()
    {
        await BootstrapOwnerAsync();
        var created = await CreateJobAsync("Blank recipient", "scheduled");

        await using var db = OpenDb();
        var job = await db.Jobs.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        job.Status = "completed";
        var sender = new RecordingEmailSender();
        var mailer = new JobCompletionGapMailer(
            db,
            sender,
            Options.Create(new AppOptions { FrontendBaseUrl = "http://localhost:5173" }),
            NullLogger<JobCompletionGapMailer>.Instance);

        await mailer.NotifyAsync(job, "scheduled", "  ", CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Equal("scheduled", await db.Jobs.Where(row => row.Id == created.Id).Select(row => row.Status).SingleAsync());
    }

    private AppDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.Database.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(
            string toEmail,
            string subject,
            string htmlBody,
            string? textBody = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Brevo rejected the message.");
        }
    }
```

`NotifyAsync_skips_a_blank_recipient` copies the job with `AsNoTracking`, sets that copy's status to `completed`, and does not call `SaveChanges`. The database row stays `scheduled`. The test fails only if `SendAsync` is called.

- [ ] **Step 2: Run the throw test to verify it fails**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~NotifyAsync_returns_when_the_sender_throws`

Expected: FAIL. `NotifyAsync` throws `InvalidOperationException` with message `Brevo rejected the message.`

- [ ] **Step 3: Catch send failures**

In `NotifyAsync`, replace the bare send:

```csharp
        await _email.SendAsync(recipientEmail, subject, html, text, cancellationToken);
```

with:

```csharp
        try
        {
            await _email.SendAsync(recipientEmail, subject, html, text, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job completion gap email failed for job {JobId}", job.Id);
        }
```

Do not catch the blank-address path. That path returns before the try. Do not change the job row.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter "FullyQualifiedName~NotifyAsync_returns_when_the_sender_throws|FullyQualifiedName~NotifyAsync_skips_a_blank_recipient"`

Expected: PASS.

Then run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~JobCompletionGapIntegrationTests`

Expected: PASS. All scenarios in this class pass.

- [ ] **Step 5: Commit**

```bash
git add backend/Services/JobCompletionGapMailer.cs backend.Tests/IntegrationTests/JobCompletionGapIntegrationTests.cs
git commit -m "fix: keep the job completed when the gap email fails"
```

---

### Task 6: Mark the spec and backlog implemented

**Files:**
- Modify: `docs/superpowers/specs/2026-10-31-job-closed-with-gaps-design.md` (status line only)
- Modify: `docs/backlog/reporting-and-notifications.md` (status line only)

**Interfaces:**
- Consumes: Tasks 1–5 passing

- [ ] **Step 1: Confirm the tests**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~JobCompletionGapIntegrationTests`

Expected: PASS.

- [ ] **Step 2: Update the two status lines**

In the spec, replace the status line with:

```markdown
**Status:** Implemented (2026-10-31)
```

In the backlog, replace the status line with:

```markdown
**Status:** Item 1 has a design pending review. Item 2 is implemented. Items 3–4 are noted only.
```

Leave the item 2 design and plan links in place. Do not edit items 1, 3, or 4.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-10-31-job-closed-with-gaps-design.md docs/backlog/reporting-and-notifications.md
git commit -m "docs: mark the job gap email implemented"
```
