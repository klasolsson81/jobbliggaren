using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedJobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// F6 P5 Punkt 2 Del A — EF Core-konfiguration för SavedJobAds (bokmärken).
/// Paritet med <see cref="RecentJobSearchConfiguration"/> + <see cref="SavedSearchConfiguration"/>:
/// strongly-typed ID-conversion, ingen DB-FK (ADR 0011 soft-references),
/// cascade-rensning sker explicit i <c>AccountHardDeleter</c> (ADR 0024 amend).
/// UNIQUE(JobSeekerId, JobAdId) bär idempotens-invarianten.
/// </summary>
public sealed class SavedJobAdConfiguration : IEntityTypeConfiguration<SavedJobAd>
{
    public void Configure(EntityTypeBuilder<SavedJobAd> builder)
    {
        builder.ToTable("saved_job_ads");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new SavedJobAdId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .HasColumnName("job_seeker_id")
            .IsRequired();

        builder.Property(s => s.JobAdId)
            .HasConversion(id => id.Value, value => new JobAdId(value))
            .HasColumnName("job_ad_id")
            .IsRequired();

        // UNIQUE(job_seeker_id, job_ad_id) — idempotens-invariant; samma
        // (seeker, annons) får sparas högst en gång. SaveJobAdCommandHandler
        // använder ADR 0032 §5 ON CONFLICT-mönstret för race-säker INSERT.
        builder.HasIndex(s => new { s.JobSeekerId, s.JobAdId })
            .IsUnique()
            .HasDatabaseName("ux_saved_job_ads_seeker_jobad");

        // Sekundär-index för list-query (ORDER BY created_at DESC scoped på seeker).
        builder.HasIndex(s => new { s.JobSeekerId, s.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_saved_job_ads_seeker_created_at");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Ignore(s => s.DomainEvents);
    }
}
