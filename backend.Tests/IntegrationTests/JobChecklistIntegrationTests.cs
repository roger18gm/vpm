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
