using Microsoft.AspNetCore.Http;
using Sentry;
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class SentryEventScrubberTests
{
    [Fact]
    public void Process_tags_request_path_and_removes_secrets()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/jobs/5";
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var processor = new ApiRequestSentryEventProcessor(accessor);
        var request = new SentryRequest
        {
            QueryString = "password=hunter2&status=open"
        };
        request.Headers["Authorization"] = "Bearer secret-token";
        request.Headers["Accept"] = "application/json";
        var sentryEvent = new SentryEvent
        {
            Request = request
        };
        sentryEvent.SetExtra("password", "hunter2");
        sentryEvent.SetExtra("jobId", 5);

        var processed = processor.Process(sentryEvent);

        Assert.Equal("/api/jobs/5", processed.Tags["request_path"]);
        Assert.False(processed.Request.Headers.ContainsKey("Authorization"));
        Assert.Equal("application/json", processed.Request.Headers["Accept"]);
        Assert.DoesNotContain("hunter2", processed.Request.QueryString);
        Assert.Contains("status=open", processed.Request.QueryString, StringComparison.Ordinal);
        Assert.Equal(SentryEventScrubber.Filtered, processed.Extra["password"]);
        Assert.Equal(5, processed.Extra["jobId"]);
    }
}
