using JobbPilot.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

/// <summary>ADR 0043 — enradig idempotens-markör (en rad, PK=1).</summary>
internal sealed class TaxonomySnapshotMetaConfiguration
    : IEntityTypeConfiguration<TaxonomySnapshotMeta>
{
    public void Configure(EntityTypeBuilder<TaxonomySnapshotMeta> builder)
    {
        builder.ToTable("taxonomy_snapshot_meta", t =>
            t.HasCheckConstraint("ck_taxonomy_snapshot_meta_singleton", "id = 1"));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.TaxonomyVersion)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.SeededAt).IsRequired();
    }
}
