using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class PayrollSummaryService : IPayrollSummaryService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly TimeProvider _time;
    private readonly ILogger<PayrollSummaryService> _logger;

    public PayrollSummaryService(
        AppDbContext db,
        IEmailSender email,
        TimeProvider time,
        ILogger<PayrollSummaryService> logger)
    {
        _db = db;
        _email = email;
        _time = time;
        _logger = logger;
    }

    public async Task SendDueSummariesAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var companies = await _db.Companies.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var company in companies)
        {
            var timeZone = TimeZoneHelper.Resolve(company.Timezone);
            if (!PayrollWeek.TryGetSendableWeek(now, timeZone, out var weekStart))
            {
                continue;
            }

            if (!await TryClaimAsync(company.Id, weekStart, now, cancellationToken))
            {
                continue;
            }

            var summary = await BuildSummaryAsync(company, timeZone, weekStart, now, cancellationToken);
            var recipients = await LoadRecipientsAsync(company.Id, cancellationToken);
            if (recipients.Count == 0)
            {
                await ReleaseAsync(company.Id, weekStart, cancellationToken);
                continue;
            }

            var (subject, html, text) = PayrollSummaryMail.Render(summary);
            var sent = 0;
            foreach (var recipient in recipients)
            {
                try
                {
                    await _email.SendAsync(recipient, subject, html, text, cancellationToken);
                    sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Payroll summary email failed for {Recipient} company {CompanyId} week {WeekStart}",
                        recipient,
                        company.Id,
                        weekStart);
                }
            }

            if (sent == 0)
            {
                await ReleaseAsync(company.Id, weekStart, cancellationToken);
                continue;
            }

            await _db.PayrollSummarySends
                .Where(row => row.CompanyId == company.Id && row.WeekStart == weekStart)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SentAt, now), cancellationToken);
            _db.ChangeTracker.Clear();
            _logger.LogInformation(
                "Payroll summary sent for company {CompanyId} week {WeekStart} to {RecipientCount} recipients",
                company.Id,
                weekStart,
                sent);
        }
    }

    private async Task<bool> TryClaimAsync(
        int companyId,
        DateOnly weekStart,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _db.PayrollSummarySends
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.CompanyId == companyId && row.WeekStart == weekStart, cancellationToken);

        if (existing is { SentAt: not null })
        {
            return false;
        }

        if (existing is not null && existing.ClaimedAt > now.AddMinutes(-15))
        {
            return false;
        }

        if (existing is null)
        {
            _db.PayrollSummarySends.Add(new PayrollSummarySend
            {
                CompanyId = companyId,
                WeekStart = weekStart,
                ClaimedAt = now
            });
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                return false;
            }
        }

        var claimedAt = existing.ClaimedAt;
        var updated = await _db.PayrollSummarySends
            .Where(row => row.CompanyId == companyId
                && row.WeekStart == weekStart
                && row.SentAt == null
                && row.ClaimedAt == claimedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ClaimedAt, now), cancellationToken);
        return updated == 1;
    }

    private async Task ReleaseAsync(int companyId, DateOnly weekStart, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Payroll summary was not sent for company {CompanyId} week {WeekStart}",
            companyId,
            weekStart);
        await _db.PayrollSummarySends
            .Where(row => row.CompanyId == companyId && row.WeekStart == weekStart && row.SentAt == null)
            .ExecuteDeleteAsync(cancellationToken);
        _db.ChangeTracker.Clear();
    }

    private async Task<PayrollSummaryModel> BuildSummaryAsync(
        Company company,
        TimeZoneInfo timeZone,
        DateOnly weekStart,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var weekEnd = weekStart.AddDays(6);
        var queryFrom = TimeZoneHelper.ToUtcStartOfDay(weekStart, timeZone).AddDays(-1);
        var queryTo = TimeZoneHelper.ToUtcEndOfDay(weekEnd, timeZone).AddDays(1);
        var weekDays = Enumerable.Range(0, 7).Select(offset => weekStart.AddDays(offset)).ToHashSet();

        var people = await (
            from member in _db.CompanyMembers.AsNoTracking()
            join person in _db.People.AsNoTracking() on member.PersonId equals person.Id
            where member.CompanyId == company.Id && member.Status == "active"
            orderby person.Name, person.Id
            select new { person.Id, person.Name }
        ).ToListAsync(cancellationToken);

        var entries = await _db.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.ClockInAt >= queryFrom && entry.ClockInAt <= queryTo)
            .Join(
                _db.Jobs.AsNoTracking().Where(job => job.CompanyId == company.Id),
                entry => entry.JobId,
                job => job.Id,
                (entry, _) => entry)
            .ToListAsync(cancellationToken);
        entries = entries
            .Where(entry => weekDays.Contains(TimeZoneHelper.ToLocalDate(entry.ClockInAt, timeZone)))
            .ToList();

        var entryIds = entries.Select(entry => entry.Id).ToList();
        var breaks = await _db.TimeBreaks
            .AsNoTracking()
            .Where(row => entryIds.Contains(row.TimeEntryId))
            .ToListAsync(cancellationToken);

        var personRows = new List<PayrollPersonModel>();
        foreach (var person in people)
        {
            var personEntries = entries.Where(entry => entry.PersonId == person.Id).ToList();
            var dayMinutes = weekDays.ToDictionary(day => day, _ => (Work: 0, Break: 0));
            foreach (var entry in personEntries)
            {
                var entryBreaks = breaks.Where(row => row.TimeEntryId == entry.Id).ToList();
                var minutes = TimeEntryMinutes.Compute(entry, entryBreaks, now);
                var day = TimeZoneHelper.ToLocalDate(entry.ClockInAt, timeZone);
                var current = dayMinutes[day];
                dayMinutes[day] = (current.Work + minutes.WorkMinutes, current.Break + minutes.BreakMinutes);
            }

            var days = Enumerable.Range(0, 7)
                .Select(offset => weekStart.AddDays(offset))
                .Select(date => new PayrollDayModel(date, dayMinutes[date].Work, dayMinutes[date].Break))
                .ToList();
            personRows.Add(new PayrollPersonModel(
                person.Name,
                days.Sum(day => day.WorkMinutes),
                days.Sum(day => day.BreakMinutes),
                personEntries.Any(entry => entry.ClockOutAt is null),
                days));
        }

        return new PayrollSummaryModel(
            company.Name,
            weekStart,
            weekEnd,
            personRows.Sum(person => person.WorkMinutes),
            personRows.Sum(person => person.BreakMinutes),
            personRows);
    }

    private async Task<List<string>> LoadRecipientsAsync(int companyId, CancellationToken cancellationToken)
    {
        var emails = await (
            from member in _db.CompanyMembers.AsNoTracking()
            join person in _db.People.AsNoTracking() on member.PersonId equals person.Id
            join auth in _db.AuthUsers.AsNoTracking() on person.AuthUserId equals auth.Id
            where member.CompanyId == companyId
                && member.Status == "active"
                && auth.IsActive
                && (member.Role.ToLower() == "owner" || member.Role.ToLower() == "admin")
            select auth.Email
        ).ToListAsync(cancellationToken);

        return emails
            .Select(email => email.Trim())
            .Where(email => email.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
