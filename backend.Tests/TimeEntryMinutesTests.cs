using VisionPaint.Models;
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class TimeEntryMinutesTests
{
    [Fact]
    public void Closed_entry_uses_stored_break_minutes()
    {
        var clockIn = new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero);
        var entry = new TimeEntry
        {
            ClockInAt = clockIn,
            ClockOutAt = clockIn.AddMinutes(90),
            BreakMinutes = 30
        };

        var result = TimeEntryMinutes.Compute(entry, breaks: null, now: clockIn.AddHours(5));

        Assert.Equal(60, result.WorkMinutes);
        Assert.Equal(30, result.BreakMinutes);
    }

    [Fact]
    public void Closed_entry_floors_work_at_zero_when_break_exceeds_span()
    {
        var clockIn = new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero);
        var entry = new TimeEntry
        {
            ClockInAt = clockIn,
            ClockOutAt = clockIn.AddMinutes(10),
            BreakMinutes = 40
        };

        var result = TimeEntryMinutes.Compute(entry, breaks: null, now: clockIn);

        Assert.Equal(0, result.WorkMinutes);
        Assert.Equal(40, result.BreakMinutes);
    }

    [Fact]
    public void Open_entry_counts_through_now_and_open_breaks()
    {
        var now = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero);
        var entry = new TimeEntry
        {
            ClockInAt = now.AddMinutes(-90),
            ClockOutAt = null,
            BreakMinutes = 0
        };
        var breaks = new List<TimeBreak>
        {
            new()
            {
                BreakStartAt = now.AddMinutes(-15),
                BreakEndAt = null,
                BreakType = "rest"
            }
        };

        var result = TimeEntryMinutes.Compute(entry, breaks, now);

        Assert.Equal(75, result.WorkMinutes);
        Assert.Equal(15, result.BreakMinutes);
    }
}
