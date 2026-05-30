namespace VisionPaint.Services;

public sealed class LocalJobPhotoStorage : IJobPhotoStorage
{
    private readonly string _rootDirectory;

    public LocalJobPhotoStorage()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "visionpaint-photos");
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<string> SaveAsync(
        int companyId,
        int jobId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
        }

        var storagePath = $"{companyId}/jobs/{jobId}/{Guid.NewGuid():N}{extension}";
        var fullPath = GetFullPath(storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return storagePath;
    }

    public Task<string> GetReadUrlAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(storagePath);
        return Task.FromResult($"/api/local-files/{encoded}");
    }

    public string GetFullPath(string storagePath) => Path.Combine(_rootDirectory, storagePath.Replace('/', Path.DirectorySeparatorChar));
}
