using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VisionPaint.Controllers;

[ApiController]
[Route("api/local-files")]
public sealed class LocalFilesController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public LocalFilesController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet("{*storagePath}")]
    [AllowAnonymous]
    public IActionResult Get(string storagePath)
    {
        if (!_environment.IsEnvironment("Testing")
            && !_environment.IsEnvironment("E2E")
            && !_environment.IsDevelopment())
        {
            return NotFound();
        }

        var root = Path.Combine(Path.GetTempPath(), "visionpaint-photos");
        var decoded = Uri.UnescapeDataString(storagePath);
        var fullPath = Path.Combine(root, decoded.Replace('/', Path.DirectorySeparatorChar));

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        return PhysicalFile(fullPath, contentType);
    }
}
