using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

public sealed class ApplicationConfiguration : IEntityTypeConfiguration<DomainApplication>
{
    public void Configure(EntityTypeBuilder<DomainApplication> builder)
    {
        builder.ToTable("applications");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(
                id => id.Value,
                value => new JobbPilot.Domain.Applications.ApplicationId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();

        builder.Property(a => a.JobAdId)
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (JobAdId?)null : new JobAdId(value.Value));

        // TODO(GDPR): CoverLetter är känsligt innehåll (BUILD.md §13.1) — kryptera kolumnen i Fas 2
        builder.Property(a => a.CoverLetter).HasMaxLength(10_000);

        builder.Property(a => a.Status)
            .HasConversion(
                s => s.Name,
                v => ApplicationStatus.FromName(v))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();
        builder.Property(a => a.LastStatusChangeAt).IsRequired();
        builder.Property(a => a.GhostedThresholdDays)
            .IsRequired()
            .HasDefaultValue(21);
        builder.Property(a => a.DeletedAt);

        // xmin är PostgreSQL-systemkolumn — ingen DDL-kolumn behövs, Npgsql mappar automatiskt
        builder.Property<uint>("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasMany(a => a.FollowUps)
            .WithOne()
            .HasForeignKey("ApplicationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Notes)
            .WithOne()
            .HasForeignKey("ApplicationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(a => a.DeletedAt == null);

        builder.Ignore(a => a.DomainEvents);
    }
}
