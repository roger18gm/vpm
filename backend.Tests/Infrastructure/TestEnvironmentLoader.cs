using System.Runtime.CompilerServices;

namespace VisionPaint.Tests.Infrastructure;

/// <summary>
/// Loads backend.Tests/.env into environment variables before tests run.
/// dotnet test does not read .env files automatically.
/// </summary>
public static class TestEnvironmentLoader
{
    private static bool _loaded;

    [ModuleInitializer]
    internal static void Initialize() => Load();

    public static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        var envPath = FindEnvFilePath();
        if (envPath is null)
        {
            return;
        }

        foreach (var line in File.ReadAllLines(envPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            value = Unquote(value);

            if (string.IsNullOrEmpty(key) || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindEnvFilePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var envPath = Path.Combine(directory.FullName, ".env");
            if (File.Exists(envPath))
            {
                return envPath;
            }

            if (File.Exists(Path.Combine(directory.FullName, "backend.Tests.csproj")))
            {
                return null;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            if ((value.StartsWith('\'') && value.EndsWith('\'')) ||
                (value.StartsWith('"') && value.EndsWith('"')))
            {
                return value[1..^1];
            }
        }

        return value;
    }
}
