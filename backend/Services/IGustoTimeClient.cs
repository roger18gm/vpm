namespace VisionPaint.Services;

public interface IGustoTimeClient
{
    Task PrepareAsync(CancellationToken cancellationToken);

    Task<GustoTimeSheetResult> PostTimeSheetAsync(
        GustoTimeSheetRequest request,
        CancellationToken cancellationToken);
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
