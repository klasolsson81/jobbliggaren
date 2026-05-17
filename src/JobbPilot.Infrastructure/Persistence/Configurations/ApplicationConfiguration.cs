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

        // ManualPosting — optional owned entity (manuell ansökan utan JobAd).
        // Explicit HasColumnName krävs: global UseSnakeCaseNamingConvention
        // skulle annars ge manual_posting_* (navigation-prefix). Samma mönster
        // som External owned-type på JobAd. IsRequired(false) obligatorisk —
        // EF Core 10 default för owned-referens är required; utan denna kan EF
        // ej skilja "ingen ManualPosting" från "all-null ManualPosting".
        builder.OwnsOne(a => a.ManualPosting, manual =>
        {
            manual.Property(m => m.Title)
                .HasColumnName("manual_title")
                .HasMaxLength(300);
            manual.Property(m => m.Company)
                .HasColumnName("manual_company")
                .HasMaxLength(200);
            manual.Property(m => m.Url)
                .HasColumnName("manual_url")
                .HasMaxLength(2000);
            manual.Property(m => m.ExpiresAt)
                .HasColumnName("manual_expires_at");
        });
        builder.Navigation(a => a.ManualPosting).IsRequired(false);

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
