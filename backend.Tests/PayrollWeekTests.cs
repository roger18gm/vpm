using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class PayrollWeekTests
{
    private static readonly TimeZoneInfo Denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

    private static DateTimeOffset AtDenver(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified),
            Denver);

    [Theory]
    [InlineData(2026, 10, 25, 20, 0)]
    [InlineData(2026, 10, 26, 6, 59)]
    public void Before_monday_0700_is_not_sendable(int year, int month, int day, int hour, int minute)
    {
        var sendable = PayrollWeek.TryGetSendableWeek(AtDenver(year, month, day, hour, minute), Denver, out _);
        Assert.False(sendable);
    }

    [Fact]
    public void Monday_0700_selects_the_previous_sunday()
    {
        var sendable = PayrollWeek.TryGetSendableWeek(AtDenver(2026, 10, 26, 7, 0), Denver, out var weekStart);

        Assert.True(sendable);
        Assert.Equal(new DateOnly(2026, 10, 18), weekStart);
    }

    [Fact]
    public void Later_in_the_same_week_keeps_that_sunday()
    {
        var sendable = PayrollWeek.TryGetSendableWeek(AtDenver(2026, 10, 27, 15, 0), Denver, out var weekStart);

        Assert.True(sendable);
        Assert.Equal(new DateOnly(2026, 10, 18), weekStart);
    }

    [Fact]
    public void Next_monday_0700_moves_to_the_newer_week()
    {
        var sendable = PayrollWeek.TryGetSendableWeek(AtDenver(2026, 11, 2, 7, 0), Denver, out var weekStart);

        Assert.True(sendable);
        Assert.Equal(new DateOnly(2026, 10, 25), weekStart);
    }
}
