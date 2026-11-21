using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    [Fact]
    public async Task Owner_saves_a_link_and_a_repeat_stays_one_row()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture,
            $"crew-{Guid.NewGuid():N}@example.com");
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        SetDirectory("emp-1", "job-1");
        var first = await PutLinkAsync(crewPersonId, "emp-1", "job-1");
        SetDirectory("emp-1", "job-1");
        var second = await PutLinkAsync(crewPersonId, "emp-1", "job-1");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using var db = CreateDb();
        var link = await db.GustoEmployeeLinks.SingleAsync();
        Assert.Equal(crewPersonId, link.PersonId);
        Assert.Equal(owner.User.CompanyId, link.CompanyId);
        Assert.Equal("emp-1", link.GustoEmployeeUuid);
        Assert.Equal("job-1", link.GustoJobUuid);
    }

    [Fact]
    public async Task Save_rejects_a_job_from_another_employee_and_a_missing_employee()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture,
            $"crew-{Guid.NewGuid():N}@example.com");
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        handler.EmployeePageBodies.Enqueue(
            """[{"uuid":"emp-1","first_name":"Ada","last_name":"Painter","email":"ada@example.com"},{"uuid":"emp-2","first_name":"Bea","last_name":"Brush","email":"bea@example.com"}]""");
        handler.JobBodies["emp-1"] = """[{"uuid":"job-1","title":"Painter","primary":true}]""";
        handler.JobBodies["emp-2"] = """[{"uuid":"job-2","title":"Lead","primary":true}]""";
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);

        var wrongJob = await PutLinkAsync(crewPersonId, "emp-1", "job-2");
        var missingEmployee = await PutLinkAsync(crewPersonId, "emp-missing", "job-1");
        var blank = await PutLinkAsync(crewPersonId, " ", " ");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongJob.StatusCode);
        Assert.Contains("does not belong to the selected employee", await wrongJob.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingEmployee.StatusCode);
        Assert.Contains("not available to link", await missingEmployee.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Contains("Choose a Gusto employee and a compensation job.", await blank.Content.ReadAsStringAsync());
        await using var db = CreateDb();
        Assert.Empty(await db.GustoEmployeeLinks.ToListAsync());
    }

    [Fact]
    public async Task Second_user_cannot_take_the_same_employee()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, firstPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture,
            $"ada-{Guid.NewGuid():N}@example.com");
        var (_, secondPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture,
            $"bea-{Guid.NewGuid():N}@example.com");
        await using (var db = CreateDb())
        {
            var ada = await db.People.SingleAsync(person => person.Id == firstPersonId);
            ada.Name = "Ada Painter";
            await db.SaveChangesAsync();
        }

        SetDirectory("emp-1", "job-1");
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var saved = await PutLinkAsync(firstPersonId, "emp-1", "job-1");
        saved.EnsureSuccessStatusCode();
        SetDirectory("emp-1", "job-1");

        var conflict = await PutLinkAsync(secondPersonId, "emp-1", "job-1");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("already linked to Ada Painter", await conflict.Content.ReadAsStringAsync());
        await using var check = CreateDb();
        var link = await check.GustoEmployeeLinks.SingleAsync();
        Assert.Equal(firstPersonId, link.PersonId);
    }

    [Fact]
    public async Task Replacing_a_link_leaves_sent_hours_alone()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture,
            $"crew-{Guid.NewGuid():N}@example.com");
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        using var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Link house" });
        created.EnsureSuccessStatusCode();
        var paintingJob = await created.Content.ReadFromJsonAsync<Job>();
        SetDirectory("emp-1", "job-1", "emp-2", "job-2");
        (await PutLinkAsync(crewPersonId, "emp-1", "job-1")).EnsureSuccessStatusCode();
        await using (var db = CreateDb())
        {
            var now = DateTimeOffset.UtcNow;
            var entry = new TimeEntry
            {
                JobId = paintingJob!.Id,
                PersonId = crewPersonId,
                ClockInAt = now.AddHours(-2),
                ClockOutAt = now.AddHours(-1),
                BreakMinutes = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            db.GustoTimeSheetSyncs.Add(new GustoTimeSheetSync
            {
                TimeEntryId = entry.Id,
                GustoTimeSheetUuid = "ts-1",
                Status = "synced",
                HoursWorked = 1.0000m,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        SetDirectory("emp-1", "job-1", "emp-2", "job-9");
        var replaced = await PutLinkAsync(crewPersonId, "emp-2", "job-9");

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        await using var check = CreateDb();
        var link = await check.GustoEmployeeLinks.SingleAsync();
        Assert.Equal("emp-2", link.GustoEmployeeUuid);
        Assert.Equal("job-9", link.GustoJobUuid);
        var sync = await check.GustoTimeSheetSyncs.SingleAsync();
        Assert.Equal("synced", sync.Status);
        Assert.Equal("ts-1", sync.GustoTimeSheetUuid);
    }

    [Fact]
    public async Task Inactive_member_and_manager_cannot_save()
    {
        var owner = await BootstrapOwnerAsync();
        var (_, crewPersonId) = await TestDataFactory.CreateCrewUserAsync(
            _fixture,
            $"crew-{Guid.NewGuid():N}@example.com");
        await using (var db = CreateDb())
        {
            var member = await db.CompanyMembers.SingleAsync(row => row.PersonId == crewPersonId);
            member.Status = "disabled";
            await db.SaveChangesAsync();
        }

        SetDirectory("emp-1", "job-1");
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var inactive = await PutLinkAsync(crewPersonId, "emp-1", "job-1");
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        Assert.Contains("Person not found.", await inactive.Content.ReadAsStringAsync());

        var managerEmail = $"manager-{Guid.NewGuid():N}@example.com";
        using var create = await _fixture.Client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Demo Manager",
            managerEmail,
            "Password123!",
            "manager"));
        create.EnsureSuccessStatusCode();
        var manager = await _fixture.AuthClient.LoginAsync(new LoginRequest(managerEmail, "Password123!"));
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        var callsBefore = handler.EmployeeRequestUris.Count;
        _fixture.AuthClient.SetBearerToken(manager.AccessToken);
        var forbidden = await PutLinkAsync(crewPersonId, "emp-1", "job-1");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(callsBefore, handler.EmployeeRequestUris.Count);
        await using var check = CreateDb();
        Assert.Empty(await check.GustoEmployeeLinks.ToListAsync());
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

    private void SetDirectory(params string[] employeeThenJob)
    {
        var handler = _fixture.Factory.Services.GetRequiredService<StubGustoHandler>();
        var employees = new StringBuilder("[");
        for (var i = 0; i < employeeThenJob.Length; i += 2)
        {
            var employeeUuid = employeeThenJob[i];
            var jobUuid = employeeThenJob[i + 1];
            if (i > 0)
            {
                employees.Append(',');
            }

            employees.Append($$"""{"uuid":"{{employeeUuid}}","first_name":"Ada","last_name":"Painter","email":"ada@example.com"}""");
            handler.JobBodies[employeeUuid] =
                $$"""[{"uuid":"{{jobUuid}}","title":"Painter","primary":true}]""";
        }

        employees.Append(']');
        handler.EmployeePageBodies.Enqueue(employees.ToString());
    }

    private Task<HttpResponseMessage> PutLinkAsync(int personId, string employeeUuid, string jobUuid) =>
        _fixture.Client.PutAsJsonAsync(
            $"/api/users/{personId}/gusto-link",
            new SaveGustoEmployeeLinkRequest(employeeUuid, jobUuid));
}