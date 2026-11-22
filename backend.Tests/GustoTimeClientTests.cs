using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VisionPaint.Services;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests;

public sealed class GustoTimeClientTests
{
    private static GustoOptions DemoOptions(string baseUrl = "https://api.gusto-demo.com") => new()
    {
        BaseUrl = baseUrl,
        ClientId = "client-id",
        ClientSecret = "client-secret",
        RefreshToken = "refresh-token",
        CompanyUuid = "company-uuid",
        ApiVersion = "2026-06-15"
    };

    private static GustoTimeSheetRequest SampleRequest() => new(
        "emp-1",
        "comp-job-1",
        "America/Denver",
        DateTimeOffset.Parse("2026-10-19T14:00:00Z"),
        DateTimeOffset.Parse("2026-10-19T18:00:00Z"),
        4.0000m,
        42,
        "North house");

    [Fact]
    public async Task Post_sends_snake_case_regular_hours_and_demo_headers()
    {
        var handler = new StubGustoHandler();
        var client = CreateClient(handler, DemoOptions());

        var result = await client.PostTimeSheetAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("ts-from-stub", result.TimeSheetUuid);
        Assert.Equal(1, handler.TokenRequests);
        var body = JsonDocument.Parse(handler.TimeSheetBodies.Single());
        Assert.Equal("emp-1", body.RootElement.GetProperty("entity_uuid").GetString());
        Assert.Equal("Employee", body.RootElement.GetProperty("entity_type").GetString());
        Assert.Equal("comp-job-1", body.RootElement.GetProperty("job_uuid").GetString());
        Assert.Equal("America/Denver", body.RootElement.GetProperty("time_zone").GetString());
        Assert.Equal(4.0m, body.RootElement.GetProperty("entries")[0].GetProperty("hours_worked").GetDecimal());
        Assert.Equal("Regular", body.RootElement.GetProperty("entries")[0].GetProperty("pay_classification").GetString());
        Assert.Equal("42", body.RootElement.GetProperty("metadata").GetProperty("visionpaint_time_entry_id").GetString());
        Assert.Equal("North house", body.RootElement.GetProperty("metadata").GetProperty("job_title").GetString());
        Assert.Equal("Bearer token-1", handler.TimeSheetAuthorization.Single());
        Assert.Equal("2026-06-15", handler.TimeSheetApiVersions.Single());
    }

    [Fact]
    public async Task Prepare_skips_refresh_when_more_than_five_minutes_remain()
    {
        var handler = new StubGustoHandler();
        var cache = new GustoAccessTokenCache();
        cache.Set("still-valid", DateTimeOffset.UtcNow.AddMinutes(6));
        var client = CreateClient(handler, DemoOptions(), cache);

        await client.PrepareAsync(CancellationToken.None);

        Assert.Equal(0, handler.TokenRequests);
    }

    [Fact]
    public async Task Prepare_refreshes_when_fewer_than_five_minutes_remain()
    {
        var handler = new StubGustoHandler();
        var cache = new GustoAccessTokenCache();
        cache.Set("almost-expired", DateTimeOffset.UtcNow.AddMinutes(4));
        var client = CreateClient(handler, DemoOptions(), cache);

        await client.PrepareAsync(CancellationToken.None);

        Assert.Equal(1, handler.TokenRequests);
    }

    [Fact]
    public async Task Post_refreshes_once_after_401_and_retries()
    {
        var handler = new StubGustoHandler();
        handler.TimeSheetStatuses.Enqueue(401);
        handler.TimeSheetStatuses.Enqueue(201);
        var client = CreateClient(handler, DemoOptions());

        var result = await client.PostTimeSheetAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.False(result.StopBatch);
        Assert.Equal(2, handler.TokenRequests);
        Assert.Equal(2, handler.TimeSheetBodies.Count);
    }

    [Fact]
    public async Task Post_second_401_returns_stop_batch()
    {
        var handler = new StubGustoHandler();
        handler.TimeSheetStatuses.Enqueue(401);
        handler.TimeSheetStatuses.Enqueue(401);
        var client = CreateClient(handler, DemoOptions());

        var result = await client.PostTimeSheetAsync(SampleRequest(), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.True(result.StopBatch);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal(2, handler.TimeSheetBodies.Count);
    }

    [Fact]
    public async Task Prepare_rejects_a_non_demo_host_before_http()
    {
        var handler = new StubGustoHandler();
        var client = CreateClient(handler, DemoOptions("https://api.gusto.com"));

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.PrepareAsync(CancellationToken.None));
        Assert.Equal(0, handler.TokenRequests);
    }

