namespace VisionPaint.Models;

public sealed record JobAssignmentDto(
    int PersonId,
    string Name,
    string AssignmentRole,
    DateTimeOffset AssignedAt);

public sealed record JobDetailResponse(
    int Id,
    int CompanyId,
    int? ClientId,
    int? CreatedByPersonId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    DateTimeOffset? ScheduledStartAt,
    DateTimeOffset? ScheduledEndAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<JobAssignmentDto> Assignments);

public sealed record PersonSummaryDto(
    int PersonId,
    string Name,
    string? Email,
    string CompanyRole);

public sealed record ReplaceJobAssignmentsRequest(int[] PersonIds, string AssignmentRole = "crew");

public sealed record JobAssignmentsResponse(int JobId, IReadOnlyList<JobAssignmentDto> Assignments);

public sealed record ActiveTimeEntryDto(
    int TimeEntryId,
    int JobId,
    string JobTitle,
    DateTimeOffset ClockInAt,
    bool OnBreak,
    DateTimeOffset? BreakStartedAt);

public sealed record ClockInRequest(int JobId);

public sealed record ClockOutRequest(string? Notes);

public sealed record ClockOutSummaryDto(int WorkMinutes, int BreakMinutes, DateTimeOffset ClockOutAt);

public sealed record JobTimePersonDto(int PersonId, string Name, int Minutes, bool InProgress);

public sealed record JobTimeSummaryDto(
    int TotalMinutes,
    int ActiveMinutes,
    IReadOnlyList<JobTimePersonDto> ByPerson);

public sealed record JobPhotoDto(
    int Id,
    int JobId,
    string PhotoKind,
    string? Caption,
    DateTimeOffset TakenAt,
    string UploadedByName,
    string Url);
