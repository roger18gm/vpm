using System.Net;
using System.Text;

namespace VisionPaint.Tests.Infrastructure;

public sealed class StubGustoHandler : HttpMessageHandler
{
    private readonly object _gate = new();

    public int TokenRequests { get; private set; }

    public List<string> TimeSheetBodies { get; } = [];

    public List<string?> TimeSheetAuthorization { get; } = [];

    public List<string?> TimeSheetApiVersions { get; } = [];

    public Queue<int> TimeSheetStatuses { get; } = new();

    public Action<string>? OnTimeSheet { get; set; }

    public void Reset()
    {
        lock (_gate)
        {
            TokenRequests = 0;
            TimeSheetBodies.Clear();
            TimeSheetAuthorization.Clear();
            TimeSheetApiVersions.Clear();
            TimeSheetStatuses.Clear();
            OnTimeSheet = null;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path.EndsWith("/oauth/token", StringComparison.Ordinal))
        {
            lock (_gate)
            {
                TokenRequests++;
            }

            return Json(HttpStatusCode.OK, """{"access_token":"token-1","token_type":"Bearer","expires_in":7200}""");
        }

        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);
        int status;
        lock (_gate)
        {
            TimeSheetBodies.Add(body);
            TimeSheetAuthorization.Add(request.Headers.Authorization?.ToString());
            TimeSheetApiVersions.Add(
                request.Headers.TryGetValues("X-Gusto-API-Version", out var values)
                    ? values.Single()
                    : null);
            status = TimeSheetStatuses.Count > 0 ? TimeSheetStatuses.Dequeue() : 201;
        }

        OnTimeSheet?.Invoke(body);

        if (status == 201)
        {
            return Json(HttpStatusCode.Created, """{"uuid":"ts-from-stub"}""");
        }

        if (status == 401)
        {
            return Json(HttpStatusCode.Unauthorized, """{"message":"unauthorized"}""");
        }

        return Json((HttpStatusCode)status, """{"message":"invalid time sheet"}""");
    }

    protected override void Dispose(bool disposing)
    {
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
