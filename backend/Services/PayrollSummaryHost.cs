using Microsoft.Extensions.Hosting;

namespace VisionPaint.Services;

public static class PayrollSummaryHost
{
    public static bool ShouldRun(IHostEnvironment environment) => environment.IsProduction();
}
