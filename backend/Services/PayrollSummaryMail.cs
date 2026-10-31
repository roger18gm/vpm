using System.Globalization;
using System.Net;
using System.Text;

namespace VisionPaint.Services;

public sealed record PayrollDayModel(DateOnly Date, int WorkMinutes, int BreakMinutes);

public sealed record PayrollPersonModel(
    string Name,
    int WorkMinutes,
    int BreakMinutes,
    bool StillClockedIn,
    IReadOnlyList<PayrollDayModel> Days);

public sealed record PayrollSummaryModel(
    string CompanyName,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int CompanyWorkMinutes,
    int CompanyBreakMinutes,
    IReadOnlyList<PayrollPersonModel> People);

public static class PayrollSummaryMail
{
    public static string FormatHours(int minutes) =>
        (minutes / 60m).ToString("0.0", CultureInfo.InvariantCulture);

    public static string SubjectFor(DateOnly weekStart, DateOnly weekEnd)
    {
        var culture = CultureInfo.InvariantCulture;
        if (weekStart.Year == weekEnd.Year)
        {
            return $"Payroll summary for {weekStart.ToString("MMM d", culture)}–{weekEnd.ToString("MMM d", culture)}, {weekEnd.Year}";
        }

        return $"Payroll summary for {weekStart.ToString("MMM d, yyyy", culture)}–{weekEnd.ToString("MMM d, yyyy", culture)}";
    }

    public static (string Subject, string Html, string Text) Render(PayrollSummaryModel model)
    {
        var subject = SubjectFor(model.WeekStart, model.WeekEnd);
        var text = new StringBuilder();
        text.AppendLine(model.CompanyName);
        text.AppendLine(subject);
        text.AppendLine();
        foreach (var person in model.People)
        {
            text.AppendLine(person.Name);
            text.Append("Work ").Append(FormatHours(person.WorkMinutes));
            text.Append("  Break ").Append(FormatHours(person.BreakMinutes));
            if (person.StillClockedIn)
            {
                text.Append("  Still clocked in");
            }

            text.AppendLine();
            foreach (var day in person.Days)
            {
                text.Append(TimeZoneHelper.FormatDayLabel(day.Date));
                text.Append("  Work ").Append(FormatHours(day.WorkMinutes));
                text.Append("  Break ").Append(FormatHours(day.BreakMinutes));
                text.AppendLine();
            }

            text.AppendLine();
        }

        text.AppendLine("Company total");
        text.Append("Work ").Append(FormatHours(model.CompanyWorkMinutes));
        text.Append("  Break ").Append(FormatHours(model.CompanyBreakMinutes));
        text.AppendLine();

        var html = new StringBuilder();
        html.Append("<p>").Append(WebUtility.HtmlEncode(model.CompanyName)).Append("</p>");
        html.Append("<p>").Append(WebUtility.HtmlEncode(subject)).Append("</p><table>");
        foreach (var person in model.People)
        {
            html.Append("<tr><th>").Append(WebUtility.HtmlEncode(person.Name)).Append("</th>");
            html.Append("<td>Work ").Append(FormatHours(person.WorkMinutes)).Append("</td>");
            html.Append("<td>Break ").Append(FormatHours(person.BreakMinutes)).Append("</td>");
            if (person.StillClockedIn)
            {
                html.Append("<td>Still clocked in</td>");
            }

            html.Append("</tr>");
            foreach (var day in person.Days)
            {
                html.Append("<tr><td>").Append(WebUtility.HtmlEncode(TimeZoneHelper.FormatDayLabel(day.Date))).Append("</td>");
                html.Append("<td>Work ").Append(FormatHours(day.WorkMinutes)).Append("</td>");
                html.Append("<td>Break ").Append(FormatHours(day.BreakMinutes)).Append("</td></tr>");
            }
        }

        html.Append("<tr><th>Company total</th><td>Work ");
        html.Append(FormatHours(model.CompanyWorkMinutes));
        html.Append("</td><td>Break ");
        html.Append(FormatHours(model.CompanyBreakMinutes));
        html.Append("</td></tr></table>");
        return (subject, html.ToString(), text.ToString());
    }
}
