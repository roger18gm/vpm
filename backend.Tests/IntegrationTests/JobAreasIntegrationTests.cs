using System.Net;
using System.Net.Http.Json;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobAreasIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private readonly BackendIntegrationFixture _fixture;

    public JobAreasIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Manager_creates_areas_in_order_with_not_started()
    {
        var jobId = await CreateJobAsOwnerAsync("Area job");

        foreach (var name in new[] { "Kitchen", "Exterior Trim", "Bedroom 1" })
        {
            using var created = await _fixture.Client.PostAsJsonAsync(
                $"/api/jobs/{jobId}/areas",
                new CreateJobAreaRequest(name));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var list = await _fixture.Client.GetFromJsonAsync<List<JobAreaDto>>($"/api/jobs/{jobId}/areas");
        Assert.NotNull(list);
        Assert.Equal(new[] { "Kitchen", "Exterior Trim", "Bedroom 1" }, list!.Select(area => area.Name));
        Assert.All(list, area => Assert.Equal("not_started", area.Status));
        Assert.Equal(new[] { 1, 2, 3 }, list.Select(area => area.SortOrder));
    }

    [Fact]
    public async Task Duplicate_name_on_same_job_returns_400()
    {
        var jobId = await CreateJobAsOwnerAsync("Dup job");
        var first = await _fixture.Client.PostAsJsonAsync(
            $"/api/jobs/{jobId}/areas",
            new CreateJobAreaRequest("Kitchen"));
        first.EnsureSuccessStatusCode();

        using var duplicate = await _fixture.Client.PostAsJsonAsync(
            $"/api/jobs/{jobId}/areas",
            new CreateJobAreaRequest("kitchen"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Assigned_crew_can_list_but_cannot_create()
    {
        var jobId = await CreateJobAsOwnerAsync("Crew areas");
        var (crewTokens, crewPersonId) = await CreateCrewOnCurrentCompanyAsync();

        var assign = await _fixture.Client.PutAsJsonAsync(
            $"/api/jobs/{jobId}/assignments",
            new ReplaceJobAssignmentsRequest(new[] { crewPersonId }));
        assign.EnsureSuccessStatusCode();

        var area = await _fixture.Client.PostAsJsonAsync(
            $"/api/jobs/{jobId}/areas",
            new CreateJobAreaRequest("Kitchen"));
        area.EnsureSuccessStatusCode();

        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);
        var list = await _fixture.Client.GetFromJsonAsync<List<JobAreaDto>>($"/api/jobs/{jobId}/areas");
        Assert.Single(list!);

        using var create = await _fixture.Client.PostAsJsonAsync(
            $"/api/jobs/{jobId}/areas",
            new CreateJobAreaRequest("Bath"));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Unassigned_crew_list_returns_404()
    {
        var jobId = await CreateJobAsOwnerAsync("Hidden areas");
        var (crewTokens, _) = await CreateCrewOnCurrentCompanyAsync();
        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);

        using var response = await _fixture.Client.GetAsync($"/api/jobs/{jobId}/areas");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_status_sets_timestamps_without_changing_siblings()
    {
        var jobId = await CreateJobAsOwnerAsync("Status job");
        var kitchen = await PostAreaAsync(jobId, "Kitchen");
        var trim = await PostAreaAsync(jobId, "Exterior Trim");

        using var inProgress = await PatchAreaAsync(jobId, kitchen.Id, new UpdateJobAreaRequest(null, "in_progress"));
        Assert.Equal(HttpStatusCode.OK, inProgress.StatusCode);
        var progressing = await inProgress.Content.ReadFromJsonAsync<JobAreaDto>();
        Assert.Equal("in_progress", progressing!.Status);
        Assert.NotNull(progressing.StartedAt);
        Assert.Null(progressing.CompletedAt);

        using var completed = await PatchAreaAsync(jobId, kitchen.Id, new UpdateJobAreaRequest(null, "completed"));
        var done = await completed.Content.ReadFromJsonAsync<JobAreaDto>();
        Assert.Equal("completed", done!.Status);
        Assert.NotNull(done.StartedAt);
        Assert.NotNull(done.CompletedAt);

        using var reopen = await PatchAreaAsync(jobId, kitchen.Id, new UpdateJobAreaRequest(null, "blocked"));
        var blocked = await reopen.Content.ReadFromJsonAsync<JobAreaDto>();
        Assert.Equal("blocked", blocked!.Status);
        Assert.NotNull(blocked.StartedAt);
        Assert.Null(blocked.CompletedAt);

        var list = await _fixture.Client.GetFromJsonAsync<List<JobAreaDto>>($"/api/jobs/{jobId}/areas");
        var sibling = Assert.Single(list!, area => area.Id == trim.Id);
        Assert.Equal("not_started", sibling.Status);
        Assert.Null(sibling.StartedAt);
    }

    [Fact]
    public async Task Patch_empty_body_returns_400()
    {
        var jobId = await CreateJobAsOwnerAsync("Empty patch");
        var area = await PostAreaAsync(jobId, "Kitchen");
        using var response = await PatchAreaAsync(jobId, area.Id, new UpdateJobAreaRequest(null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_area_from_later_list()
    {
        var jobId = await CreateJobAsOwnerAsync("Delete job");
        var area = await PostAreaAsync(jobId, "Kitchen");
        using var deleted = await _fixture.Client.DeleteAsync($"/api/jobs/{jobId}/areas/{area.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var list = await _fixture.Client.GetFromJsonAsync<List<JobAreaDto>>($"/api/jobs/{jobId}/areas");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Crew_cannot_patch_or_delete()
    {
        var jobId = await CreateJobAsOwnerAsync("Crew mutate");
        var area = await PostAreaAsync(jobId, "Kitchen");
        var (crewTokens, crewPersonId) = await CreateCrewOnCurrentCompanyAsync();
        var assign = await _fixture.Client.PutAsJsonAsync(
            $"/api/jobs/{jobId}/assignments",
            new ReplaceJobAssignmentsRequest(new[] { crewPersonId }));
        assign.EnsureSuccessStatusCode();

        _fixture.AuthClient.SetBearerToken(crewTokens.AccessToken);
        using var patch = await PatchAreaAsync(jobId, area.Id, new UpdateJobAreaRequest(null, "completed"));
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);

        using var delete = await _fixture.Client.DeleteAsync($"/api/jobs/{jobId}/areas/{area.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Cancelled_job_rejects_mutations()
    {
        var jobId = await CreateJobAsOwnerAsync("Cancel areas");
        var area = await PostAreaAsync(jobId, "Kitchen");
        using var archive = await _fixture.Client.DeleteAsync($"/api/jobs/{jobId}");
        archive.EnsureSuccessStatusCode();

        using var create = await _fixture.Client.PostAsJsonAsync(
            $"/api/jobs/{jobId}/areas",
            new CreateJobAreaRequest("Bath"));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        using var patch = await PatchAreaAsync(jobId, area.Id, new UpdateJobAreaRequest(null, "completed"));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);

        using var delete = await _fixture.Client.DeleteAsync($"/api/jobs/{jobId}/areas/{area.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
    }

    private async Task<int> CreateJobAsOwnerAsync(string title)
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));
        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title });
        created.EnsureSuccessStatusCode();
        var job = await created.Content.ReadFromJsonAsync<Job>();
        return job!.Id;
    }

    private async Task<(AuthTokenResponse Tokens, int PersonId)> CreateCrewOnCurrentCompanyAsync()
    {
        var email = $"crew-{Guid.NewGuid():N}@example.com";
        using var create = await _fixture.Client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest("Crew User", email, "Password123!", "crew"));
        create.EnsureSuccessStatusCode();
        var user = await create.Content.ReadFromJsonAsync<UserAdminDto>();
        var tokens = await _fixture.AuthClient.LoginAsync(new LoginRequest(email, "Password123!"));
        return (tokens, user!.PersonId);
    }

    private async Task<JobAreaDto> PostAreaAsync(int jobId, string name)
    {
        using var created = await _fixture.Client.PostAsJsonAsync(
            $"/api/jobs/{jobId}/areas",
            new CreateJobAreaRequest(name));
        created.EnsureSuccessStatusCode();
        var dto = await created.Content.ReadFromJsonAsync<JobAreaDto>();
        return dto!;
    }

    private async Task<HttpResponseMessage> PatchAreaAsync(int jobId, int areaId, UpdateJobAreaRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/jobs/{jobId}/areas/{areaId}")
        {
            Content = JsonContent.Create(body)
        };
        return await _fixture.Client.SendAsync(request);
    }
}
