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

public sealed record JobListItemResponse(
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
    int PhotoCount);

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

public sealed record CreateTimeEntryRequest(
    int JobId,
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    int? PersonId,
    int BreakMinutes = 0,
    string? Notes = null);

public sealed record UpdateTimeEntryRequest(
    int JobId,
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    int BreakMinutes = 0,
    string? Notes = null);

public sealed record TimeBreakWindowDto(
    int Id,
    DateTimeOffset BreakStartAt,
    DateTimeOffset? BreakEndAt,
    string BreakType,
    int Minutes);

public sealed record WeeklyTimesheetSessionDto(
    int TimeEntryId,
    int JobId,
    string JobTitle,
    DateTimeOffset ClockInAt,
    DateTimeOffset? ClockOutAt,
    int WorkMinutes,
    int BreakMinutes,
    bool InProgress,
    IReadOnlyList<TimeBreakWindowDto> Breaks);

public sealed record WeeklyTimesheetDayDto(
    string Date,
    string DayLabel,
    int WorkMinutes,
    int BreakMinutes,
    IReadOnlyList<WeeklyTimesheetSessionDto> Sessions);

public sealed record WeeklyTimesheetDto(
    int PersonId,
    string PersonName,
    string WeekStartDate,
    string TimezoneId,
    int WeekTotalWorkMinutes,
    int WeekTotalBreakMinutes,
    IReadOnlyList<WeeklyTimesheetDayDto> Days);

public sealed record ClockedInWorkerDto(
    int PersonId,
    string Name,
    int JobId,
    string JobTitle,
    DateTimeOffset ClockInAt,
    bool OnBreak);

public sealed record DashboardSummaryDto(
    int HoursThisWeekMinutes,
    int CompletedThisWeekCount,
    IReadOnlyList<ClockedInWorkerDto> ClockedInWorkers);

public sealed record JobTimePersonDto(int PersonId, string Name, int Minutes, bool InProgress);

public sealed record JobTimeSummaryDto(
    int TotalMinutes,
    int ActiveMinutes,
    IReadOnlyList<JobTimePersonDto> ByPerson);

public sealed record JobStatusHistoryDto(
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt,
    string ChangedByName,
    string? Reason);

public sealed record JobPhotoDto(
    int Id,
    int JobId,
    string PhotoKind,
    string? Caption,
    DateTimeOffset TakenAt,
    string UploadedByName,
    string Url);

public sealed record UserAdminDto(
    int PersonId,
    Guid AuthUserId,
    string Name,
    string Email,
    string CompanyRole,
    string MembershipStatus,
    bool IsActive,
    bool IsClockedIn,
    string? ClockedInJobTitle,
    DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest(
    string Name,
    string Email,
    string Password,
    string CompanyRole);

public sealed record UpdateUserRoleRequest(string CompanyRole);
