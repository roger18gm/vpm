using Microsoft.AspNetCore.Mvc;
using VisionPaint.Models;

namespace VisionPaint.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private static List<Job> _jobs = new();

    [HttpGet]
    public ActionResult<IEnumerable<Job>> GetJobs()
    {
        return Ok(_jobs);
    }

    [HttpGet("{id}")]
    public ActionResult<Job> GetJob(int id)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == id);
        if (job == null)
            return NotFound();
        return Ok(job);
    }

    [HttpPost]
    public ActionResult<Job> CreateJob([FromBody] Job job)
    {
        job.Id = _jobs.Count > 0 ? _jobs.Max(j => j.Id) + 1 : 1;
        job.CreatedAt = DateTime.UtcNow;
        _jobs.Add(job);
        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
    }

    [HttpPut("{id}")]
    public IActionResult UpdateJob(int id, [FromBody] Job updatedJob)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == id);
        if (job == null)
            return NotFound();

        job.Title = updatedJob.Title;
        job.Description = updatedJob.Description;
        job.Status = updatedJob.Status;
        job.DueDate = updatedJob.DueDate;

        return NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteJob(int id)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == id);
        if (job == null)
            return NotFound();

        _jobs.Remove(job);
        return NoContent();
    }
}
