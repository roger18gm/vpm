using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class GustoHoursTests
{
    [Theory]
    [InlineData(0, "0.0000")]
    [InlineData(1, "0.0167")]
    [InlineData(30, "0.5000")]
    [InlineData(90, "1.5000")]
    [InlineData(240, "4.0000")]
    public void FromWorkMinutes_rounds_half_away_from_zero_to_four_places(int minutes, string expected)
    {
        var hours = GustoHours.FromWorkMinutes(minutes);
        Assert.Equal(decimal.Parse(expected), hours);
    }
}