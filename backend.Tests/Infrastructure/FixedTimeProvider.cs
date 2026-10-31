namespace VisionPaint.Tests.Infrastructure;

public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        UtcNowValue = utcNow;
    }

    public DateTimeOffset UtcNowValue { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNowValue;
}
