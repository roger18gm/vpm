using Microsoft.Extensions.Options;
using VisionPaint.Services;
using Xunit;

namespace VisionPaint.Tests;

public sealed class SupabaseJobPhotoStorageTests
{
    [Fact]
    public void ResolveStorageApiBase_appends_storage_v1_when_missing()
    {
        Assert.Equal(
            "https://example.supabase.co/storage/v1",
            SupabaseStorageClientFactory.ResolveStorageApiBase("https://example.supabase.co"));
    }

    [Fact]
    public void ResolveStorageApiBase_preserves_existing_storage_v1_suffix()
    {
        Assert.Equal(
            "https://example.supabase.co/storage/v1",
            SupabaseStorageClientFactory.ResolveStorageApiBase("https://example.supabase.co/storage/v1"));
    }

    [Fact]
    public async Task GetReadUrlAsync_public_bucket_returns_public_object_url()
    {
        var client = SupabaseStorageClientFactory.Create(new JobPhotoStorageOptions
        {
            Url = "https://example.supabase.co/storage/v1",
            ServiceRoleKey = "secret-key",
            Bucket = "job-photos"
        });

        var storage = new SupabaseJobPhotoStorage(
            client,
            Options.Create(new JobPhotoStorageOptions
            {
                Url = "https://example.supabase.co/storage/v1",
                ServiceRoleKey = "secret-key",
                Bucket = "job-photos",
                PublicBucket = true
            }));

        var url = await storage.GetReadUrlAsync("1/jobs/9/photo.png");

        Assert.StartsWith(
            "https://example.supabase.co/storage/v1/object/public/job-photos/1/jobs/9/photo.png",
            url);
    }

    [Fact]
    public void NormalizeSignedUrl_strips_trailing_question_mark_from_library_urls()
    {
        var url = SupabaseJobPhotoStorage.NormalizeSignedUrl(
            "https://example.supabase.co/storage/v1/object/sign/job-photos/1/jobs/7/photo.png?token=abc?");

        Assert.Equal(
            "https://example.supabase.co/storage/v1/object/sign/job-photos/1/jobs/7/photo.png?token=abc",
            url);
    }

    [Fact]
    public async Task GetReadUrlAsync_throws_when_supabase_is_not_configured()
    {
        var client = SupabaseStorageClientFactory.Create(new JobPhotoStorageOptions
        {
            Url = "https://example.supabase.co",
            ServiceRoleKey = "secret-key",
            Bucket = "job-photos"
        });

        var storage = new SupabaseJobPhotoStorage(
            client,
            Options.Create(new JobPhotoStorageOptions
            {
                Url = "",
                ServiceRoleKey = "",
                Bucket = "job-photos"
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.GetReadUrlAsync("1/jobs/9/photo.png"));

        Assert.Equal("Supabase storage is not configured.", exception.Message);
    }
}
