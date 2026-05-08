namespace VisionPaint.Models;

public sealed record AuthStatusResponse(bool IsAuthenticated, bool CanBootstrap, AuthenticatedUserResponse? User);

public sealed record AuthenticatedUserResponse(
    Guid AuthUserId,
    int PersonId,
    int CompanyId,
    string CompanyRole,
    string PersonName,
    string Email);

public sealed record LoginRequest(string Email, string Password);

public sealed record BootstrapRequest(string Name, string Email, string Password);
