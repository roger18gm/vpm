using VisionPaint.Models;

namespace VisionPaint.Services;

public interface IJobAccessService
{
    Task<Job?> GetCompanyJobAsync(int jobId, CurrentUserContext user, CancellationToken cancellationToken = default);

    Task<bool> CanViewJobAsync(int jobId, CurrentUserContext user, CancellationToken cancellationToken = default);

    Task<bool> IsAssignedAsync(int jobId, int personId, CancellationToken cancellationToken = default);

    Task<bool> CanClockInAsync(int jobId, CurrentUserContext user, CancellationToken cancellationToken = default);

    IQueryable<Job> FilterJobsForUser(IQueryable<Job> query, CurrentUserContext user, bool includeCancelled);
}
