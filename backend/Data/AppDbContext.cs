using Microsoft.EntityFrameworkCore;
using VisionPaint.Models;

namespace VisionPaint.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuthUser> AuthUsers => Set<AuthUser>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<CompanyMember> CompanyMembers => Set<CompanyMember>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobStatusHistory> JobStatusHistories => Set<JobStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuthUser>(entity =>
        {
            entity.ToTable("auth_user");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(user => user.Email).HasColumnName("email");
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash");
            entity.Property(user => user.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(user => user.EmailConfirmedAt).HasColumnName("email_confirmed_at");
            entity.Property(user => user.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(user => user.Email).IsUnique();
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("company");
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(company => company.Name).HasColumnName("name");
            entity.Property(company => company.Timezone).HasColumnName("timezone");
            entity.Property(company => company.LanguageCode).HasColumnName("language_code");
            entity.Property(company => company.CreatedAt).HasColumnName("created_at");
            entity.Property(company => company.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<Person>(entity =>
        {
            entity.ToTable("person");
            entity.HasKey(person => person.Id);
            entity.Property(person => person.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(person => person.AuthUserId).HasColumnName("auth_user_id");
            entity.Property(person => person.Name).HasColumnName("name");
            entity.Property(person => person.Email).HasColumnName("email");
            entity.Property(person => person.Phone).HasColumnName("phone");
            entity.Property(person => person.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(person => person.CreatedAt).HasColumnName("created_at");
            entity.Property(person => person.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(person => person.AuthUserId).IsUnique();
        });

        modelBuilder.Entity<CompanyMember>(entity =>
        {
            entity.ToTable("company_member");
            entity.HasKey(member => new { member.CompanyId, member.PersonId });
            entity.Property(member => member.CompanyId).HasColumnName("company_id");
            entity.Property(member => member.PersonId).HasColumnName("person_id");
            entity.Property(member => member.Role).HasColumnName("role").HasDefaultValue("crew");
            entity.Property(member => member.Status).HasColumnName("status").HasDefaultValue("active");
            entity.Property(member => member.InvitedAt).HasColumnName("invited_at");
            entity.Property(member => member.JoinedAt).HasColumnName("joined_at");
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("job");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(job => job.CompanyId).HasColumnName("company_id").HasDefaultValue(1);
            entity.Property(job => job.ClientId).HasColumnName("client_id");
            entity.Property(job => job.CreatedByPersonId).HasColumnName("created_by_person_id");
            entity.Property(job => job.Title).HasColumnName("title");
            entity.Property(job => job.Description).HasColumnName("description");
            entity.Property(job => job.Status).HasColumnName("status").HasDefaultValue("scheduled");
            entity.Property(job => job.Priority).HasColumnName("priority").HasDefaultValue("normal");
            entity.Property(job => job.AddressLine1).HasColumnName("address_line1");
            entity.Property(job => job.AddressLine2).HasColumnName("address_line2");
            entity.Property(job => job.City).HasColumnName("city");
            entity.Property(job => job.StateRegion).HasColumnName("state_region");
            entity.Property(job => job.PostalCode).HasColumnName("postal_code");
            entity.Property(job => job.CountryCode).HasColumnName("country_code");
            entity.Property(job => job.ScheduledStartAt).HasColumnName("scheduled_start_at");
            entity.Property(job => job.ScheduledEndAt).HasColumnName("scheduled_end_at");
            entity.Property(job => job.DueAt).HasColumnName("due_at");
            entity.Property(job => job.StartedAt).HasColumnName("started_at");
            entity.Property(job => job.CompletedAt).HasColumnName("completed_at");
            entity.Property(job => job.ClosedAt).HasColumnName("closed_at");
            entity.Property(job => job.CreatedAt).HasColumnName("created_at");
            entity.Property(job => job.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<JobStatusHistory>(entity =>
        {
            entity.ToTable("job_status_history");
            entity.HasKey(history => history.Id);
            entity.Property(history => history.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(history => history.JobId).HasColumnName("job_id");
            entity.Property(history => history.FromStatus).HasColumnName("from_status");
            entity.Property(history => history.ToStatus).HasColumnName("to_status");
            entity.Property(history => history.ChangedByPersonId).HasColumnName("changed_by_person_id");
            entity.Property(history => history.ChangedAt).HasColumnName("changed_at");
            entity.Property(history => history.Reason).HasColumnName("reason");
            entity.Property(history => history.Notes).HasColumnName("notes");
        });
    }
}
