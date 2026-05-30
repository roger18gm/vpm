namespace VisionPaint.Services;

public sealed class CompanyAuthorizationService : ICompanyAuthorizationService
{
    private static readonly HashSet<string> ManagerRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "manager"
    };

    public bool IsManager(CurrentUserContext user) => IsManagerRole(user.CompanyRole);

    public bool IsManagerRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && ManagerRoles.Contains(role);
}
