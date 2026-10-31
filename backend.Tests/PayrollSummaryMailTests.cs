using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class PayrollSummaryMailTests
{
    [Fact]
    public void FormatHours_uses_one_decimal_from_minutes()
    {
        Assert.Equal("0.0", PayrollSummaryMail.FormatHours(0));
        Assert.Equal("1.5", PayrollSummaryMail.FormatHours(90));
    }

    [Fact]
    public void Subject_uses_one_year_inside_a_calendar_year()
    {
        var subject = PayrollSummaryMail.SubjectFor(new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 24));
        Assert.Equal("Payroll summary for Oct 18–Oct 24, 2026", subject);
    }

    [Fact]
    public void Subject_repeats_the_year_when_the_week_crosses_years()
    {
        var subject = PayrollSummaryMail.SubjectFor(new DateOnly(2026, 12, 27), new DateOnly(2027, 1, 2));
        Assert.Equal("Payroll summary for Dec 27, 2026–Jan 2, 2027", subject);
    }

    [Fact]
    public void Render_lists_seven_days_zero_hours_and_an_open_shift()
    {
        var days = Enumerable.Range(0, 7)
            .Select(i => new PayrollDayModel(new DateOnly(2026, 10, 18).AddDays(i), 0, 0))
            .ToArray();
        days[6] = days[6] with { WorkMinutes = 2910 };

        var model = new PayrollSummaryModel(
            "VisionPaint",
            new DateOnly(2026, 10, 18),
            new DateOnly(2026, 10, 24),
            3000,
            0,
            new[]
            {
                new PayrollPersonModel("Open Worker", 2910, 0, true, days)
            });

        var (subject, html, text) = PayrollSummaryMail.Render(model);

        Assert.Equal("Payroll summary for Oct 18–Oct 24, 2026", subject);
        Assert.Contains("VisionPaint", text);
        Assert.Contains("Open Worker", text);
        Assert.Contains("Work 48.5  Break 0.0  Still clocked in", text);
        Assert.Contains("Sun 10/18  Work 0.0  Break 0.0", text);
        Assert.Contains("Sat 10/24  Work 48.5  Break 0.0", text);
        Assert.Contains("Company total", text);
        var afterTotal = text.Split("Company total")[1];
        Assert.Contains("Work 50.0", afterTotal);
        Assert.DoesNotContain("Work 50.0", text.Split("Company total")[0]);
        Assert.Contains("Still clocked in", html);
        Assert.Contains("48.5", html);
    }
}
