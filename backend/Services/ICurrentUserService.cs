namespace VisionPaint.Services;

public interface ICurrentUserService
{
    Task<CurrentUserContext?> GetAsync(CancellationToken cancellationToken = default);
}
