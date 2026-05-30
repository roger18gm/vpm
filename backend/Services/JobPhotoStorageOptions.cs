namespace VisionPaint.Services;

public sealed class JobPhotoStorageOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;

    public string ServiceRoleKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "job-photos";
}
