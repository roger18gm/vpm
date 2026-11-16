using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisionPaint.Services;

namespace VisionPaint.Tests.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly bool _gustoConfigured;
    private readonly string? _gustoBaseUrl;

    public TestWebApplicationFactory(
        string connectionString,
        bool gustoConfigured = true,
        string? gustoBaseUrl = null)
    {
        _connectionString = connectionString;
        _gustoConfigured = gustoConfigured;
        _gustoBaseUrl = gustoBaseUrl;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var gustoSettings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["App:FrontendBaseUrl"] = "http://localhost:5173",
                ["Auth:PasswordResetTokenHours"] = "1"
            };
            if (_gustoConfigured)
            {
                gustoSettings["Gusto:BaseUrl"] = _gustoBaseUrl ?? GustoOptions.DemoBaseUrl;
                gustoSettings["Gusto:ClientId"] = "client-id";
                gustoSettings["Gusto:ClientSecret"] = "client-secret";
                gustoSettings["Gusto:RefreshToken"] = "refresh-token";
                gustoSettings["Gusto:CompanyUuid"] = "company-uuid";
                gustoSettings["Gusto:ApiVersion"] = "2026-06-15";
            }

            configBuilder.AddInMemoryCollection(gustoSettings);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<RecordingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());
            services.AddSingleton<StubGustoHandler>();
            services.AddHttpClient(GustoTimeClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<StubGustoHandler>());
        });
    }
}
