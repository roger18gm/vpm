using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class JobAccessService : IJobAccessService
{
    private static readonly HashSet<string> ClockableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "scheduled",
        "in_progress"
    };

    private readonly AppDbContext _db;
    private readonly ICompanyAuthorizationService _companyAuthorization;

    public JobAccessService(AppDbContext db, ICompanyAuthorizationService companyAuthorization)
    {
        _db = db;
        _companyAuthorization = companyAuthorization;
    }

    public async Task<Job?> GetCompanyJobAsync(int jobId, CurrentUserContext user, CancellationToken cancellationToken = default)
    {
        return await _db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(job => job.Id == jobId && job.CompanyId == user.CompanyId, cancellationToken);
    }

    public async Task<bool> CanViewJobAsync(int jobId, CurrentUserContext user, CancellationToken cancellationToken = default)
    {
        var job = await GetCompanyJobAsync(jobId, user, cancellationToken);
        if (job is null)
        {
            return false;
        }

        if (_companyAuthorization.IsManager(user))
        {
            return true;
        }

        return await IsAssignedAsync(jobId, user.PersonId, cancellationToken);
    }

    public async Task<bool> IsAssignedAsync(int jobId, int personId, CancellationToken cancellationToken = default)
    {
        return await _db.JobAssignments
            .AsNoTracking()
            .AnyAsync(
                assignment => assignment.JobId == jobId
                    && assignment.PersonId == personId
                    && assignment.UnassignedAt == null,
                cancellationToken);
    }

    public async Task<bool> CanClockInAsync(int jobId, CurrentUserContext user, CancellationToken cancellationToken = default)
    {
        var job = await GetCompanyJobAsync(jobId, user, cancellationToken);
        if (job is null || !ClockableStatuses.Contains(job.Status))
        {
            return false;
        }

        if (_companyAuthorization.IsManager(user))
        {
            return true;
        }

        return await IsAssignedAsync(jobId, user.PersonId, cancellationToken);
    }

    public IQueryable<Job> FilterJobsForUser(IQueryable<Job> query, CurrentUserContext user, bool includeCancelled)
    {
        query = query.Where(job => job.CompanyId == user.CompanyId);

        if (!includeCancelled)
        {
            query = query.Where(job => job.Status != "cancelled");
        }

        if (!_companyAuthorization.IsManager(user))
        {
            query = query.Where(job => _db.JobAssignments.Any(assignment =>
                assignment.JobId == job.Id
                && assignment.PersonId == user.PersonId
                && assignment.UnassignedAt == null));
        }

        return query;
    }
}
