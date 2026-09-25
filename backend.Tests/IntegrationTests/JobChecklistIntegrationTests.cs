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

    private AppDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.Database.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}
