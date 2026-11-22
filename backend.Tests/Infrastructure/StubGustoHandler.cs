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

    public Exception? TokenFailure { get; set; }

    public string? TokenBody { get; set; }

    public string? TokenContentType { get; set; }

    public Exception? TimeSheetFailure { get; set; }

    public string? TimeSheetSuccessBody { get; set; }

    public Queue<string> EmployeePageBodies { get; } = new();

    public Queue<int> EmployeeStatuses { get; } = new();

    public List<string> EmployeeRequestUris { get; } = [];

    public List<string?> EmployeeAuthorization { get; } = [];

    public List<string?> EmployeeApiVersions { get; } = [];

    public Exception? EmployeeFailure { get; set; }

    public Dictionary<string, string> JobBodies { get; } = new(StringComparer.Ordinal);

    public string DefaultJobBody { get; set; } = "[]";

    public Queue<int> JobStatuses { get; } = new();

    public List<string> JobRequestUris { get; } = [];

    public List<string?> JobAuthorization { get; } = [];

    public List<string?> JobApiVersions { get; } = [];

    public Exception? JobFailure { get; set; }

    public List<string> FlowBodies { get; } = [];

    public List<string> FlowRequestUris { get; } = [];

    public List<string?> FlowAuthorization { get; } = [];

    public List<string?> FlowApiVersions { get; } = [];

    public Queue<int> FlowStatuses { get; } = new();

    public string FlowBody { get; set; } = """{"url":"https://flows.gusto-demo.com/flows/stub"}""";

    public Exception? FlowFailure { get; set; }

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
            TokenFailure = null;
            TokenBody = null;
            TokenContentType = null;
            TimeSheetFailure = null;
            TimeSheetSuccessBody = null;
            EmployeePageBodies.Clear();
            EmployeeStatuses.Clear();
            EmployeeRequestUris.Clear();
            EmployeeAuthorization.Clear();
            EmployeeApiVersions.Clear();
            EmployeeFailure = null;
            JobBodies.Clear();
            DefaultJobBody = "[]";
            JobStatuses.Clear();
            JobRequestUris.Clear();
            JobAuthorization.Clear();
            JobApiVersions.Clear();
            JobFailure = null;
            FlowBodies.Clear();
            FlowRequestUris.Clear();
            FlowAuthorization.Clear();
            FlowApiVersions.Clear();
            FlowStatuses.Clear();
            FlowBody = """{"url":"https://flows.gusto-demo.com/flows/stub"}""";
            FlowFailure = null;
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

            if (TokenFailure is not null)
            {
                throw TokenFailure;
            }

            var tokenBody = TokenBody ?? """{"access_token":"token-1","token_type":"Bearer","expires_in":7200}""";
            var tokenMediaType = TokenContentType ?? "application/json";
            return Json(HttpStatusCode.OK, tokenBody, tokenMediaType);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/employees", StringComparison.Ordinal))
        {
            return DirectoryResponse(
                request,
                EmployeeRequestUris,
                EmployeeAuthorization,
                EmployeeApiVersions,
                EmployeeStatuses,
                EmployeePageBodies,
                "[]",
                EmployeeFailure);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/jobs", StringComparison.Ordinal))
        {
            var uuid = EmployeeUuidFromJobPath(path);
            var fallback = DefaultJobBody;
            if (uuid is not null && JobBodies.TryGetValue(uuid, out var specific))
            {
                fallback = specific;
            }

            return DirectoryResponse(
                request,
                JobRequestUris,
                JobAuthorization,
                JobApiVersions,
                JobStatuses,
                bodies: null,
                fallback,
                JobFailure);
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/flows", StringComparison.Ordinal))
        {
            var flowRequestBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            int flowStatus;
            Exception? flowFailure;
            string flowResponseBody;
            lock (_gate)
            {
                FlowBodies.Add(flowRequestBody);
                FlowRequestUris.Add(request.RequestUri?.ToString() ?? "");
                FlowAuthorization.Add(request.Headers.Authorization?.ToString());
                FlowApiVersions.Add(Header(request, "X-Gusto-API-Version"));
                flowStatus = FlowStatuses.Count > 0 ? FlowStatuses.Dequeue() : 201;
                flowFailure = FlowFailure;
                flowResponseBody = FlowBody;
            }

            if (flowFailure is not null)
            {
                throw flowFailure;
            }

            return Json((HttpStatusCode)flowStatus, flowResponseBody);
        }

        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);
        int status;
        Exception? failure;
        string? successBody;
        lock (_gate)
        {
            TimeSheetBodies.Add(body);
            TimeSheetAuthorization.Add(request.Headers.Authorization?.ToString());
            TimeSheetApiVersions.Add(Header(request, "X-Gusto-API-Version"));
            status = TimeSheetStatuses.Count > 0 ? TimeSheetStatuses.Dequeue() : 201;
            failure = TimeSheetFailure;
            successBody = TimeSheetSuccessBody;
        }

        OnTimeSheet?.Invoke(body);

        if (failure is not null)
        {
            throw failure;
        }

        if (status == 201)
        {
            return Json(HttpStatusCode.Created, successBody ?? """{"uuid":"ts-from-stub"}""");
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

    private HttpResponseMessage DirectoryResponse(
        HttpRequestMessage request,
        List<string> uris,
        List<string?> authorization,
        List<string?> apiVersions,
        Queue<int> statuses,
        Queue<string>? bodies,
        string fallbackBody,
        Exception? failure)
    {
        int status;
        string body;
        lock (_gate)
        {
            uris.Add(request.RequestUri?.ToString() ?? "");
            authorization.Add(request.Headers.Authorization?.ToString());
            apiVersions.Add(Header(request, "X-Gusto-API-Version"));
            status = statuses.Count > 0 ? statuses.Dequeue() : 200;
            body = bodies is not null && bodies.Count > 0 ? bodies.Dequeue() : fallbackBody;
        }

        if (failure is not null)
        {
            throw failure;
        }

        if (status == 401)
        {
            return Json(HttpStatusCode.Unauthorized, """{"message":"unauthorized"}""");
        }

        if (status is >= 200 and < 300)
        {
            return Json((HttpStatusCode)status, body);
        }

        return Json((HttpStatusCode)status, """{"message":"gusto failed"}""");
    }

    private static string? EmployeeUuidFromJobPath(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4
            && parts[0] == "v1"
            && parts[1] == "employees"
            && parts[3] == "jobs")
        {
            return parts[2];
        }

        return null;
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string json,
        string mediaType = "application/json") =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, mediaType)
        };
}
