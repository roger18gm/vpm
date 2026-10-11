using Sentry;
using Sentry.Extensibility;

namespace VisionPaint.Services;

public sealed class ApiRequestSentryEventProcessor : ISentryEventProcessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ApiRequestSentryEventProcessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public SentryEvent Process(SentryEvent @event)
    {
        var path = _httpContextAccessor.HttpContext?.Request.Path.Value;
        if (!string.IsNullOrWhiteSpace(path))
        {
            @event.SetTag("request_path", path);
        }

        SentryEventScrubber.Scrub(@event);
        return @event;
    }
}
