using Npgsql;

namespace VisionPaint.Tests.Infrastructure;

public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private PostgresTestDatabase(string adminConnectionString, string databaseName, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<PostgresTestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("VISIONPAINT_TEST_PGADMIN")
            ?? "Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres;Database=postgres";

        var databaseName = $"visionpaint_tests_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = "postgres"
        };

        try
        {
            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = $"create database \"{databaseName}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidPassword)
        {
            throw new InvalidOperationException(
                "Unable to connect to the local PostgreSQL test server. Set VISIONPAINT_TEST_PGADMIN to a connection string with CREATE DATABASE privileges.",
                ex);
        }

        var appBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            Database = databaseName
        };

        return new PostgresTestDatabase(adminBuilder.ConnectionString, databaseName, appBuilder.ConnectionString);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var terminateCommand = connection.CreateCommand();
        terminateCommand.CommandText = $"""
            select pg_terminate_backend(pg_stat_activity.pid)
            from pg_stat_activity
            where pg_stat_activity.datname = '{_databaseName.Replace("'", "''")}'
              and pid <> pg_backend_pid();
            """;
        await terminateCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var dropCommand = connection.CreateCommand();
        dropCommand.CommandText = $"drop database if exists \"{_databaseName}\"";
        await dropCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