    [Fact]
    public async Task Prepare_throws_when_the_token_call_fails_to_connect()
    {
        var handler = new StubGustoHandler();
        handler.TokenFailure = new HttpRequestException("connection refused");
        var client = CreateClient(handler, DemoOptions());

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.PrepareAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_throws_when_the_token_body_is_not_json()
    {
        var handler = new StubGustoHandler();
        handler.TokenBody = "not-json";
        handler.TokenContentType = "text/plain";
        var client = CreateClient(handler, DemoOptions());

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.PrepareAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_connection_failure_is_not_accepted_and_does_not_stop_the_batch()
    {
        var handler = new StubGustoHandler();
        handler.TimeSheetFailure = new HttpRequestException("connection refused");
        var client = CreateClient(handler, DemoOptions());

        var result = await client.PostTimeSheetAsync(SampleRequest(), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.False(result.StopBatch);
        Assert.Equal("The Gusto request could not connect.", result.ErrorMessage);
    }

    [Fact]
    public async Task Post_non_json_success_is_accepted_with_a_null_uuid()
    {
        var handler = new StubGustoHandler();
        handler.TimeSheetSuccessBody = "not-json";
        var client = CreateClient(handler, DemoOptions());

        var result = await client.PostTimeSheetAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Null(result.TimeSheetUuid);
        Assert.False(result.StopBatch);
        Assert.Equal((int)HttpStatusCode.Created, result.StatusCode);
    }

    [Fact]
    public async Task Prepare_rejects_missing_secret_before_http()
    {
        var handler = new StubGustoHandler();
        var options = DemoOptions();
        options.ClientSecret = "";
        var client = CreateClient(handler, options);

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.PrepareAsync(CancellationToken.None));
        Assert.Equal(0, handler.TokenRequests);
    }

    [Fact]
    public async Task ListEmployees_reads_onboarded_employees_and_jobs_without_pay()
    {
        var handler = new StubGustoHandler();
        handler.EmployeePageBodies.Enqueue(
            """[{"uuid":"emp-1","first_name":"Ada","last_name":"Painter","email":"ada@example.com","jobs":[{"rate":"99.00"}]}]""");
        handler.JobBodies["emp-1"] =
            """[{"uuid":"job-1","title":"Painter","primary":true,"rate":"25.00","payment_unit":"Hour","compensations":[{"rate":"25.00"}]}]""";
        var client = CreateClient(handler, DemoOptions());

        var employees = await client.ListEmployeesAsync(CancellationToken.None);

        var employee = Assert.Single(employees);
        Assert.Equal("emp-1", employee.Uuid);
        Assert.Equal("Ada", employee.FirstName);
        Assert.Equal("Painter", employee.LastName);
        Assert.Equal("ada@example.com", employee.Email);
        var job = Assert.Single(employee.Jobs);
        Assert.Equal("job-1", job.Uuid);
        Assert.Equal("Painter", job.Title);
        Assert.True(job.Primary);
        Assert.Contains("/v1/companies/company-uuid/employees", handler.EmployeeRequestUris.Single());
        Assert.Contains("onboarded_active=true", handler.EmployeeRequestUris.Single());
        Assert.Contains("per=100", handler.EmployeeRequestUris.Single());
        Assert.Contains("/v1/employees/emp-1/jobs", handler.JobRequestUris.Single());
        Assert.Equal("Bearer token-1", handler.EmployeeAuthorization.Single());
        Assert.Equal("2026-06-15", handler.EmployeeApiVersions.Single());
        Assert.Empty(handler.TimeSheetBodies);
    }

    [Fact]
    public async Task ListEmployees_follows_a_full_page()
    {
        var handler = new StubGustoHandler();
        handler.EmployeePageBodies.Enqueue(EmployeePage(100));
        handler.EmployeePageBodies.Enqueue(
            """[{"uuid":"emp-last","first_name":"Zed","last_name":"Last","email":"zed@example.com"}]""");
        handler.DefaultJobBody = """[{"uuid":"job-default","title":"Painter","primary":true}]""";
        var client = CreateClient(handler, DemoOptions());

        var employees = await client.ListEmployeesAsync(CancellationToken.None);

        Assert.Equal(101, employees.Count);
        Assert.Equal(2, handler.EmployeeRequestUris.Count);
        Assert.Equal(101, handler.JobRequestUris.Count);
        Assert.Contains("page=2", handler.EmployeeRequestUris[1]);
    }

    [Fact]
    public async Task ListEmployees_refreshes_once_after_401()
    {
        var handler = new StubGustoHandler();
        handler.EmployeeStatuses.Enqueue(401);
        handler.EmployeeStatuses.Enqueue(200);
        handler.EmployeePageBodies.Enqueue("""[]""");
        handler.EmployeePageBodies.Enqueue(
            """[{"uuid":"emp-1","first_name":"Ada","last_name":"Painter","email":"ada@example.com"}]""");
        handler.DefaultJobBody = """[{"uuid":"job-1","title":"Painter","primary":false}]""";
        var client = CreateClient(handler, DemoOptions());

        var employees = await client.ListEmployeesAsync(CancellationToken.None);

        Assert.Single(employees);
        Assert.Equal(2, handler.TokenRequests);
        Assert.Equal(2, handler.EmployeeRequestUris.Count);
    }

    [Fact]
    public async Task ListEmployees_rejects_a_non_demo_host_before_http()
    {
        var handler = new StubGustoHandler();
        var client = CreateClient(handler, DemoOptions("https://api.gusto.com"));

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.ListEmployeesAsync(CancellationToken.None));
        Assert.Equal(0, handler.TokenRequests);
        Assert.Empty(handler.EmployeeRequestUris);
    }

    [Fact]
    public async Task ListEmployees_throws_when_the_token_call_fails_to_connect()
    {
        var handler = new StubGustoHandler();
        handler.TokenFailure = new HttpRequestException("connection refused");
        var client = CreateClient(handler, DemoOptions());

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.ListEmployeesAsync(CancellationToken.None));
        Assert.Empty(handler.EmployeeRequestUris);
    }

