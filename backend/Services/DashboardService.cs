using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class DashboardService
{
    private readonly AppDbContext _db;
    private readonly ICompanyAuthorizationService _companyAuthorization;

    public DashboardService(AppDbContext db, ICompanyAuthorizationService companyAuthorization)
    {
        _db = db;
        _companyAuthorization = companyAuthorization;
    }

    public async Task<(DashboardSummaryDto? Result, string? Error, int StatusCode)> GetSummaryAsync(
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        if (!_companyAuthorization.IsManager(user))
        {
            return (null, "You cannot view the dashboard summary.", 403);
        }

        var company = await _db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == user.CompanyId, cancellationToken);
        if (company is null)
        {
            return (null, "Company not found.", 404);
        }

        var timeZone = TimeZoneHelper.Resolve(company.Timezone);
        var today = TimeZoneHelper.ToLocalDate(DateTimeOffset.UtcNow, timeZone);
        var weekStart = TimeZoneHelper.SundayOfWeek(today);
        var weekEnd = weekStart.AddDays(6);
        var weekDays = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToHashSet();

        var queryFrom = TimeZoneHelper.ToUtcStartOfDay(weekStart, timeZone).AddDays(-1);
        var queryTo = TimeZoneHelper.ToUtcEndOfDay(weekEnd, timeZone).AddDays(1);

        var entries = await _db.TimeEntries
            .AsNoTracking()
            .Where(e => e.ClockInAt >= queryFrom && e.ClockInAt <= queryTo)
            .Join(
                _db.Jobs.AsNoTracking(),
                entry => entry.JobId,
                job => job.Id,
                (entry, job) => new { entry, job })
            .Where(x => x.job.CompanyId == user.CompanyId)
            .Select(x => x.entry)
            .ToListAsync(cancellationToken);

        entries = entries
            .Where(e => weekDays.Contains(TimeZoneHelper.ToLocalDate(e.ClockInAt, timeZone)))
            .ToList();

        var entryIds = entries.Select(e => e.Id).ToList();
        var breaksByEntryId = await _db.TimeBreaks
            .AsNoTracking()
            .Where(b => entryIds.Contains(b.TimeEntryId))
            .GroupBy(b => b.TimeEntryId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList(), cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var hoursThisWeekMinutes = entries.Sum(entry =>
        {
            var breaks = breaksByEntryId.GetValueOrDefault(entry.Id);
            return ComputeWorkMinutes(entry, breaks, now);
        });

        var weekStartUtc = TimeZoneHelper.ToUtcStartOfDay(weekStart, timeZone);
        var weekEndUtc = TimeZoneHelper.ToUtcEndOfDay(weekEnd, timeZone);

        var completedJobs = await _db.Jobs
            .AsNoTracking()
            .Where(j => j.CompanyId == user.CompanyId
                && j.Status == "completed"
                && j.CompletedAt != null
                && j.CompletedAt >= weekStartUtc
                && j.CompletedAt <= weekEndUtc)
            .Select(j => j.CompletedAt!.Value)
            .ToListAsync(cancellationToken);

        var completedThisWeekCount = completedJobs.Count(completedAt =>
            weekDays.Contains(TimeZoneHelper.ToLocalDate(completedAt, timeZone)));

        var openEntries = await _db.TimeEntries
            .AsNoTracking()
            .Where(e => e.ClockOutAt == null)
            .Join(
                _db.Jobs.AsNoTracking(),
                entry => entry.JobId,
                job => job.Id,
                (entry, job) => new { entry, job })
            .Where(x => x.job.CompanyId == user.CompanyId)
            .Select(x => new
            {
                x.entry.Id,
                x.entry.PersonId,
                x.entry.JobId,
                x.job.Title,
                x.entry.ClockInAt
            })
            .OrderBy(x => x.ClockInAt)
            .ToListAsync(cancellationToken);

        var openEntryIds = openEntries.Select(e => e.Id).ToList();
        var openBreakEntryIds = await _db.TimeBreaks
            .AsNoTracking()
            .Where(b => openEntryIds.Contains(b.TimeEntryId) && b.BreakEndAt == null)
            .Select(b => b.TimeEntryId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var onBreakSet = openBreakEntryIds.ToHashSet();

        var personIds = openEntries.Select(e => e.PersonId).Distinct().ToList();
        var people = await _db.People
            .AsNoTracking()
            .Where(p => personIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var clockedInWorkers = openEntries
            .Select(e => new ClockedInWorkerDto(
                e.PersonId,
                people.GetValueOrDefault(e.PersonId, "Unknown"),
                e.JobId,
                e.Title,
                e.ClockInAt,
                onBreakSet.Contains(e.Id)))
            .ToList();

        return (new DashboardSummaryDto(hoursThisWeekMinutes, completedThisWeekCount, clockedInWorkers), null, 200);
    }

    private static int ComputeWorkMinutes(
        TimeEntry entry,
        IReadOnlyList<TimeBreak>? breaks,
        DateTimeOffset now)
    {
        var breakMinutes = SumBreakMinutes(breaks, now);
        if (entry.ClockOutAt is null)
        {
            var partial = (int)Math.Max(0, (now - entry.ClockInAt).TotalMinutes);
            return Math.Max(0, partial - breakMinutes);
        }

        var span = (int)Math.Max(0, (entry.ClockOutAt.Value - entry.ClockInAt).TotalMinutes);
        return Math.Max(0, span - entry.BreakMinutes);
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
