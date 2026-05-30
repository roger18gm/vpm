using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

internal static class JobAssignmentHelper
{
    public static async Task<IReadOnlyList<JobAssignmentDto>> LoadActiveAssignmentsAsync(
        AppDbContext db,
        int jobId,
        CancellationToken cancellationToken)
    {
        return await (
            from assignment in db.JobAssignments.AsNoTracking()
            join person in db.People.AsNoTracking() on assignment.PersonId equals person.Id
            where assignment.JobId == jobId && assignment.UnassignedAt == null
            orderby person.Name
            select new JobAssignmentDto(
                assignment.PersonId,
                person.Name,
                assignment.AssignmentRole,
                assignment.AssignedAt)
        ).ToListAsync(cancellationToken);
    }

    public static JobDetailResponse ToDetailResponse(Job job, IReadOnlyList<JobAssignmentDto> assignments)
    {
        return new JobDetailResponse(
            job.Id,
            job.CompanyId,
            job.ClientId,
            job.CreatedByPersonId,
            job.Title,
            job.Description,
            job.Status,
            job.Priority,
            job.AddressLine1,
            job.AddressLine2,
            job.City,
            job.StateRegion,
            job.PostalCode,
            job.CountryCode,
            job.ScheduledStartAt,
            job.ScheduledEndAt,
            job.DueAt,
            job.StartedAt,
            job.CompletedAt,
            job.ClosedAt,
            job.CreatedAt,
            job.UpdatedAt,
            assignments);
    }
}
