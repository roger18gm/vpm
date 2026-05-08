using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private const int DefaultCompanyId = 1;
    private readonly AppDbContext _context;

    public JobsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Job>>> GetJobs()
    {
        var jobs = await _context.Jobs.ToListAsync();
        return Ok(jobs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Job>> GetJob(int id)
    {
        var job = await _context.Jobs.FindAsync(id);
        if (job == null)
            return NotFound();
        return Ok(job);
    }

    [HttpPost]
    public async Task<ActionResult<Job>> CreateJob([FromBody] Job job)
    {
        var now = DateTime.UtcNow;
        job.CompanyId = DefaultCompanyId;
        job.Status = NormalizeStatus(job.Status);
        job.Priority = NormalizePriority(job.Priority);
        job.CreatedAt = now;
        job.UpdatedAt = now;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        _context.JobStatusHistories.Add(new JobStatusHistory
        {
            JobId = job.Id,
            FromStatus = null,
            ToStatus = job.Status,
            ChangedByPersonId = job.CreatedByPersonId,
            ChangedAt = now
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateJob(int id, [FromBody] Job updatedJob)
    {
        var job = await _context.Jobs.FindAsync(id);
        if (job == null)
            return NotFound();

        var now = DateTime.UtcNow;
        var previousStatus = job.Status;

        job.CompanyId = DefaultCompanyId;
        job.Title = updatedJob.Title;
        job.Description = updatedJob.Description;
        job.Status = NormalizeStatus(updatedJob.Status);
        job.Priority = NormalizePriority(updatedJob.Priority);
        job.AddressLine1 = updatedJob.AddressLine1;
        job.AddressLine2 = updatedJob.AddressLine2;
        job.City = updatedJob.City;
        job.StateRegion = updatedJob.StateRegion;
        job.PostalCode = updatedJob.PostalCode;
        job.CountryCode = updatedJob.CountryCode;
        job.ScheduledStartAt = updatedJob.ScheduledStartAt;
        job.ScheduledEndAt = updatedJob.ScheduledEndAt;
        job.DueDate = updatedJob.DueDate;
        job.StartedAt = updatedJob.StartedAt;
        job.CompletedAt = updatedJob.CompletedAt;
        job.ClosedAt = updatedJob.ClosedAt;
        job.UpdatedAt = now;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        _context.Jobs.Update(job);
        await _context.SaveChangesAsync();

        if (!string.Equals(previousStatus, job.Status, StringComparison.OrdinalIgnoreCase))
        {
            _context.JobStatusHistories.Add(new JobStatusHistory
            {
                JobId = job.Id,
                FromStatus = previousStatus,
                ToStatus = job.Status,
                ChangedByPersonId = updatedJob.CreatedByPersonId ?? job.CreatedByPersonId,
                ChangedAt = now
            });

            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteJob(int id)
    {
        var job = await _context.Jobs.FindAsync(id);
        if (job == null)
            return NotFound();

        _context.Jobs.Remove(job);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static string NormalizeStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "scheduled";

        return status.Trim().ToLowerInvariant().Replace(" ", "_");
    }

    private static string NormalizePriority(string priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
            return "normal";

        return priority.Trim().ToLowerInvariant();
    }
}
