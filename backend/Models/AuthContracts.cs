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
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

public sealed record BootstrapRequest(
    [property: Required] string Name,
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);
