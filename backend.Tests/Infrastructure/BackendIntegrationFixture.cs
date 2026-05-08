using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisionPaint.Tests.Infrastructure;

public sealed class BackendIntegrationFixture : IAsyncLifetime
{
    private HttpClient? _client;

    public PostgresTestDatabase Database { get; private set; } = null!;

    public TestWebApplicationFactory Factory { get; private set; } = null!;

    public TestAuthClient AuthClient { get; private set; } = null!;

    public HttpClient Client => _client ?? throw new InvalidOperationException("Test client has not been initialized.");

    public async Task InitializeAsync()
    {
        Database = await PostgresTestDatabase.CreateAsync();
        await TestDatabaseInitializer.InitializeAsync(Database.ConnectionString);

        Factory = new TestWebApplicationFactory(Database.ConnectionString);
        _client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        AuthClient = new TestAuthClient(_client);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        Factory?.Dispose();
        if (Database is not null)
        {
            await Database.DisposeAsync();
        }
    }
}
