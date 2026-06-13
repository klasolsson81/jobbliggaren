using Jobbliggaren.Infrastructure.JobAds.SnapshotMisses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class JobAdSnapshotMissConfiguration : IEntityTypeConfiguration<JobAdSnapshotMiss>
{
    public void Configure(EntityTypeBuilder<JobAdSnapshotMiss> builder)
    {
        builder.ToTable("job_ad_snapshot_misses");

        // Composite PK (Source, ExternalId) — varje (källa, extern-id)
        // har max en rad. Postgres ON CONFLICT-upsert i tracker:n förlitar
        // sig på denna constraint.
        builder.HasKey(m => new { m.Source, m.ExternalId });

        builder.Property(m => m.Source)
            .HasColumnName("source")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.ExternalId)
            .HasColumnName("external_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.MissCount)
            .HasColumnName("miss_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.FirstMissedAt)
            .HasColumnName("first_missed_at");

        builder.Property(m => m.LastMissedAt)
            .HasColumnName("last_missed_at");

        // Partial index för retention-scanning: bara rader med faktiska
        // misses är intressanta. Hela tabellen scannas aldrig.
        builder.HasIndex(m => new { m.Source, m.MissCount })
            .HasFilter("\"miss_count\" >= 1")
            .HasDatabaseName("ix_job_ad_snapshot_misses_miss_count");
    }
}
