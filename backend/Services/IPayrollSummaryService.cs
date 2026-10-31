namespace VisionPaint.Services;

public interface IPayrollSummaryService
{
    Task SendDueSummariesAsync(CancellationToken cancellationToken);
}
