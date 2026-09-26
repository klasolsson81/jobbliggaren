using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1682 — schema for the occupation × SNI-division profile. Exists for the migration only; the
/// table is Infrastructure-internal, never a <c>DbSet</c> on <c>IAppDbContext</c>, and is reached by
/// raw SQL in <c>OccupationDivisionProfileStore</c> (ADR 0139's layer half).
///
/// <para>
/// No index beyond the composite primary key: the only read is
/// <c>occupation_group_concept_id = ANY(@ids)</c> over a handful of ids, which the key's leading
/// column serves, and an index nothing reads is write amplification on every rebuild. No foreign
/// key, no cascade, no user or criterion column — a corpus statistic, not personal data.
/// </para>
/// </summary>
internal sealed class OccupationDivisionProfileRowConfiguration
    : IEntityTypeConfiguration<OccupationDivisionProfileRow>
{
    public void Configure(EntityTypeBuilder<OccupationDivisionProfileRow> builder)
    {
        builder.ToTable("occupation_division_profiles");

        builder.HasKey(r => new { r.OccupationGroupConceptId, r.DivisionCode });

        builder.Property(r => r.OccupationGroupConceptId)
            .HasColumnName("occupation_group_concept_id")
            .IsRequired();

        // Two digits or a two-character sentinel; max length 2 makes a five-digit leaf code that
        // slipped past LEFT(code, 2) a constraint violation rather than a silent new bucket.
        builder.Property(r => r.DivisionCode)
            .HasColumnName("division_code")
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(r => r.AdCount)
            .HasColumnName("ad_count")
            .IsRequired();
    }
}
