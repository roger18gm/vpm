namespace VisionPaint.Services;

public interface IGustoTimeClient
{
    Task PrepareAsync(CancellationToken cancellationToken);

    Task<GustoTimeSheetResult> PostTimeSheetAsync(
        GustoTimeSheetRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GustoDirectoryEmployee>> ListEmployeesAsync(CancellationToken cancellationToken);

    Task<string> CreateFlowAsync(string flowType, CancellationToken cancellationToken);
}

public sealed record GustoTimeSheetRequest(
    string EmployeeUuid,
    string CompensationJobUuid,
    string TimeZone,
    DateTimeOffset ShiftStartedAt,
    DateTimeOffset ShiftEndedAt,
    decimal HoursWorked,
    int TimeEntryId,
    string JobTitle);

public sealed record GustoTimeSheetResult(
    bool Accepted,
    string? TimeSheetUuid,
    int StatusCode,
    string? ErrorMessage,
    bool StopBatch);

public sealed record GustoDirectoryJob(string Uuid, string Title, bool Primary);

public sealed record GustoDirectoryEmployee(
    string Uuid,
    string FirstName,
    string LastName,
    string Email,
    IReadOnlyList<GustoDirectoryJob> Jobs);
