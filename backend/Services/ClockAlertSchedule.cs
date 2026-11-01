namespace VisionPaint.Services;

public static class ClockAlertSchedule
{
    public const string Morning = "morning";
    public const string Evening = "evening";

    public readonly record struct Slot(DateOnly SlotOn, string Kind);

    public static Slot OpenSlot(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (time < new TimeOnly(7, 0))
        {
            return new Slot(date.AddDays(-1), Evening);
        }

        if (time < new TimeOnly(19, 0))
        {
            return new Slot(date, Morning);
        }

        return new Slot(date, Evening);
    }

    public static DateTimeOffset FollowUpInstant(DateTimeOffset firstSentAt, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(firstSentAt, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (time >= new TimeOnly(7, 0))
        {
            date = date.AddDays(1);
        }

        while (IsWeekend(date))
        {
            date = date.AddDays(1);
        }

        return AtLocal(date, new TimeOnly(7, 0), zone);
    }

    public static DateTimeOffset AtLocal(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static int ThresholdMinutes(string breakType) =>
        breakType.Equals("lunch", StringComparison.OrdinalIgnoreCase) ? 45 : 20;

    public static string BreakLabel(string breakType)
    {
        if (breakType.Equals("lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "Lunch";
        }

        if (breakType.Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            return "Other";
        }

        return "Rest";
    }

    public static int WholeMinutes(DateTimeOffset start, DateTimeOffset now) =>
        (int)Math.Max(0, (now - start).TotalMinutes);
}
