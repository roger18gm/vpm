using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class ClockAlertScheduleTests
{
    private static readonly TimeZoneInfo Denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

    [Theory]
    [InlineData(2026, 10, 26, 6, 59, 2026, 10, 25, "evening")]
    [InlineData(2026, 10, 26, 7, 0, 2026, 10, 26, "morning")]
    [InlineData(2026, 10, 26, 18, 59, 2026, 10, 26, "morning")]
    [InlineData(2026, 10, 26, 19, 0, 2026, 10, 26, "evening")]
    public void OpenSlot_uses_the_7am_and_7pm_boundaries(
        int year, int month, int day, int hour, int minute,
        int slotYear, int slotMonth, int slotDay, string kind)
    {
        var slot = ClockAlertSchedule.OpenSlot(AtDenver(year, month, day, hour, minute), Denver);

        Assert.Equal(new DateOnly(slotYear, slotMonth, slotDay), slot.SlotOn);
        Assert.Equal(kind, slot.Kind);
    }

    [Theory]
    [InlineData(2026, 10, 27, 10, 0, 2026, 10, 28, 7, 0)]
    [InlineData(2026, 10, 27, 19, 5, 2026, 10, 28, 7, 0)]
    [InlineData(2026, 10, 30, 21, 0, 2026, 11, 2, 7, 0)]
    [InlineData(2026, 10, 31, 8, 0, 2026, 11, 2, 7, 0)]
    [InlineData(2026, 10, 31, 6, 0, 2026, 11, 2, 7, 0)]
    [InlineData(2026, 11, 2, 6, 0, 2026, 11, 2, 7, 0)]
    public void FollowUpInstant_is_the_next_weekday_at_7am(
        int year, int month, int day, int hour, int minute,
        int followYear, int followMonth, int followDay, int followHour, int followMinute)
    {
        var followUp = ClockAlertSchedule.FollowUpInstant(AtDenver(year, month, day, hour, minute), Denver);

        Assert.Equal(AtDenver(followYear, followMonth, followDay, followHour, followMinute), followUp);
    }

    [Fact]
    public void Threshold_and_whole_minutes()
    {
        Assert.Equal(45, ClockAlertSchedule.ThresholdMinutes("lunch"));
        Assert.Equal(45, ClockAlertSchedule.ThresholdMinutes("LUNCH"));
        Assert.Equal(20, ClockAlertSchedule.ThresholdMinutes("rest"));
        Assert.Equal(20, ClockAlertSchedule.ThresholdMinutes("other"));
        Assert.Equal("Lunch", ClockAlertSchedule.BreakLabel("lunch"));
        Assert.Equal("Rest", ClockAlertSchedule.BreakLabel("rest"));
        Assert.Equal("Other", ClockAlertSchedule.BreakLabel("other"));

        var start = AtDenver(2026, 10, 27, 18, 0);
        Assert.Equal(19, ClockAlertSchedule.WholeMinutes(start, start.AddMinutes(19.9)));
        Assert.Equal(20, ClockAlertSchedule.WholeMinutes(start, start.AddMinutes(20)));
    }

    private static DateTimeOffset AtDenver(int year, int month, int day, int hour, int minute) =>
        new(TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified),
            Denver));
}
