using Microsoft.Extensions.Options;
using Supabase.Storage;
using Supabase.Storage.Exceptions;

namespace VisionPaint.Services;

public sealed class SupabaseJobPhotoStorage : IJobPhotoStorage
{
    private const int DefaultSignedUrlExpirySeconds = 3600;

    private readonly Client _client;
    private readonly JobPhotoStorageOptions _options;

    public SupabaseJobPhotoStorage(Client client, IOptions<JobPhotoStorageOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<string> SaveAsync(
        int companyId,
        int jobId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var storagePath = BuildStoragePath(companyId, jobId, extension);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var bucket = _client.From(_options.Bucket);
        try
        {
            await bucket.Upload(
                buffer.ToArray(),
                storagePath,
                new Supabase.Storage.FileOptions { Upsert = false, ContentType = contentType },
                inferContentType: false,
                cancellationToken: cancellationToken);
        }
        catch (SupabaseStorageException ex)
        {
            throw new InvalidOperationException($"Supabase upload failed: {ex.Message}", ex);
        }

        return storagePath;
    }

    public async Task<string> GetReadUrlAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = NormalizeStoragePath(storagePath);
        var bucket = _client.From(_options.Bucket);

        if (_options.PublicBucket)
        {
            return bucket.GetPublicUrl(path, transformOptions: null);
        }

        try
        {
            return await bucket.CreateSignedUrl(path, DefaultSignedUrlExpirySeconds);
        }
        catch (SupabaseStorageException ex)
        {
            throw new InvalidOperationException($"Supabase sign failed for '{storagePath}': {ex.Message}", ex);
        }
    }

    private static string BuildStoragePath(int companyId, int jobId, string extension)
    {
        return $"{companyId}/jobs/{jobId}/{Guid.NewGuid():N}{extension}";
    }

    private static string NormalizeStoragePath(string storagePath)
    {
        return storagePath.Trim().TrimStart('/');
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.ServiceRoleKey))
        {
            throw new InvalidOperationException("Supabase storage is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Bucket))
        {
            throw new InvalidOperationException("Supabase storage bucket is not configured.");
        }
    }
}
