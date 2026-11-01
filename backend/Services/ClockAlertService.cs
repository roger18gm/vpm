using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class ClockAlertService : IClockAlertService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly TimeProvider _time;
    private readonly AppOptions _app;
    private readonly ILogger<ClockAlertService> _logger;

    public ClockAlertService(
        AppDbContext db,
        IEmailSender email,
        TimeProvider time,
        IOptions<AppOptions> app,
        ILogger<ClockAlertService> logger)
    {
        _db = db;
        _email = email;
        _time = time;
        _app = app.Value;
        _logger = logger;
    }

    public async Task SendDueAlertsAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var companies = await _db.Companies.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var company in companies)
        {
            var zone = TimeZoneHelper.Resolve(company.Timezone);
            var slot = ClockAlertSchedule.OpenSlot(now, zone);
            if (!await TryClaimAsync(company.Id, slot, now, cancellationToken))
            {
                continue;
            }

            var work = await LoadWorkAsync(company, zone, slot, now, cancellationToken);
            var sending = work.Where(alert => alert.Send).ToList();
            var consumes = work.Where(alert => !alert.Send).ToList();
            if (sending.Count == 0)
            {
                await MarkSentAsync(company.Id, Array.Empty<AlertWork>(), consumes, now, cancellationToken);
                await FinishSlotAsync(company.Id, slot, now, cancellationToken);
                continue;
            }

            var recipients = await LoadRecipientsAsync(company.Id, sending, cancellationToken);
            var delivered = new HashSet<AlertWork>();
            var succeeded = 0;
            foreach (var group in sending
                .SelectMany(alert => recipients[alert].Select(recipient => (recipient, alert)))
                .GroupBy(pair => pair.recipient.PersonId))
            {
                var recipient = group.First().recipient;
                var alerts = group.Select(pair => pair.alert).Distinct().ToList();
                var (subject, html, text) = ClockAlertMail.Render(BuildModel(company.Name, slot, alerts));
                try
                {
                    await _email.SendAsync(recipient.Email, subject, html, text, cancellationToken);
                    succeeded++;
                    foreach (var alert in alerts)
                    {
                        delivered.Add(alert);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Clock alert email failed for {Recipient} company {CompanyId} slot {SlotOn} {SlotKind}",
                        recipient.Email,
                        company.Id,
                        slot.SlotOn,
                        slot.Kind);
                }
            }

            if (succeeded == 0)
            {
                await ReleaseAsync(company.Id, slot, cancellationToken);
                continue;
            }

            await MarkSentAsync(company.Id, delivered.ToList(), consumes, now, cancellationToken);
            await FinishSlotAsync(company.Id, slot, now, cancellationToken);
            _logger.LogInformation(
                "Clock alerts sent for company {CompanyId} slot {SlotOn} {SlotKind} to {RecipientCount} recipients",
                company.Id,
                slot.SlotOn,
                slot.Kind,
                succeeded);
        }
    }

    private async Task<bool> TryClaimAsync(
        int companyId,
        ClockAlertSchedule.Slot slot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _db.ClockAlertSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.CompanyId == companyId && row.SlotOn == slot.SlotOn && row.SlotKind == slot.Kind,
                cancellationToken);
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
            _db.ClockAlertSlots.Add(new ClockAlertSlot
            {
                CompanyId = companyId,
                SlotOn = slot.SlotOn,
                SlotKind = slot.Kind,
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
        var updated = await _db.ClockAlertSlots
            .Where(row => row.CompanyId == companyId
                && row.SlotOn == slot.SlotOn
                && row.SlotKind == slot.Kind
                && row.SentAt == null
                && row.ClaimedAt == claimedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ClaimedAt, now), cancellationToken);
        return updated == 1;
    }

    private async Task ReleaseAsync(
        int companyId,
        ClockAlertSchedule.Slot slot,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Clock alerts were not sent for company {CompanyId} slot {SlotOn} {SlotKind}",
            companyId,
            slot.SlotOn,
            slot.Kind);
        await _db.ClockAlertSlots
            .Where(row => row.CompanyId == companyId
                && row.SlotOn == slot.SlotOn
                && row.SlotKind == slot.Kind
                && row.SentAt == null)
            .ExecuteDeleteAsync(cancellationToken);
        _db.ChangeTracker.Clear();
    }

    private async Task FinishSlotAsync(
        int companyId,
        ClockAlertSchedule.Slot slot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _db.ClockAlertSlots
            .Where(row => row.CompanyId == companyId
                && row.SlotOn == slot.SlotOn
                && row.SlotKind == slot.Kind
                && row.SentAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SentAt, now), cancellationToken);
        _db.ChangeTracker.Clear();
    }

    private async Task MarkSentAsync(
        int companyId,
        IReadOnlyList<AlertWork> delivered,
        IReadOnlyList<AlertWork> consumes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var alert in delivered.Where(alert => !alert.FollowUp))
        {
            _db.ClockAlertSends.Add(new ClockAlertSend
            {
                CompanyId = companyId,
                TimeBreakId = alert.TimeBreakId,
                TimeEntryId = alert.TimeEntryId,
                FirstSentAt = now
            });
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var followUpIds = delivered
            .Where(alert => alert.FollowUp)
            .Select(alert => alert.ExistingId!.Value)
            .Concat(consumes.Select(alert => alert.ExistingId!.Value))
            .Distinct()
            .ToList();
        if (followUpIds.Count == 0)
        {
            return;
        }

        await _db.ClockAlertSends
            .Where(row => followUpIds.Contains(row.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FollowUpSentAt, now), cancellationToken);
    }

    private string LinkFor(int jobId) => $"{_app.FrontendBaseUrl.TrimEnd('/')}/jobs/{jobId}";

    private async Task<List<AlertWork>> LoadWorkAsync(
        Company company,
        TimeZoneInfo zone,
        ClockAlertSchedule.Slot slot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sends = await _db.ClockAlertSends.AsNoTracking()
            .Where(row => row.CompanyId == company.Id)
            .ToListAsync(cancellationToken);
        var openEntries = await (
            from entry in _db.TimeEntries.AsNoTracking()
            join job in _db.Jobs.AsNoTracking() on entry.JobId equals job.Id
            join person in _db.People.AsNoTracking() on entry.PersonId equals person.Id
            where job.CompanyId == company.Id && entry.ClockOutAt == null
            select new EntryRow(entry.Id, entry.JobId, entry.PersonId, person.Name, job.Title, entry.ClockInAt, entry.ClockOutAt)
        ).ToListAsync(cancellationToken);

        var openIds = openEntries.Select(entry => entry.Id).ToList();
        var trackedBreakIds = sends.Where(row => row.TimeBreakId != null).Select(row => row.TimeBreakId!.Value).ToList();
        var breaks = await _db.TimeBreaks.AsNoTracking()
            .Where(row => openIds.Contains(row.TimeEntryId) || trackedBreakIds.Contains(row.Id))
            .ToListAsync(cancellationToken);
        var extraEntryIds = sends.Where(row => row.TimeEntryId != null).Select(row => row.TimeEntryId!.Value)
            .Concat(breaks.Select(row => row.TimeEntryId))
            .Distinct()
            .Except(openIds)
            .ToList();
        var extraEntries = extraEntryIds.Count == 0
            ? new List<EntryRow>()
            : await (
                from entry in _db.TimeEntries.AsNoTracking()
                join job in _db.Jobs.AsNoTracking() on entry.JobId equals job.Id
                join person in _db.People.AsNoTracking() on entry.PersonId equals person.Id
                where extraEntryIds.Contains(entry.Id)
                select new EntryRow(entry.Id, entry.JobId, entry.PersonId, person.Name, job.Title, entry.ClockInAt, entry.ClockOutAt)
            ).ToListAsync(cancellationToken);

        var entries = openEntries.Concat(extraEntries).ToDictionary(entry => entry.Id);
        var breakMap = breaks.ToDictionary(row => row.Id);
        var sentBreaks = sends.Where(row => row.TimeBreakId != null).Select(row => row.TimeBreakId!.Value).ToHashSet();
        var sentShifts = sends.Where(row => row.TimeEntryId != null).Select(row => row.TimeEntryId!.Value).ToHashSet();
        var slotSeven = ClockAlertSchedule.AtLocal(slot.SlotOn, new TimeOnly(7, 0), zone);
        var morning = slot.Kind == ClockAlertSchedule.Morning;
        var weekend = ClockAlertSchedule.IsWeekend(slot.SlotOn);
        var work = new List<AlertWork>();

        if (morning)
        {
            foreach (var send in sends.Where(row => row.FollowUpSentAt is null))
            {
                var followUp = ClockAlertSchedule.FollowUpInstant(send.FirstSentAt, zone);
                if (followUp > slotSeven)
                {
                    continue;
                }

                var still = TryFollowUp(send, entries, breakMap, zone, now, out var alert);
                if (!weekend && followUp == slotSeven && still)
                {
                    work.Add(alert!);
                }
                else
                {
                    work.Add(Consume(send));
                }
            }
        }

        foreach (var row in breaks.Where(row => row.BreakEndAt is null && !sentBreaks.Contains(row.Id)))
        {
            if (!entries.TryGetValue(row.TimeEntryId, out var entry) || entry.ClockOutAt is not null)
            {
                continue;
            }

            var minutes = ClockAlertSchedule.WholeMinutes(row.BreakStartAt, now);
            if (minutes < ClockAlertSchedule.ThresholdMinutes(row.BreakType))
            {
                continue;
            }

            work.Add(BreakAlert(entry, row, minutes, followUp: false, existingId: null));
        }

        if (slot.Kind == ClockAlertSchedule.Evening && !weekend)
        {
            foreach (var entry in openEntries.Where(entry => !sentShifts.Contains(entry.Id)))
            {
                work.Add(ShiftAlert(entry, zone, followUp: false, existingId: null));
            }
        }

        return work;
    }

    private bool TryFollowUp(
        ClockAlertSend send,
        Dictionary<int, EntryRow> entries,
        Dictionary<int, TimeBreak> breaks,
        TimeZoneInfo zone,
        DateTimeOffset now,
        out AlertWork? alert)
    {
        alert = null;
        if (send.TimeBreakId is int breakId)
        {
            if (!breaks.TryGetValue(breakId, out var row) || row.BreakEndAt is not null)
            {
                return false;
            }

            if (!entries.TryGetValue(row.TimeEntryId, out var entry) || entry.ClockOutAt is not null)
            {
                return false;
            }

            var minutes = ClockAlertSchedule.WholeMinutes(row.BreakStartAt, now);
            if (minutes < ClockAlertSchedule.ThresholdMinutes(row.BreakType))
            {
                return false;
            }

            alert = BreakAlert(entry, row, minutes, followUp: true, existingId: send.Id);
            return true;
        }

        if (send.TimeEntryId is int entryId
            && entries.TryGetValue(entryId, out var shift)
            && shift.ClockOutAt is null)
        {
            alert = ShiftAlert(shift, zone, followUp: true, existingId: send.Id);
            return true;
        }

        return false;
    }

    private AlertWork BreakAlert(EntryRow entry, TimeBreak row, int minutes, bool followUp, int? existingId) =>
        new()
        {
            TimeBreakId = row.Id,
            ExistingId = existingId,
            JobId = entry.JobId,
            PersonId = entry.PersonId,
            PersonName = entry.PersonName,
            JobTitle = entry.JobTitle,
            Link = LinkFor(entry.JobId),
            FollowUp = followUp,
            Send = true,
            Line = ClockAlertMail.BreakLine(row.BreakType, minutes),
            SortAt = row.BreakStartAt,
            SortId = row.Id
        };

    private AlertWork ShiftAlert(EntryRow entry, TimeZoneInfo zone, bool followUp, int? existingId) =>
        new()
        {
            TimeEntryId = entry.Id,
            ExistingId = existingId,
            JobId = entry.JobId,
            PersonId = entry.PersonId,
            PersonName = entry.PersonName,
            JobTitle = entry.JobTitle,
            Link = LinkFor(entry.JobId),
            FollowUp = followUp,
            Send = true,
            Line = ClockAlertMail.ClockedInSince(entry.ClockInAt, zone),
            SortAt = DateTimeOffset.MaxValue,
            SortId = entry.Id
        };

    private static AlertWork Consume(ClockAlertSend send) =>
        new()
        {
            ExistingId = send.Id,
            TimeBreakId = send.TimeBreakId,
            TimeEntryId = send.TimeEntryId,
            FollowUp = true,
            Send = false
        };

    private async Task<Dictionary<AlertWork, List<Recipient>>> LoadRecipientsAsync(
        int companyId,
        IReadOnlyList<AlertWork> alerts,
        CancellationToken cancellationToken)
    {
        var result = alerts.ToDictionary(alert => alert, _ => new List<Recipient>());
        var rows = await (
            from member in _db.CompanyMembers.AsNoTracking()
            join person in _db.People.AsNoTracking() on member.PersonId equals person.Id
            join auth in _db.AuthUsers.AsNoTracking() on person.AuthUserId equals auth.Id
            where member.CompanyId == companyId && member.Status == "active" && auth.IsActive
            select new { person.Id, member.Role, auth.Email }
        ).ToListAsync(cancellationToken);
        var directory = rows
            .Select(row => new DirectoryPerson(row.Id, row.Role, row.Email.Trim()))
            .Where(row => row.Email.Length > 0)
            .ToList();
        var jobIds = alerts.Select(alert => alert.JobId).Distinct().ToList();
        var assignments = await _db.JobAssignments.AsNoTracking()
            .Where(row => jobIds.Contains(row.JobId) && row.UnassignedAt == null)
            .ToListAsync(cancellationToken);
        var owners = directory.Where(person =>
            person.CompanyRole.Equals("owner", StringComparison.OrdinalIgnoreCase)
            || person.CompanyRole.Equals("admin", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var alert in alerts)
        {
            var leads = assignments
                .Where(row => row.JobId == alert.JobId
                    && row.PersonId != alert.PersonId
                    && IsLead(row.AssignmentRole))
                .Select(row => directory.FirstOrDefault(person => person.PersonId == row.PersonId))
                .Where(person => person is not null)
                .Select(person => new Recipient(person!.PersonId, person.Email))
                .ToList();
            if (leads.Count == 0)
            {
                leads = owners
                    .Where(person => person.PersonId != alert.PersonId)
                    .Select(person => new Recipient(person.PersonId, person.Email))
                    .ToList();
            }

            result[alert] = leads;
        }

        return result;
    }

    private static bool IsLead(string assignmentRole) =>
        assignmentRole.Equals("lead", StringComparison.OrdinalIgnoreCase)
        || assignmentRole.Equals("supervisor", StringComparison.OrdinalIgnoreCase);

    private static ClockAlertMailModel BuildModel(
        string companyName,
        ClockAlertSchedule.Slot slot,
        IReadOnlyList<AlertWork> alerts)
    {
        var jobs = alerts
            .GroupBy(alert => alert.JobId)
            .Select(group =>
            {
                var people = group
                    .GroupBy(alert => alert.PersonId)
                    .Select(person => new ClockAlertPersonModel(
                        person.First().PersonName,
                        person.OrderBy(alert => alert.SortAt).ThenBy(alert => alert.SortId).Select(alert => alert.Line!).ToList(),
                        person.Key))
                    .OrderBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(person => person.PersonId)
                    .ToList();
                return new ClockAlertJobModel(group.First().JobTitle, group.First().Link, people, group.Key);
            })
            .OrderBy(job => job.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.JobId)
            .ToList();
        return new ClockAlertMailModel(companyName, slot.SlotOn, slot.Kind == ClockAlertSchedule.Morning, jobs);
    }

    private sealed class AlertWork
    {
        public int? TimeBreakId { get; init; }
        public int? TimeEntryId { get; init; }
        public int? ExistingId { get; init; }
        public int JobId { get; init; }
        public int PersonId { get; init; }
        public string PersonName { get; init; } = "";
        public string JobTitle { get; init; } = "";
        public string Link { get; init; } = "";
        public bool FollowUp { get; init; }
        public bool Send { get; init; }
        public string? Line { get; init; }
        public DateTimeOffset SortAt { get; init; }
        public int SortId { get; init; }
    }

    private sealed record EntryRow(
        int Id,
        int JobId,
        int PersonId,
        string PersonName,
        string JobTitle,
        DateTimeOffset ClockInAt,
        DateTimeOffset? ClockOutAt);

    private sealed record DirectoryPerson(int PersonId, string CompanyRole, string Email);

    private sealed record Recipient(int PersonId, string Email);
}
