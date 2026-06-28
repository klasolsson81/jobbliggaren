using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR 0084 — standalone relation table (Anticorruption Layer). A directed edge
/// (source ssyk-4 → related ssyk-4) keyed on the composite triple; no FK to
/// taxonomy_concepts/job_ads (concept-ids are loose references — replica of
/// external taxonomy, parity <see cref="TaxonomyConceptConfiguration"/>).
/// Seeded idempotently at app-start by <c>TaxonomySnapshotSeeder</c> (Variant A,
/// composite-version-gated re-seed).
/// </summary>
internal sealed class TaxonomyRelationConfiguration
    : IEntityTypeConfiguration<TaxonomyRelation>
{
    public void Configure(EntityTypeBuilder<TaxonomyRelation> builder)
    {
        builder.ToTable("taxonomy_relations");

        // Composite PK — a directed edge is unique per (source, related, kind).
        // Its leading column (source_concept_id) is the lookup direction, so the
        // PK B-tree already serves the "related-by-source" prefix scan; no
        // separate secondary index is added (it would be redundant, and the whole
        // table is read once into the singleton in-memory cache anyway —
        // TaxonomyReadModel.LoadAsync, no per-request DB hit).
        builder.HasKey(r => new { r.SourceConceptId, r.RelatedConceptId, r.Kind });

        builder.Property(r => r.SourceConceptId)
            .HasMaxLength(32)            // mirrors the concept-id format (taxonomy_concepts)
            .ValueGeneratedNever();

        builder.Property(r => r.RelatedConceptId)
            .HasMaxLength(32)
            .ValueGeneratedNever();

        builder.Property(r => r.Kind)
            .HasConversion<string>()     // readable in DB, stable against enum reordering
            .HasMaxLength(20)
            .IsRequired();
    }
}
