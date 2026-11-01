namespace VisionPaint.Services;

public interface IClockAlertService
{
    Task SendDueAlertsAsync(CancellationToken cancellationToken);
}
