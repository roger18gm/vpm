namespace VisionPaint.Services;

public static class GustoHours
{
    public static decimal FromWorkMinutes(int workMinutes)
    {
        if (workMinutes < 0)
        {
            workMinutes = 0;
        }

        return Math.Round(workMinutes / 60m, 4, MidpointRounding.AwayFromZero);
    }
}