namespace VisionPaint.Services;

public sealed record CurrentUserContext(
    Guid AuthUserId,
    int PersonId,
    int CompanyId,
    string CompanyRole,
    string PersonName,
    string Email);
