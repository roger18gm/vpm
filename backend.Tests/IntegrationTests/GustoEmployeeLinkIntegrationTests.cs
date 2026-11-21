using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;
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
        _fixture.Factory.Services.GetRequiredService<StubGustoHandler>().Reset();
        _fixture.Factory.Services.GetRequiredService<GustoAccessTokenCache>().Clear();
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

    [Fact]
    public async Task Owner_loads_the_directory_without_pay_rates()
    {
        var owner = await BootstrapOwnerAsync();
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        handler.EmployeePageBodies.Enqueue(
            """[{"uuid":"emp-z","first_name":"Zed","last_name":"Last","email":"zed@example.com"},{"uuid":"emp-a","first_name":"Ada","last_name":"Painter","email":"ada@example.com"}]""");
        handler.JobBodies["emp-a"] =
            """[{"uuid":"job-a","title":"Painter","primary":true,"rate":"25.00","payment_unit":"Hour"}]""";
        handler.JobBodies["emp-z"] =
            """[{"uuid":"job-z","title":"Lead","primary":false,"rate":"40.00"}]""";
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        var response = await _fixture.Client.GetAsync("/api/users/gusto-directory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("25.00", text);
        Assert.DoesNotContain("payment_unit", text, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadFromJsonAsync<GustoEmployeeDirectoryDto>();
        Assert.Equal(new[] { "emp-a", "emp-z" }, body!.Employees.Select(employee => employee.Uuid).ToArray());
        Assert.Equal("Painter", body.Employees[0].Jobs.Single().Title);
        Assert.Empty(body.Links);
    }

    [Fact]
    public async Task Manager_cannot_load_the_directory()
    {
        var owner = await BootstrapOwnerAsync();
        var managerEmail = $"manager-{Guid.NewGuid():N}@example.com";
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        using var create = await _fixture.Client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Demo Manager",
            managerEmail,
            "Password123!",
            "manager"));
        create.EnsureSuccessStatusCode();
        var manager = await _fixture.AuthClient.LoginAsync(new LoginRequest(managerEmail, "Password123!"));
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        _fixture.AuthClient.SetBearerToken(manager.AccessToken);

        var response = await _fixture.Client.GetAsync("/api/users/gusto-directory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Only owners and admins can manage users.", await response.Content.ReadAsStringAsync());
        Assert.Empty(handler.EmployeeRequestUris);
        Assert.Equal(0, handler.TokenRequests);
    }

    [Fact]
    public async Task Missing_gusto_configuration_returns_503_for_the_directory()
    {
        var owner = await BootstrapOwnerAsync();
        await using var factory = new TestWebApplicationFactory(_fixture.Database.ConnectionString, gustoConfigured: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var response = await client.GetAsync("/api/users/gusto-directory");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("not configured", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.Services.GetRequiredService<StubGustoHandler>().TokenRequests);
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