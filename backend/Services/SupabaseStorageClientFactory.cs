using Supabase.Storage;

namespace VisionPaint.Services;

public static class SupabaseStorageClientFactory
{
    public static Client Create(JobPhotoStorageOptions options)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {options.ServiceRoleKey}",
            ["apikey"] = options.ServiceRoleKey,
        };

        return new Client(ResolveStorageApiBase(options.Url), headers);
    }

    public static string ResolveStorageApiBase(string url)
    {
        var projectUrl = url.TrimEnd('/');
        if (projectUrl.EndsWith("/storage/v1", StringComparison.OrdinalIgnoreCase))
        {
            return projectUrl;
        }

        return $"{projectUrl}/storage/v1";
    }
}
