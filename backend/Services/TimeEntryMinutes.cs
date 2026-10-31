using VisionPaint.Models;

namespace VisionPaint.Services;

public readonly record struct TimeEntryMinuteResult(int WorkMinutes, int BreakMinutes);

public static class TimeEntryMinutes
{
    public static TimeEntryMinuteResult Compute(
        TimeEntry entry,
        IReadOnlyList<TimeBreak>? breaks,
        DateTimeOffset now)
    {
        if (entry.ClockOutAt is null)
        {
            var breakMinutes = SumBreaks(breaks, now);
            var partial = (int)Math.Max(0, (now - entry.ClockInAt).TotalMinutes);
            return new TimeEntryMinuteResult(Math.Max(0, partial - breakMinutes), breakMinutes);
        }

        var span = (int)Math.Max(0, (entry.ClockOutAt.Value - entry.ClockInAt).TotalMinutes);
        return new TimeEntryMinuteResult(Math.Max(0, span - entry.BreakMinutes), entry.BreakMinutes);
    }

    private static int SumBreaks(IReadOnlyList<TimeBreak>? breaks, DateTimeOffset now)
    {
        if (breaks is null)
        {
            return 0;
        }

        return breaks.Sum(b => (int)Math.Max(0, ((b.BreakEndAt ?? now) - b.BreakStartAt).TotalMinutes));
    }
}
