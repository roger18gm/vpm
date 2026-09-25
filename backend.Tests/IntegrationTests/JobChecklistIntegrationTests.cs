using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobChecklistIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public JobChecklistIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Bootstrap_restores_surface_prep_after_reset()
    {
        await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        await using var db = OpenDb();
        var template = await db.ChecklistTemplates.SingleAsync(row => row.Name == "Surface prep" && row.IsDefault);
        var items = await db.ChecklistTemplateItems
            .Where(item => item.ChecklistTemplateId == template.Id)
            .OrderBy(item => item.SortOrder)
            .ToListAsync();

        Assert.Equal(new[] { "Sanding", "Masking", "Priming", "Cleanup" }, items.Select(item => item.Title));
        Assert.Equal(new[] { 1, 2, 3, 4 }, items.Select(item => item.SortOrder));
        Assert.All(items, item => Assert.True(item.IsRequired));
    }

    [Fact]
    public async Task Manager_lists_seeded_surface_prep_template()
    {
        await BootstrapOwnerAsync();

        var templates = await _fixture.Client.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/checklist-templates");

        Assert.NotNull(templates);
        var template = Assert.Single(templates!);
        Assert.Equal("Surface prep", template.Name);
        Assert.True(template.IsDefault);
        Assert.Equal(new[] { "Sanding", "Masking", "Priming", "Cleanup" }, template.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task Second_template_is_not_default_until_requested()
    {
        await BootstrapOwnerAsync();

        using var created = await _fixture.Client.PostAsJsonAsync(
            "/api/checklist-templates",
            new CreateChecklistTemplateRequest("Exterior", false));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdTemplate = await created.Content.ReadFromJsonAsync<ChecklistTemplateDto>();
        Assert.NotNull(createdTemplate);
        Assert.False(createdTemplate!.IsDefault);
        Assert.Empty(createdTemplate.Items);

        var templates = await _fixture.Client.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/checklist-templates");
        Assert.Equal(new[] { "Exterior", "Surface prep" }, templates!.Select(template => template.Name));
        Assert.True(templates.Single(template => template.Name == "Surface prep").IsDefault);
    }

    [Fact]
    public async Task Duplicate_template_name_returns_400()
    {
        await BootstrapOwnerAsync();

        using var duplicate = await _fixture.Client.PostAsJsonAsync(
            "/api/checklist-templates",
            new CreateChecklistTemplateRequest("surface prep", false));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Manager_adds_renames_and_toggles_items_in_sort_order()
    {
        await BootstrapOwnerAsync();
        var template = await CreateTemplateAsync("Exterior");

        using var first = await _fixture.Client.PostAsJsonAsync(
            $"/api/checklist-templates/{template.Id}/items",
            new CreateChecklistTemplateItemRequest("Scrape", true));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await _fixture.Client.PostAsJsonAsync(
            $"/api/checklist-templates/{template.Id}/items",
            new CreateChecklistTemplateItemRequest("Caulk", false));
        second.EnsureSuccessStatusCode();
        var caulk = await second.Content.ReadFromJsonAsync<ChecklistTemplateItemDto>();

        using var renamed = await PatchItemAsync(template.Id, caulk!.Id, new UpdateChecklistTemplateItemRequest("Caulk gaps", null));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var required = await PatchItemAsync(template.Id, caulk.Id, new UpdateChecklistTemplateItemRequest(null, true));
        required.EnsureSuccessStatusCode();

        var templates = await _fixture.Client.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/checklist-templates");
        var exterior = templates!.Single(row => row.Id == template.Id);
        Assert.Equal(new[] { "Scrape", "Caulk gaps" }, exterior.Items.Select(item => item.Title));
        Assert.Equal(new[] { 1, 2 }, exterior.Items.Select(item => item.SortOrder));
        Assert.All(exterior.Items, item => Assert.True(item.IsRequired));
    }

    [Fact]
    public async Task Duplicate_item_title_returns_400()
    {
        await BootstrapOwnerAsync();
        var template = await CreateTemplateAsync("Exterior");
        var created = await _fixture.Client.PostAsJsonAsync(
            $"/api/checklist-templates/{template.Id}/items",
            new CreateChecklistTemplateItemRequest("Scrape", true));
        created.EnsureSuccessStatusCode();

        using var duplicate = await _fixture.Client.PostAsJsonAsync(
            $"/api/checklist-templates/{template.Id}/items",
            new CreateChecklistTemplateItemRequest("scrape", true));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Clearing_default_returns_400_and_setting_default_moves_it()
    {
        await BootstrapOwnerAsync();
        var surface = (await _fixture.Client.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/checklist-templates"))!.Single();
        var exterior = await CreateTemplateAsync("Exterior");

        using var clear = await PatchTemplateAsync(surface.Id, new UpdateChecklistTemplateRequest(null, false));
        Assert.Equal(HttpStatusCode.BadRequest, clear.StatusCode);

        using var makeDefault = await PatchTemplateAsync(exterior.Id, new UpdateChecklistTemplateRequest(null, true));
        makeDefault.EnsureSuccessStatusCode();

        var templates = await _fixture.Client.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/checklist-templates");
        Assert.False(templates!.Single(template => template.Id == surface.Id).IsDefault);
        Assert.True(templates.Single(template => template.Id == exterior.Id).IsDefault);
    }

    [Fact]
    public async Task Deleting_the_default_promotes_the_oldest_remaining_template()
    {
        await BootstrapOwnerAsync();
        var surface = (await _fixture.Client.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/checklist-templates"))!.Single();
        var exterior = await CreateTemplateAsync("Exterior");
        await CreateTemplateAsync("Interior");
        var makeDefault = await PatchTemplateAsync(exterior.Id, new UpdateChecklistTemplateRequest(null, true));
        makeDefault.EnsureSuccessStatusCode();

        using var deleted = await _fixture.Client.DeleteAsync($"/api/checklist-templates/{exterior.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var templates = await _fixture.Client.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/checklist-templates");
        Assert.True(templates!.Single(template => template.Id == surface.Id).IsDefault);
        Assert.False(templates.Single(template => template.Name == "Interior").IsDefault);
    }

    [Fact]
    public async Task Delete_item_in_use_returns_400()
    {
        await BootstrapOwnerAsync();
        var template = (await _fixture.Client.GetFromJsonAsync<List<ChecklistTemplateDto>>("/api/checklist-templates"))!.Single();
        var item = template.Items[0];
        var jobId = await CreateJobAsync("Prep job");

        await using (var db = OpenDb())
        {
            db.JobChecklistItems.Add(new JobChecklistItem
            {
                JobId = jobId,
                TemplateItemId = item.Id,
                Status = "pending"
            });
            await db.SaveChangesAsync();
        }

        using var deleteItem = await _fixture.Client.DeleteAsync($"/api/checklist-templates/{template.Id}/items/{item.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteItem.StatusCode);

        using var deleteTemplate = await _fixture.Client.DeleteAsync($"/api/checklist-templates/{template.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteTemplate.StatusCode);
    }

    [Fact]
    public async Task Template_from_another_company_returns_404()
    {
        await BootstrapOwnerAsync();
        int foreignTemplateId;
        await using (var db = OpenDb())
        {
            var company = new Company
            {
                Name = "Other Co",
                Timezone = "America/Denver",
                LanguageCode = "en",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            var template = new ChecklistTemplate
            {
                CompanyId = company.Id,
                Name = "Foreign",
                IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ChecklistTemplates.Add(template);
            await db.SaveChangesAsync();
            foreignTemplateId = template.Id;
        }

        using var patch = await PatchTemplateAsync(foreignTemplateId, new UpdateChecklistTemplateRequest("Renamed", null));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    private async Task<ChecklistTemplateDto> CreateTemplateAsync(string name)
    {
        using var created = await _fixture.Client.PostAsJsonAsync(
            "/api/checklist-templates",
            new CreateChecklistTemplateRequest(name, false));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<ChecklistTemplateDto>())!;
    }

    private async Task<int> CreateJobAsync(string title)
    {
        using var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title });
        created.EnsureSuccessStatusCode();
        var job = await created.Content.ReadFromJsonAsync<Job>();
        return job!.Id;
    }

    private async Task<HttpResponseMessage> PatchTemplateAsync(int templateId, UpdateChecklistTemplateRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/checklist-templates/{templateId}")
        {
            Content = JsonContent.Create(body)
        };
        return await _fixture.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PatchItemAsync(int templateId, int itemId, UpdateChecklistTemplateItemRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/checklist-templates/{templateId}/items/{itemId}")
        {
            Content = JsonContent.Create(body)
        };
        return await _fixture.Client.SendAsync(request);
    }

    private async Task BootstrapOwnerAsync()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
    }

    private AppDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.Database.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}
