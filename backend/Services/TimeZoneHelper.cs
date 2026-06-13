namespace VisionPaint.Services;

public static class TimeZoneHelper
{
    public static TimeZoneInfo Resolve(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        }
    }

    public static DateOnly ToLocalDate(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

    public static DateOnly SundayOfWeek(DateOnly reference) =>
        reference.AddDays(-(int)reference.DayOfWeek);

    public static bool IsSunday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday;

    public static DateTimeOffset ToUtcStartOfDay(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    public static DateTimeOffset ToUtcEndOfDay(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(23, 59, 59)), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    public static string FormatDayLabel(DateOnly date) =>
        date.ToString("ddd M/d", System.Globalization.CultureInfo.InvariantCulture);
}
