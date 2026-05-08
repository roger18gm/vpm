using System.ComponentModel.DataAnnotations;

namespace VisionPaint.Models;

public sealed record AuthStatusResponse(bool IsAuthenticated, bool CanBootstrap, AuthenticatedUserResponse? User, string CsrfToken);

public sealed record AuthenticatedUserResponse(
    Guid AuthUserId,
    int PersonId,
    int CompanyId,
    string CompanyRole,
    string PersonName,
    string Email,
    string CsrfToken);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record BootstrapRequest(
    [Required] string Name,
    [Required, EmailAddress] string Email,
    [Required] string Password);
