using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;
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
}
