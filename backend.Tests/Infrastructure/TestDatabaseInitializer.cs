using Microsoft.EntityFrameworkCore;
using Npgsql;
using VisionPaint.Data;

namespace VisionPaint.Tests.Infrastructure;

public static class TestDatabaseInitializer
{
    public static async Task InitializeAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await ExecuteMigrationScriptsAsync(connectionString, cancellationToken);
    }

    public static async Task ResetAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext(connectionString);
        await db.Database.ExecuteSqlRawAsync(
            """
            truncate table public.refresh_token,
                          public.job_status_history,
                          public.job,
                          public.company_member,
                          public.person,
                          public.auth_user
            restart identity cascade;
            """,
            cancellationToken);
    }

    private static AppDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }

    private static async Task ExecuteMigrationScriptsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var migrationDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "migrations"));

        foreach (var migrationPath in Directory.GetFiles(migrationDirectory, "*.sql").OrderBy(path => path))
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(migrationPath, cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
