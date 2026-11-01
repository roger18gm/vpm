using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class ClockAlertMailTests
{
    private static readonly TimeZoneInfo Denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

    [Theory]
    [InlineData(true, "Clock alerts for Oct 27, 7:00 AM")]
    [InlineData(false, "Clock alerts for Oct 27, 7:00 PM")]
    public void Subject_names_the_slot_clock(bool morning, string expected)
    {
        Assert.Equal(expected, ClockAlertMail.SubjectFor(new DateOnly(2026, 10, 27), morning));
    }

    [Fact]
    public void Render_encodes_html_and_keeps_plain_text()
    {
        var model = new ClockAlertMailModel(
            "VisionPaint",
            new DateOnly(2026, 10, 27),
            Morning: false,
            new[]
            {
                new ClockAlertJobModel(
                    "North <house\nwest",
                    "http://localhost:5173/jobs/4",
                    new[]
                    {
                        new ClockAlertPersonModel(
                            "A<lex",
                            new[]
                            {
                                "Lunch break open for 45 minutes",
                                "Clocked in since 8:00 AM"
                            },
                            3)
                    },
                    4)
            });

        var (subject, html, text) = ClockAlertMail.Render(model);

        Assert.Equal("Clock alerts for Oct 27, 7:00 PM", subject);
        Assert.Contains("VisionPaint", text);
        Assert.Contains("North <house west", text);
        Assert.Contains("A<lex", text);
        Assert.Contains("http://localhost:5173/jobs/4", text);
        Assert.Contains("Lunch break open for 45 minutes", text);
        Assert.Contains("North &lt;house west", html);
        Assert.Contains("A&lt;lex", html);
        Assert.Contains("<h2>North &lt;house west</h2>", html);
        Assert.Contains("<strong>A&lt;lex</strong>", html);
        Assert.Contains("href=\"http://localhost:5173/jobs/4\"", html);
        Assert.DoesNotContain("North <house", html);
    }

    [Fact]
    public void Break_and_clock_lines()
    {
        Assert.Equal("Lunch break open for 45 minutes", ClockAlertMail.BreakLine("lunch", 45));
        Assert.Equal("Rest break open for 20 minutes", ClockAlertMail.BreakLine("rest", 20));
        Assert.Equal("Other break open for 30 minutes", ClockAlertMail.BreakLine("other", 30));
        var clockIn = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 10, 27, 16, 10, 0, DateTimeKind.Unspecified),
            Denver));
        Assert.Equal("Clocked in since 4:10 PM", ClockAlertMail.ClockedInSince(clockIn, Denver));
    }
}
