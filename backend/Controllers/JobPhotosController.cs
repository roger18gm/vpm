using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

namespace VisionPaint.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs/{jobId:int}/photos")]
public sealed class JobPhotosController : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "before",
        "after",
        "progress"
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IJobAccessService _jobAccess;
    private readonly IJobPhotoStorage _photoStorage;

    public JobPhotosController(
        AppDbContext db,
        ICurrentUserService currentUserService,
        IJobAccessService jobAccess,
        IJobPhotoStorage photoStorage)
    {
        _db = db;
        _currentUserService = currentUserService;
        _jobAccess = jobAccess;
        _photoStorage = photoStorage;
    }

    [HttpGet]
    public async Task<ActionResult<List<JobPhotoDto>>> List(int jobId, CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!await _jobAccess.CanViewJobAsync(jobId, currentUser, cancellationToken))
        {
            return NotFound();
        }

        var photos = await _db.JobPhotos
            .AsNoTracking()
            .Where(photo => photo.JobId == jobId)
            .OrderByDescending(photo => photo.TakenAt ?? photo.CreatedAt)
            .ToListAsync(cancellationToken);

        var uploaderIds = photos
            .Where(photo => photo.UploadedByPersonId.HasValue)
            .Select(photo => photo.UploadedByPersonId!.Value)
            .Distinct()
            .ToList();

        var names = await _db.People
            .AsNoTracking()
            .Where(person => uploaderIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, person => person.Name, cancellationToken);

        var result = new List<JobPhotoDto>();
        foreach (var photo in photos)
        {
            var url = await _photoStorage.GetReadUrlAsync(photo.StoragePath, cancellationToken);
            var uploadedBy = photo.UploadedByPersonId is int personId
                ? names.GetValueOrDefault(personId, "Unknown")
                : "Unknown";

            result.Add(new JobPhotoDto(
                photo.Id,
                photo.JobId,
                photo.PhotoKind,
                photo.Caption,
                photo.TakenAt ?? photo.CreatedAt,
                uploadedBy,
                url));
        }

        return Ok(result);
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<JobPhotoDto>> Upload(
        int jobId,
        IFormFile file,
        [FromForm] string photoKind,
        [FromForm] string? caption,
        [FromForm] DateTimeOffset? takenAt,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        if (!await _jobAccess.CanViewJobAsync(jobId, currentUser, cancellationToken))
        {
            return NotFound();
        }

        var job = await _jobAccess.GetCompanyJobAsync(jobId, currentUser, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        if (job.Status == "cancelled")
        {
            return BadRequest(new { message = "Cannot upload photos to a cancelled job." });
        }

        if (file.Length == 0 || file.Length > 10 * 1024 * 1024)
        {
            return BadRequest(new { message = "Image must be between 1 byte and 10 MB." });
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return BadRequest(new { message = "Only JPEG, PNG, and WebP images are allowed." });
        }

        var kind = string.IsNullOrWhiteSpace(photoKind) ? "progress" : photoKind.Trim().ToLowerInvariant();
        if (!AllowedKinds.Contains(kind))
        {
            return BadRequest(new { message = "Invalid photo kind." });
        }

        await using var stream = file.OpenReadStream();
        var storagePath = await _photoStorage.SaveAsync(
            currentUser.CompanyId,
            jobId,
            file.FileName,
            file.ContentType,
            stream,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var photo = new JobPhoto
        {
            JobId = jobId,
            JobStatus = job.Status,
            UploadedByPersonId = currentUser.PersonId,
            PhotoKind = kind,
            StoragePath = storagePath,
            Caption = caption,
            TakenAt = takenAt ?? now,
            CreatedAt = now
        };

        _db.JobPhotos.Add(photo);
        await _db.SaveChangesAsync(cancellationToken);

        var url = await _photoStorage.GetReadUrlAsync(photo.StoragePath, cancellationToken);
        var dto = new JobPhotoDto(
            photo.Id,
            photo.JobId,
            photo.PhotoKind,
            photo.Caption,
            photo.TakenAt ?? photo.CreatedAt,
            currentUser.PersonName,
            url);

        return CreatedAtAction(nameof(List), new { jobId }, dto);
    }
}