    [Fact]
    public async Task ListEmployees_throws_when_gusto_returns_500()
    {
        var handler = new StubGustoHandler();
        handler.EmployeeStatuses.Enqueue(500);
        var client = CreateClient(handler, DemoOptions());

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.ListEmployeesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateFlow_posts_add_employees_and_returns_the_demo_flow_url()
    {
        var handler = new StubGustoHandler();
        handler.FlowBody = """{"url":"https://flows.gusto-demo.com/flows/abc"}""";
        var client = CreateClient(handler, DemoOptions());

        var url = await client.CreateFlowAsync("add_employees", CancellationToken.None);

        Assert.Equal("https://flows.gusto-demo.com/flows/abc", url);
        Assert.Single(handler.FlowRequestUris);
        Assert.EndsWith("/v1/companies/company-uuid/flows", new Uri(handler.FlowRequestUris[0]).AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"flow_type\":\"add_employees\"", handler.FlowBodies.Single());
        Assert.Equal("Bearer token-1", handler.FlowAuthorization.Single());
        Assert.Equal("2026-06-15", handler.FlowApiVersions.Single());
    }

    [Fact]
    public async Task CreateFlow_refreshes_once_after_401_and_returns_the_second_url()
    {
        var handler = new StubGustoHandler();
        handler.FlowStatuses.Enqueue(401);
        handler.FlowBody = """{"url":"https://flows.gusto-demo.com/flows/retry"}""";
        var client = CreateClient(handler, DemoOptions());

        var url = await client.CreateFlowAsync("company_onboarding", CancellationToken.None);

        Assert.Equal("https://flows.gusto-demo.com/flows/retry", url);
        Assert.Equal(2, handler.TokenRequests);
        Assert.Equal(2, handler.FlowBodies.Count);
    }

    [Fact]
    public async Task CreateFlow_422_throws_not_ready()
    {
        var handler = new StubGustoHandler();
        handler.FlowStatuses.Enqueue(422);
        var client = CreateClient(handler, DemoOptions());

        var error = await Assert.ThrowsAsync<GustoFlowNotReadyException>(() =>
            client.CreateFlowAsync("add_employees", CancellationToken.None));

        Assert.Equal("The Gusto company is not ready for employees.", error.Message);
    }

    [Fact]
    public async Task CreateFlow_rejects_a_url_on_another_host()
    {
        var handler = new StubGustoHandler();
        handler.FlowBody = """{"url":"https://evil.example/flows/abc"}""";
        var client = CreateClient(handler, DemoOptions());

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.CreateFlowAsync("add_employees", CancellationToken.None));
    }

    [Fact]
    public async Task CreateFlow_rejects_a_non_demo_api_host_before_http()
    {
        var handler = new StubGustoHandler();
        var client = CreateClient(handler, DemoOptions("https://api.gusto.com"));

        await Assert.ThrowsAsync<GustoNotConfiguredException>(() =>
            client.CreateFlowAsync("add_employees", CancellationToken.None));
        Assert.Equal(0, handler.TokenRequests);
        Assert.Empty(handler.FlowRequestUris);
    }

    private static string EmployeePage(int count)
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append($$"""{"uuid":"emp-{{i}}","first_name":"A","last_name":"{{i}}","email":"a{{i}}@example.com"}""");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static GustoTimeClient CreateClient(
        StubGustoHandler handler,
        GustoOptions options,
        GustoAccessTokenCache? cache = null)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        return new GustoTimeClient(new SingleHttpClientFactory(http), Options.Create(options), cache ?? new GustoAccessTokenCache());
    }

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
