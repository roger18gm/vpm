using Microsoft.Extensions.Hosting;

namespace VisionPaint.Services;

public static class ClockAlertHost
{
    public static bool ShouldRun(IHostEnvironment environment) => environment.IsProduction();
}
