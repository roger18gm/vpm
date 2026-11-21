using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class GustoEmployeeLinkIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public GustoEmployeeLinkIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Duplicate_employee_uuid_in_one_company_is_rejected()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture,
            $"crew-{Guid.NewGuid():N}@example.com");
        await using var db = CreateDb();
        var companyId = await db.CompanyMembers
            .Where(member => member.PersonId == owner.User.PersonId)
            .Select(member => member.CompanyId)
            .SingleAsync();
        var now = DateTimeOffset.UtcNow;
        db.GustoEmployeeLinks.Add(Link(owner.User.PersonId, companyId, "emp-1", "job-1", now));
        await db.SaveChangesAsync();

        db.GustoEmployeeLinks.Add(Link(crewPersonId, companyId, "emp-1", "job-2", now));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task<AuthTokenResponse> BootstrapOwnerAsync()
    {
        return await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));
    }

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_fixture.Database.ConnectionString).Options);

    private static GustoEmployeeLink Link(
        int personId,
        int companyId,
        string employeeUuid,
        string jobUuid,
        DateTimeOffset now) => new()
    {
        PersonId = personId,
        CompanyId = companyId,
        GustoEmployeeUuid = employeeUuid,
        GustoJobUuid = jobUuid,
        CreatedAt = now,
        UpdatedAt = now
    };
}