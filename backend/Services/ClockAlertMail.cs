using System.Globalization;
using System.Net;
using System.Text;

namespace VisionPaint.Services;

public sealed record ClockAlertPersonModel(string Name, IReadOnlyList<string> Lines, int PersonId);

public sealed record ClockAlertJobModel(
    string Title,
    string Link,
    IReadOnlyList<ClockAlertPersonModel> People,
    int JobId);

public sealed record ClockAlertMailModel(
    string CompanyName,
    DateOnly SlotDate,
    bool Morning,
    IReadOnlyList<ClockAlertJobModel> Jobs);

public static class ClockAlertMail
{
    public static string SubjectFor(DateOnly slotDate, bool morning)
    {
        var culture = CultureInfo.InvariantCulture;
        var clock = morning ? "7:00 AM" : "7:00 PM";
        return $"Clock alerts for {slotDate.ToString("MMM d", culture)}, {clock}";
    }

    public static string BreakLine(string breakType, int minutes) =>
        $"{ClockAlertSchedule.BreakLabel(breakType)} break open for {minutes} minutes";

    public static string ClockedInSince(DateTimeOffset clockInAt, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(clockInAt, zone);
        return "Clocked in since " + local.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    public static string Flatten(string value) =>
        value.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");

    public static (string Subject, string Html, string Text) Render(ClockAlertMailModel model)
    {
        var subject = SubjectFor(model.SlotDate, model.Morning);
        var text = new StringBuilder();
        text.AppendLine(Flatten(model.CompanyName));
        text.AppendLine(subject);
        text.AppendLine();

        var html = new StringBuilder();
        html.Append("<p>").Append(Encode(model.CompanyName)).Append("</p>");
        html.Append("<p>").Append(Encode(subject)).Append("</p>");
        foreach (var job in model.Jobs)
        {
            text.AppendLine(Flatten(job.Title));
            text.AppendLine(job.Link);
            var encodedLink = WebUtility.HtmlEncode(job.Link);
            html.Append("<h2>").Append(Encode(job.Title)).Append("</h2>");
            html.Append("<p><a href=\"").Append(encodedLink).Append("\">").Append(encodedLink).Append("</a></p>");
            foreach (var person in job.People)
            {
                text.AppendLine(Flatten(person.Name));
                html.Append("<p><strong>").Append(Encode(person.Name)).Append("</strong></p>");
                foreach (var line in person.Lines)
                {
                    text.AppendLine(line);
                    html.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
                }

                text.AppendLine();
            }
        }

        return (subject, html.ToString(), text.ToString());
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(Flatten(value));
}
