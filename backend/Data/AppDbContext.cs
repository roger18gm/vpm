using Microsoft.EntityFrameworkCore;
using VisionPaint.Models;

namespace VisionPaint.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<JobStatusHistory> JobStatusHistories { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("job");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id").HasDefaultValue(1);
            entity.Property(e => e.ClientId).HasColumnName("client_id");
            entity.Property(e => e.CreatedByPersonId).HasColumnName("created_by_person_id");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("scheduled");
            entity.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue("normal");
            entity.Property(e => e.AddressLine1).HasColumnName("address_line1");
            entity.Property(e => e.AddressLine2).HasColumnName("address_line2");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.StateRegion).HasColumnName("state_region");
            entity.Property(e => e.PostalCode).HasColumnName("postal_code");
            entity.Property(e => e.CountryCode).HasColumnName("country_code");
            entity.Property(e => e.ScheduledStartAt).HasColumnName("scheduled_start_at");
            entity.Property(e => e.ScheduledEndAt).HasColumnName("scheduled_end_at");
            entity.Property(e => e.DueDate).HasColumnName("due_at");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.ClosedAt).HasColumnName("closed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<JobStatusHistory>(entity =>
        {
            entity.ToTable("job_status_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.JobId).HasColumnName("job_id");
            entity.Property(e => e.FromStatus).HasColumnName("from_status");
            entity.Property(e => e.ToStatus).HasColumnName("to_status").IsRequired();
            entity.Property(e => e.ChangedByPersonId).HasColumnName("changed_by_person_id");
            entity.Property(e => e.ChangedAt).HasColumnName("changed_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Notes).HasColumnName("notes");
        });
    }
}
