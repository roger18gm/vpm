using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VisionPaint.Services;

public sealed class ClockAlertScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ClockAlertScheduler> _logger;
    private readonly TimeSpan _interval;

    public ClockAlertScheduler(
        IServiceScopeFactory scopes,
        ILogger<ClockAlertScheduler> logger,
        TimeSpan? interval = null)
    {
        _scopes = scopes;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IClockAlertService>();
                await service.SendDueAlertsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Clock alert check failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
