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

    public async Task<(GustoEmployeeLinkDto? Result, string? Error, int StatusCode)> SaveAsync(
        CurrentUserContext user,
        int personId,
        SaveGustoEmployeeLinkRequest? request,
        CancellationToken cancellationToken)
    {
        if (!_authorization.IsAdmin(user))
        {
            return (null, "Only owners and admins can manage users.", 403);
        }

        var employeeUuid = request?.GustoEmployeeUuid?.Trim() ?? "";
        var jobUuid = request?.GustoJobUuid?.Trim() ?? "";
        if (employeeUuid.Length == 0 || jobUuid.Length == 0)
        {
            return (null, "Choose a Gusto employee and a compensation job.", 400);
        }

        var memberExists = await _db.CompanyMembers.AsNoTracking().AnyAsync(
            member => member.CompanyId == user.CompanyId
                && member.PersonId == personId
                && member.Status == "active",
            cancellationToken);
        if (!memberExists)
        {
            return (null, "Person not found.", 404);
        }

        IReadOnlyList<GustoDirectoryEmployee> employees;
        try
        {
            employees = await _gusto.ListEmployeesAsync(cancellationToken);
        }
        catch (GustoNotConfiguredException ex)
        {
            return (null, ex.Message, 503);
        }

        var employee = employees.FirstOrDefault(candidate => candidate.Uuid == employeeUuid);
        if (employee is null)
        {
            return (null, "That Gusto employee is not available to link.", 422);
        }

        if (employee.Jobs.All(job => job.Uuid != jobUuid))
        {
            return (null, "That compensation job does not belong to the selected employee.", 422);
        }

        var takenName = await LinkedEmployeeNameAsync(user.CompanyId, employeeUuid, personId, cancellationToken);
        if (takenName is not null)
        {
            return (null, $"This Gusto employee is already linked to {takenName}.", 409);
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.GustoEmployeeLinks.FirstOrDefaultAsync(
            link => link.PersonId == personId,
            cancellationToken);
        if (existing is null)
        {
            _db.GustoEmployeeLinks.Add(new GustoEmployeeLink
            {
                PersonId = personId,
                CompanyId = user.CompanyId,
                GustoEmployeeUuid = employeeUuid,
                GustoJobUuid = jobUuid,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else if (existing.CompanyId != user.CompanyId)
        {
            return (null, "Person not found.", 404);
        }
        else if (existing.GustoEmployeeUuid == employeeUuid && existing.GustoJobUuid == jobUuid)
        {
            return (new GustoEmployeeLinkDto(personId, employeeUuid, jobUuid), null, 200);
        }
        else
        {
            existing.GustoEmployeeUuid = employeeUuid;
            existing.GustoJobUuid = jobUuid;
            existing.UpdatedAt = now;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            var name = await LinkedEmployeeNameAsync(user.CompanyId, employeeUuid, personId, cancellationToken);
            return (null, $"This Gusto employee is already linked to {name ?? "another user"}.", 409);
        }

        return (new GustoEmployeeLinkDto(personId, employeeUuid, jobUuid), null, 200);
    }

    private async Task<string?> LinkedEmployeeNameAsync(
        int companyId,
        string employeeUuid,
        int personId,
        CancellationToken cancellationToken) =>
        await (
            from link in _db.GustoEmployeeLinks.AsNoTracking()
            join person in _db.People.AsNoTracking() on link.PersonId equals person.Id
            where link.CompanyId == companyId
                && link.GustoEmployeeUuid == employeeUuid
                && link.PersonId != personId
            select person.Name).FirstOrDefaultAsync(cancellationToken);

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
