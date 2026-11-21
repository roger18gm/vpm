using Microsoft.EntityFrameworkCore;
using Npgsql;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Services;

public sealed class GustoEmployeeLinkService
{
    private readonly AppDbContext _db;
    private readonly ICompanyAuthorizationService _authorization;
    private readonly IGustoTimeClient _gusto;

    public GustoEmployeeLinkService(
        AppDbContext db,
        ICompanyAuthorizationService authorization,
        IGustoTimeClient gusto)
    {
        _db = db;
        _authorization = authorization;
        _gusto = gusto;
    }

    public async Task<(GustoEmployeeDirectoryDto? Result, string? Error, int StatusCode)> GetDirectoryAsync(
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        if (!_authorization.IsAdmin(user))
        {
            return (null, "Only owners and admins can manage users.", 403);
        }

        try
        {
            var employees = await _gusto.ListEmployeesAsync(cancellationToken);
            var links = await LinksForCompanyAsync(user.CompanyId, cancellationToken);
            return (new GustoEmployeeDirectoryDto(ToDtos(employees), links), null, 200);
        }
        catch (GustoNotConfiguredException ex)
        {
            return (null, ex.Message, 503);
        }
    }

    public Task<(GustoEmployeeLinkDto? Result, string? Error, int StatusCode)> SaveAsync(
        CurrentUserContext user,
        int personId,
        SaveGustoEmployeeLinkRequest? request,
        CancellationToken cancellationToken) =>
        Task.FromResult<(GustoEmployeeLinkDto?, string?, int)>((null, "Choose a Gusto employee and a compensation job.", 400));

    internal static List<GustoDirectoryEmployeeDto> ToDtos(IReadOnlyList<GustoDirectoryEmployee> employees) =>
        employees
            .OrderBy(employee => $"{employee.FirstName} {employee.LastName}".Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(employee => employee.Uuid, StringComparer.Ordinal)
            .Select(employee => new GustoDirectoryEmployeeDto(
                employee.Uuid,
                employee.FirstName,
                employee.LastName,
                employee.Email,
                employee.Jobs.Select(job => new GustoDirectoryJobDto(job.Uuid, job.Title, job.Primary)).ToList()))
            .ToList();

    private async Task<List<GustoEmployeeLinkDto>> LinksForCompanyAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        await _db.GustoEmployeeLinks.AsNoTracking()
            .Where(link => link.CompanyId == companyId)
            .OrderBy(link => link.PersonId)
            .Select(link => new GustoEmployeeLinkDto(link.PersonId, link.GustoEmployeeUuid, link.GustoJobUuid))
            .ToListAsync(cancellationToken);

    internal static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }
}
