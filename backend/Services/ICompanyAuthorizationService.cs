namespace VisionPaint.Services;

public interface ICompanyAuthorizationService
{
    bool IsManager(CurrentUserContext user);

    bool IsManagerRole(string? role);

    bool IsAdmin(CurrentUserContext user);

    bool IsAdminRole(string? role);
}
