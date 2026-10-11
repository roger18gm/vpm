using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Sentry;

namespace VisionPaint.Services;

public static class SentryEventScrubber
{
    public const string Filtered = "[Filtered]";

    public static void Scrub(SentryEvent sentryEvent)
    {
        if (sentryEvent.Request is { } request)
        {
            request.Cookies = null;
            request.Data = null;
            if (request.Headers is not null)
            {
                foreach (var key in request.Headers.Keys.Where(IsSensitiveKey).ToArray())
                {
                    request.Headers.Remove(key);
                }
            }

            if (!string.IsNullOrEmpty(request.QueryString))
            {
                request.QueryString = ScrubQueryString(request.QueryString);
            }
        }

        foreach (var key in sentryEvent.Extra.Keys.Where(IsSensitiveKey).ToArray())
        {
            sentryEvent.SetExtra(key, Filtered);
        }
    }

    public static bool IsSensitiveKey(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || key.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            || key.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || key.Contains("api-key", StringComparison.OrdinalIgnoreCase)
            || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || key.Contains("credential", StringComparison.OrdinalIgnoreCase);
    }

    private static string ScrubQueryString(string queryString)
    {
        var hadQuestionMark = queryString.StartsWith('?');
        var parsed = QueryHelpers.ParseQuery(hadQuestionMark ? queryString : "?" + queryString);
        var builder = new StringBuilder();
        foreach (var pair in parsed)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            var value = IsSensitiveKey(pair.Key) ? Filtered : pair.Value.ToString();
            builder.Append(Uri.EscapeDataString(value));
        }

        var scrubbed = builder.ToString();
        return hadQuestionMark ? "?" + scrubbed : scrubbed;
    }
}
