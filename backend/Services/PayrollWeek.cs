namespace VisionPaint.Services;

public static class PayrollWeek
{
    public static bool TryGetSendableWeek(DateTimeOffset utcNow, TimeZoneInfo timeZone, out DateOnly weekStart)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var localDate = DateOnly.FromDateTime(local.DateTime);
        var thisSunday = TimeZoneHelper.SundayOfWeek(localDate);
        weekStart = thisSunday.AddDays(-7);
        var saturday = weekStart.AddDays(6);
        var monday = saturday.AddDays(2);
        var sendOpensLocal = DateTime.SpecifyKind(monday.ToDateTime(new TimeOnly(7, 0)), DateTimeKind.Unspecified);
        var sendOpensUtc = TimeZoneInfo.ConvertTimeToUtc(sendOpensLocal, timeZone);
        return utcNow >= sendOpensUtc;
    }
}
