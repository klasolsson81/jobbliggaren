using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR 0043 — fristående snapshot-tabell (Anticorruption Layer). Ingen FK
/// till job_ads/saved_searches (concept-id är lös referens — replika av
/// extern taxonomi). Seedas idempotent vid app-start (Variant A).
/// </summary>
internal sealed class TaxonomyConceptConfiguration
    : IEntityTypeConfiguration<TaxonomyConcept>
{
    public void Configure(EntityTypeBuilder<TaxonomyConcept> builder)
    {
        builder.ToTable("taxonomy_concepts");

        builder.HasKey(c => c.ConceptId);
        builder.Property(c => c.ConceptId)
            .HasMaxLength(32)            // speglar SearchCriteria concept-id-format
            .ValueGeneratedNever();

        builder.Property(c => c.Kind)
            .HasConversion<string>()     // läsbart i DB, stabilt mot enum-omordning
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Label)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.ParentConceptId)
            .HasMaxLength(32);

        // Picker-trädet byggs per Kind; yrken slås upp per yrkesområde.
        builder.HasIndex(c => c.Kind);
        builder.HasIndex(c => c.ParentConceptId);
    }
}
