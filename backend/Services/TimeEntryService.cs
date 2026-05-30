using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class TimeEntryService
{
    private readonly AppDbContext _db;
    private readonly IJobAccessService _jobAccess;

    public TimeEntryService(AppDbContext db, IJobAccessService jobAccess)
    {
        _db = db;
        _jobAccess = jobAccess;
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
            var minutes = 0;
            var inProgress = false;

            foreach (var entry in group)
            {
                if (entry.ClockOutAt is null)
                {
                    inProgress = true;
                    var partial = (int)Math.Max(0, (now - entry.ClockInAt).TotalMinutes);
                    minutes += partial;
                    activeMinutes += partial;
                }
                else
                {
                    var span = (int)Math.Max(0, (entry.ClockOutAt.Value - entry.ClockInAt).TotalMinutes);
                    minutes += Math.Max(0, span - entry.BreakMinutes);
                }
            }

            totalMinutes += minutes;
            byPerson.Add(new JobTimePersonDto(
                group.Key,
                people.GetValueOrDefault(group.Key, "Unknown"),
                minutes,
                inProgress));
        }

        return new JobTimeSummaryDto(totalMinutes, activeMinutes, byPerson);
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
}
