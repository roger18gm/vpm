namespace VisionPaint.Services;

public sealed class CompanyAuthorizationService : ICompanyAuthorizationService
{
    private static readonly HashSet<string> ManagerRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "manager"
    };

    private static readonly HashSet<string> AdminRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin"
    };

    public bool IsManager(CurrentUserContext user) => IsManagerRole(user.CompanyRole);

    public bool IsManagerRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && ManagerRoles.Contains(role);

    public bool IsAdmin(CurrentUserContext user) => IsAdminRole(user.CompanyRole);

    public bool IsAdminRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && AdminRoles.Contains(role);
}
