using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class GustoTimeSyncService
{
    private readonly AppDbContext _db;
    private readonly ICompanyAuthorizationService _authorization;
    private readonly IGustoTimeClient _gusto;
    private readonly ILogger<GustoTimeSyncService> _logger;

    public GustoTimeSyncService(
        AppDbContext db,
        ICompanyAuthorizationService authorization,
        IGustoTimeClient gusto,
        ILogger<GustoTimeSyncService> logger)
    {
        _db = db;
        _authorization = authorization;
        _gusto = gusto;
        _logger = logger;
    }

    public async Task<(GustoWeekSyncResultDto? Result, string? Error, int StatusCode)> SendWeekAsync(
        CurrentUserContext user,
        string? weekStartText,
        int personId,
        CancellationToken cancellationToken)
    {
        if (!_authorization.IsManager(user))
        {
            return (null, "You cannot send hours to Gusto.", 403);
        }

        if (!DateOnly.TryParse(weekStartText, out var weekStart) || !TimeZoneHelper.IsSunday(weekStart))
        {
            return (null, "weekStart must be a Sunday (YYYY-MM-DD).", 400);
        }

        var memberExists = await _db.CompanyMembers.AsNoTracking().AnyAsync(
            member => member.CompanyId == user.CompanyId
                && member.PersonId == personId
                && member.Status == "active",
            cancellationToken);
        if (!memberExists)
        {
            return (null, "Person not found.", 404);
        }

        var link = await _db.GustoEmployeeLinks.AsNoTracking().FirstOrDefaultAsync(
            row => row.PersonId == personId && row.CompanyId == user.CompanyId,
            cancellationToken);
        if (link is null)
        {
            return (null, "This worker is not linked to Gusto.", 422);
        }

        try
        {
            await _gusto.PrepareAsync(cancellationToken);
        }
        catch (GustoNotConfiguredException ex)
        {
            return (null, ex.Message, 503);
        }

        var company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == user.CompanyId, cancellationToken);
        var timeZone = TimeZoneHelper.Resolve(company.Timezone);
        var weekEnd = weekStart.AddDays(6);
        var queryFrom = TimeZoneHelper.ToUtcStartOfDay(weekStart, timeZone).AddDays(-1);
        var queryTo = TimeZoneHelper.ToUtcEndOfDay(weekEnd, timeZone).AddDays(1);
        var weekDays = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToHashSet();

        var entries = await _db.TimeEntries
            .Where(entry => entry.PersonId == personId
                && entry.ClockInAt >= queryFrom
                && entry.ClockInAt <= queryTo)
            .ToListAsync(cancellationToken);
        entries = entries
            .Where(entry => weekDays.Contains(TimeZoneHelper.ToLocalDate(entry.ClockInAt, timeZone)))
            .OrderBy(entry => entry.ClockInAt)
            .ToList();

        var entryIds = entries.Select(entry => entry.Id).ToList();
        var breaksByEntryId = await _db.TimeBreaks
            .AsNoTracking()
            .Where(row => entryIds.Contains(row.TimeEntryId))
            .GroupBy(row => row.TimeEntryId)
            .ToDictionaryAsync(group => group.Key, group => (IReadOnlyList<TimeBreak>)group.ToList(), cancellationToken);
        var jobIds = entries.Select(entry => entry.JobId).Distinct().ToList();
        var jobsById = await _db.Jobs.AsNoTracking()
            .Where(job => jobIds.Contains(job.Id))
            .ToDictionaryAsync(job => job.Id, job => job.Title, cancellationToken);
        var syncByEntryId = await _db.GustoTimeSheetSyncs
            .Where(row => entryIds.Contains(row.TimeEntryId))
            .ToDictionaryAsync(row => row.TimeEntryId, cancellationToken);

        var sent = 0;
        var alreadySynced = 0;
        var skippedOpen = 0;
        var skippedZero = 0;
        var failed = new List<GustoSyncFailureDto>();
        var stop = false;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            if (stop)
            {
                break;
            }

            if (entry.ClockOutAt is null)
            {
                skippedOpen++;
                continue;
            }

            var minutes = TimeEntryMinutes.Compute(entry, breaksByEntryId.GetValueOrDefault(entry.Id), now);
            if (minutes.WorkMinutes <= 0)
            {
                skippedZero++;
                continue;
            }

            if (syncByEntryId.TryGetValue(entry.Id, out var existing) && existing.Status == "synced")
            {
                alreadySynced++;
                continue;
            }

            var hours = GustoHours.FromWorkMinutes(minutes.WorkMinutes);
            var sync = existing;
            if (sync is null)
            {
                sync = new GustoTimeSheetSync
                {
                    TimeEntryId = entry.Id,
                    Status = "pending",
                    HoursWorked = hours,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.GustoTimeSheetSyncs.Add(sync);
            }
            else
            {
                sync.Status = "pending";
                sync.HoursWorked = hours;
                sync.ErrorMessage = null;
                sync.GustoTimeSheetUuid = null;
                sync.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);

            var result = await _gusto.PostTimeSheetAsync(
                new GustoTimeSheetRequest(
                    link.GustoEmployeeUuid,
                    link.GustoJobUuid,
                    company.Timezone,
                    entry.ClockInAt,
                    entry.ClockOutAt.Value,
                    hours,
                    entry.Id,
                    jobsById.GetValueOrDefault(entry.JobId, "Unknown")),
                cancellationToken);

            _logger.LogInformation(
                "Gusto time sheet post for time entry {TimeEntryId} returned {StatusCode}",
                entry.Id,
                result.StatusCode);

            if (result.StopBatch)
            {
                sync.Status = "failed";
                sync.ErrorMessage = result.ErrorMessage ?? "Gusto rejected the access token.";
                sync.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
                failed.Add(new GustoSyncFailureDto(entry.Id, sync.ErrorMessage));
                stop = true;
                continue;
            }

            if (result.Accepted)
            {
                sync.Status = "synced";
                sync.GustoTimeSheetUuid = result.TimeSheetUuid;
                sync.ErrorMessage = null;
                sent++;
            }
            else
            {
                sync.Status = "failed";
                sync.ErrorMessage = result.ErrorMessage ?? "Gusto rejected the time sheet.";
                failed.Add(new GustoSyncFailureDto(entry.Id, sync.ErrorMessage));
            }

            sync.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return (new GustoWeekSyncResultDto(sent, alreadySynced, skippedOpen, skippedZero, failed), null, 200);
    }
}
