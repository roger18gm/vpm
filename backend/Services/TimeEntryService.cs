using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class TimeEntryService
{
    private readonly AppDbContext _db;
    private readonly IJobAccessService _jobAccess;
    private readonly ICompanyAuthorizationService _companyAuthorization;

    public TimeEntryService(
        AppDbContext db,
        IJobAccessService jobAccess,
        ICompanyAuthorizationService companyAuthorization)
    {
        _db = db;
        _jobAccess = jobAccess;
        _companyAuthorization = companyAuthorization;
    }

    public async Task<ActiveTimeEntryDto?> GetActiveAsync(CurrentUserContext user, CancellationToken cancellationToken)
    {
        var entry = await _db.TimeEntries
            .AsNoTracking()
            .Where(e => e.PersonId == user.PersonId && e.ClockOutAt == null)
            .OrderByDescending(e => e.ClockInAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return null;
        }

        var job = await _db.Jobs.AsNoTracking().FirstAsync(j => j.Id == entry.JobId, cancellationToken);
        var openBreak = await _db.TimeBreaks
            .AsNoTracking()
            .Where(b => b.TimeEntryId == entry.Id && b.BreakEndAt == null)
            .OrderByDescending(b => b.BreakStartAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new ActiveTimeEntryDto(
            entry.Id,
            entry.JobId,
            job.Title,
            entry.ClockInAt,
            openBreak is not null,
            openBreak?.BreakStartAt);
    }

    public async Task<(ActiveTimeEntryDto? Result, string? Error, int StatusCode)> ClockInAsync(
        CurrentUserContext user,
        int jobId,
        CancellationToken cancellationToken)
    {
        if (!await _jobAccess.CanClockInAsync(jobId, user, cancellationToken))
        {
            var exists = await _jobAccess.GetCompanyJobAsync(jobId, user, cancellationToken);
            return (null, exists is null ? "Job not found." : "You cannot clock in on this job.", exists is null ? 404 : 403);
        }

        var hasOpen = await _db.TimeEntries.AnyAsync(
            e => e.PersonId == user.PersonId && e.ClockOutAt == null,
            cancellationToken);

        if (hasOpen)
        {
            return (null, "You are already clocked in.", 409);
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new TimeEntry
        {
            JobId = jobId,
            PersonId = user.PersonId,
            ClockInAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            _db.TimeEntries.Add(entry);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return (null, "You are already clocked in.", 409);
        }

        var job = await _db.Jobs.AsNoTracking().FirstAsync(j => j.Id == jobId, cancellationToken);
        return (new ActiveTimeEntryDto(entry.Id, jobId, job.Title, entry.ClockInAt, false, null), null, 200);
    }

    public async Task<(ClockOutSummaryDto? Result, string? Error, int StatusCode)> ClockOutAsync(
        CurrentUserContext user,
        string? notes,
        CancellationToken cancellationToken)
    {
        var entry = await _db.TimeEntries
            .FirstOrDefaultAsync(e => e.PersonId == user.PersonId && e.ClockOutAt == null, cancellationToken);

        if (entry is null)
        {
            return (null, "You are not clocked in.", 409);
        }

        var now = DateTimeOffset.UtcNow;
        await CloseOpenBreaksAsync(entry.Id, now, cancellationToken);
        var breakMinutes = await SumBreakMinutesAsync(entry.Id, cancellationToken);

        entry.ClockOutAt = now;
        entry.BreakMinutes = breakMinutes;
        entry.Notes = notes;
        entry.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        var totalMinutes = (int)Math.Max(0, (entry.ClockOutAt.Value - entry.ClockInAt).TotalMinutes);
        var workMinutes = Math.Max(0, totalMinutes - breakMinutes);

        return (new ClockOutSummaryDto(workMinutes, breakMinutes, now), null, 200);
    }

    public async Task<(bool Success, string? Error, int StatusCode)> StartBreakAsync(
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        var entry = await GetOpenEntryTrackedAsync(user.PersonId, cancellationToken);
        if (entry is null)
        {
            return (false, "You are not clocked in.", 409);
        }

        var hasOpenBreak = await _db.TimeBreaks.AnyAsync(
            b => b.TimeEntryId == entry.Id && b.BreakEndAt == null,
            cancellationToken);

        if (hasOpenBreak)
        {
            return (false, "You are already on break.", 409);
        }

        _db.TimeBreaks.Add(new TimeBreak
        {
            TimeEntryId = entry.Id,
            BreakStartAt = DateTimeOffset.UtcNow,
            BreakType = "rest"
        });
        await _db.SaveChangesAsync(cancellationToken);
        return (true, null, 204);
    }

    public async Task<(bool Success, string? Error, int StatusCode)> EndBreakAsync(
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        var entry = await GetOpenEntryTrackedAsync(user.PersonId, cancellationToken);
        if (entry is null)
        {
            return (false, "You are not clocked in.", 409);
        }

        var openBreak = await _db.TimeBreaks
            .Where(b => b.TimeEntryId == entry.Id && b.BreakEndAt == null)
            .OrderByDescending(b => b.BreakStartAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (openBreak is null)
        {
            return (false, "You are not on break.", 409);
        }

        openBreak.BreakEndAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return (true, null, 204);
    }

    public async Task<JobTimeSummaryDto?> GetJobSummaryAsync(
        int jobId,
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        if (!await _jobAccess.CanViewJobAsync(jobId, user, cancellationToken))
        {
            return null;
        }

        var entries = await _db.TimeEntries
            .AsNoTracking()
            .Where(e => e.JobId == jobId)
            .ToListAsync(cancellationToken);

        var entryIds = entries.Select(e => e.Id).ToList();
        var breaksByEntryId = await _db.TimeBreaks
            .AsNoTracking()
            .Where(b => entryIds.Contains(b.TimeEntryId))
            .GroupBy(b => b.TimeEntryId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList(), cancellationToken);

        var personIds = entries.Select(e => e.PersonId).Distinct().ToList();
        var people = await _db.People
            .AsNoTracking()
            .Where(p => personIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var byPerson = new List<JobTimePersonDto>();
        var totalMinutes = 0;
        var activeMinutes = 0;

        foreach (var group in entries.GroupBy(e => e.PersonId))
        {
            var totalPersonMinutes = 0;
            var inProgress = false;

            foreach (var entry in group)
            {
                var breaks = breaksByEntryId.GetValueOrDefault(entry.Id);
                var entryMinutes = ComputeEntryMinutes(entry, breaks, now);
                if (entry.ClockOutAt is null)
                {
                    inProgress = true;
                    activeMinutes += entryMinutes.WorkMinutes;
                }

                totalPersonMinutes += entryMinutes.WorkMinutes;
            }

            totalMinutes += totalPersonMinutes;
            byPerson.Add(new JobTimePersonDto(
                group.Key,
                people.GetValueOrDefault(group.Key, "Unknown"),
                totalPersonMinutes,
                inProgress));
        }

        return new JobTimeSummaryDto(totalMinutes, activeMinutes, byPerson);
    }

    public async Task<(WeeklyTimesheetDto? Result, string? Error, int StatusCode)> GetWeeklyTimesheetAsync(
        CurrentUserContext user,
        DateOnly? weekStartParam,
        int? personIdParam,
        CancellationToken cancellationToken)
    {
        var targetPersonId = personIdParam ?? user.PersonId;
        if (targetPersonId != user.PersonId && !_companyAuthorization.IsManager(user))
        {
            return (null, "You cannot view this timesheet.", 403);
        }

        var company = await _db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == user.CompanyId, cancellationToken);
        if (company is null)
        {
            return (null, "Company not found.", 404);
        }

        var timeZone = TimeZoneHelper.Resolve(company.Timezone);
        var weekStart = weekStartParam ?? TimeZoneHelper.SundayOfWeek(
            TimeZoneHelper.ToLocalDate(DateTimeOffset.UtcNow, timeZone));

        if (!TimeZoneHelper.IsSunday(weekStart))
        {
            return (null, "weekStart must be a Sunday (YYYY-MM-DD).", 400);
        }

        var memberExists = await _db.CompanyMembers.AsNoTracking().AnyAsync(
            m => m.CompanyId == user.CompanyId
                && m.PersonId == targetPersonId
                && m.Status == "active",
            cancellationToken);
        if (!memberExists)
        {
            return (null, "Person not found.", 404);
        }

        var personName = await _db.People
            .AsNoTracking()
            .Where(p => p.Id == targetPersonId)
            .Select(p => p.Name)
            .FirstAsync(cancellationToken);

        var weekEnd = weekStart.AddDays(6);
        var queryFrom = TimeZoneHelper.ToUtcStartOfDay(weekStart, timeZone).AddDays(-1);
        var queryTo = TimeZoneHelper.ToUtcEndOfDay(weekEnd, timeZone).AddDays(1);

        var entries = await _db.TimeEntries
            .AsNoTracking()
            .Where(e => e.PersonId == targetPersonId
                && e.ClockInAt >= queryFrom
                && e.ClockInAt <= queryTo)
            .ToListAsync(cancellationToken);

        var weekDays = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToHashSet();
        entries = entries
            .Where(e => weekDays.Contains(TimeZoneHelper.ToLocalDate(e.ClockInAt, timeZone)))
            .ToList();

        var entryIds = entries.Select(e => e.Id).ToList();
        var breaksByEntryId = await _db.TimeBreaks
            .AsNoTracking()
            .Where(b => entryIds.Contains(b.TimeEntryId))
            .GroupBy(b => b.TimeEntryId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList(), cancellationToken);

        var jobIds = entries.Select(e => e.JobId).Distinct().ToList();
        var jobsById = await _db.Jobs
            .AsNoTracking()
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Title, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var sessionsByDate = new Dictionary<DateOnly, List<WeeklyTimesheetSessionDto>>();

        foreach (var entry in entries.OrderBy(e => e.ClockInAt))
        {
            var localDate = TimeZoneHelper.ToLocalDate(entry.ClockInAt, timeZone);
            var breaks = breaksByEntryId.GetValueOrDefault(entry.Id) ?? [];
            var minutes = ComputeEntryMinutes(entry, breaks, now);
            var session = new WeeklyTimesheetSessionDto(
                entry.Id,
                entry.JobId,
                jobsById.GetValueOrDefault(entry.JobId, "Unknown"),
                entry.ClockInAt,
                entry.ClockOutAt,
                minutes.WorkMinutes,
                minutes.BreakMinutes,
                entry.ClockOutAt is null,
                MapBreakWindows(breaks, now));

            if (!sessionsByDate.TryGetValue(localDate, out var list))
            {
                list = [];
                sessionsByDate[localDate] = list;
            }

            list.Add(session);
        }

        var days = new List<WeeklyTimesheetDayDto>();
        var weekWork = 0;
        var weekBreak = 0;

        for (var i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var sessions = sessionsByDate.GetValueOrDefault(date) ?? [];
            var workMinutes = sessions.Sum(s => s.WorkMinutes);
            var breakMinutes = sessions.Sum(s => s.BreakMinutes);
            weekWork += workMinutes;
            weekBreak += breakMinutes;

            days.Add(new WeeklyTimesheetDayDto(
                date.ToString("yyyy-MM-dd"),
                TimeZoneHelper.FormatDayLabel(date),
                workMinutes,
                breakMinutes,
                sessions));
        }

        var result = new WeeklyTimesheetDto(
            targetPersonId,
            personName,
            weekStart.ToString("yyyy-MM-dd"),
            company.Timezone,
            weekWork,
            weekBreak,
            days);

        return (result, null, 200);
    }

    private sealed record EntryMinutes(int WorkMinutes, int BreakMinutes);

    private static EntryMinutes ComputeEntryMinutes(
        TimeEntry entry,
        IReadOnlyList<TimeBreak>? breaks,
        DateTimeOffset now)
    {
        var breakMinutes = SumBreakMinutes(breaks, now);
        if (entry.ClockOutAt is null)
        {
            var partial = (int)Math.Max(0, (now - entry.ClockInAt).TotalMinutes);
            return new EntryMinutes(Math.Max(0, partial - breakMinutes), breakMinutes);
        }

        var span = (int)Math.Max(0, (entry.ClockOutAt.Value - entry.ClockInAt).TotalMinutes);
        return new EntryMinutes(Math.Max(0, span - entry.BreakMinutes), entry.BreakMinutes);
    }

    private static IReadOnlyList<TimeBreakWindowDto> MapBreakWindows(
        IReadOnlyList<TimeBreak> breaks,
        DateTimeOffset now)
    {
        return breaks
            .OrderBy(b => b.BreakStartAt)
            .Select(b =>
            {
                var end = b.BreakEndAt ?? now;
                var minutes = (int)Math.Max(0, (end - b.BreakStartAt).TotalMinutes);
                return new TimeBreakWindowDto(b.Id, b.BreakStartAt, b.BreakEndAt, b.BreakType, minutes);
            })
            .ToList();
    }

    private async Task<TimeEntry?> GetOpenEntryTrackedAsync(int personId, CancellationToken cancellationToken)
    {
        return await _db.TimeEntries
            .FirstOrDefaultAsync(e => e.PersonId == personId && e.ClockOutAt == null, cancellationToken);
    }

    private async Task CloseOpenBreaksAsync(int timeEntryId, DateTimeOffset endAt, CancellationToken cancellationToken)
    {
        var openBreaks = await _db.TimeBreaks
            .Where(b => b.TimeEntryId == timeEntryId && b.BreakEndAt == null)
            .ToListAsync(cancellationToken);

        foreach (var breakEntry in openBreaks)
        {
            breakEntry.BreakEndAt = endAt;
        }

        if (openBreaks.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<int> SumBreakMinutesAsync(int timeEntryId, CancellationToken cancellationToken)
    {
        var breaks = await _db.TimeBreaks
            .AsNoTracking()
            .Where(b => b.TimeEntryId == timeEntryId && b.BreakEndAt != null)
            .ToListAsync(cancellationToken);

        return breaks.Sum(b => (int)Math.Max(0, (b.BreakEndAt!.Value - b.BreakStartAt).TotalMinutes));
    }

    private static int SumBreakMinutes(IReadOnlyList<TimeBreak>? breaks, DateTimeOffset now)
    {
        if (breaks is null)
        {
            return 0;
        }

        return breaks.Sum(b => (int)Math.Max(0, ((b.BreakEndAt ?? now) - b.BreakStartAt).TotalMinutes));
    }
}
