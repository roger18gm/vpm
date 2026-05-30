namespace VisionPaint.Services;

public interface IJobPhotoStorage
{
    Task<string> SaveAsync(
        int companyId,
        int jobId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<string> GetReadUrlAsync(string storagePath, CancellationToken cancellationToken = default);
}
