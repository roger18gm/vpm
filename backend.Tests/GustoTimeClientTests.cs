using System.Net;
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
