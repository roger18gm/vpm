using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VisionPaint.Data;
using VisionPaint.Models;

namespace VisionPaint.Tests.Infrastructure;

public static class TestDataFactory
{
    public static async Task<(AuthTokenResponse Tokens, int PersonId)> CreateCrewUserAsync(
        BackendIntegrationFixture fixture,
        string email = "crew@example.com",
        string password = "Password123!",
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext(fixture.Database.ConnectionString);

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
        if (existing is not null)
        {
            var tokens = await fixture.AuthClient.LoginAsync(
                new LoginRequest(normalizedEmail, password),
                cancellationToken);
            var existingPerson = await db.People.AsNoTracking().FirstAsync(p => p.AuthUserId == existing.Id, cancellationToken);
            return (tokens, existingPerson.Id);
        }

        var passwordHasher = new PasswordHasher<AuthUser>();
        var authUser = new AuthUser
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = string.Empty,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        authUser.PasswordHash = passwordHasher.HashPassword(authUser, password);

        var person = new Person
        {
            AuthUserId = authUser.Id,
            Name = "Crew User",
            Email = normalizedEmail,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.AuthUsers.Add(authUser);
        db.People.Add(person);
        await db.SaveChangesAsync(cancellationToken);

        db.CompanyMembers.Add(new CompanyMember
        {
            CompanyId = 1,
            PersonId = person.Id,
            Role = "crew",
            Status = "active",
            JoinedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        var loginTokens = await fixture.AuthClient.LoginAsync(
            new LoginRequest(normalizedEmail, password),
            cancellationToken);

        return (loginTokens, person.Id);
    }

    private static AppDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
