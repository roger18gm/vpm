using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class PayrollSummarySchedulerTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("E2E", false)]
    public void ShouldRun_is_production_only(string environmentName, bool expected)
    {
        var environment = new StubEnvironment { EnvironmentName = environmentName };
        Assert.Equal(expected, PayrollSummaryHost.ShouldRun(environment));
    }

    [Fact]
    public async Task Scheduler_checks_once_before_waiting()
    {
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton<IPayrollSummaryService>(new CallbackSummaryService(() =>
        {
            calls++;
            gate.TrySetResult();
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider();
        var scheduler = new PayrollSummaryScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PayrollSummaryScheduler>.Instance,
            TimeSpan.FromHours(1));

        await scheduler.StartAsync(CancellationToken.None);
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        await scheduler.StopAsync(CancellationToken.None);
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "VisionPaint.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CallbackSummaryService : IPayrollSummaryService
    {
        private readonly Func<Task> _send;

        public CallbackSummaryService(Func<Task> send)
        {
            _send = send;
        }

        public Task SendDueSummariesAsync(CancellationToken cancellationToken) => _send();
    }
}
