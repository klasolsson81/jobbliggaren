using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #560 (ADR 0091) — EF Core configuration for the <c>company_register</c> read-model replica
/// (parity <c>TaxonomyConceptConfiguration</c>, ADR 0043). The table is NOT a <c>DbSet</c> on
/// <c>IAppDbContext</c> — it is reached only via the concrete <c>AppDbContext.Set&lt;T&gt;()</c>
/// inside Infrastructure, and populated via the batched raw-SQL <c>ON CONFLICT</c> upsert. This
/// configuration exists for the migration schema + read materialization; it must live in the
/// <c>...Persistence.Configurations</c> namespace (that is what <c>AppDbContext.OnModelCreating</c>
/// scans).
/// </summary>
internal sealed class ScbCompanyRegisterEntryConfiguration
    : IEntityTypeConfiguration<ScbCompanyRegisterEntry>
{
    public void Configure(EntityTypeBuilder<ScbCompanyRegisterEntry> builder)
    {
        builder.ToTable("company_register");

        // 10-digit org.nr is the natural key (Evans natural identity — no surrogate; parity ADR 0087
        // D2's read-model identity). Plaintext varchar(10), no hyphen.
        builder.HasKey(c => c.OrganizationNumber);
        builder.Property(c => c.OrganizationNumber)
            .HasColumnName("organization_number")
            .HasMaxLength(10)
            .ValueGeneratedNever();

        builder.Property(c => c.Name)
            .HasColumnName("company_name")
            .IsRequired();

        builder.Property(c => c.SeatMunicipalityCode)
            .HasColumnName("sate_kommun_code")
            .HasMaxLength(4)
            .IsRequired();

        builder.Property(c => c.SeatMunicipalityName)
            .HasColumnName("sate_kommun_name");

        // text[] for the ≤5 SNI codes (Npgsql auto-maps List<string>). Value comparer per the
        // RecentJobSearch text[] precedent so EF snapshots the collection correctly. No GIN index in
        // v1 — no consumer until smart-bevakning (ADR 0091 / Fork 5).
        var sniComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>(), StringComparer.Ordinal),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
            v => v.ToList());
        builder.Property(c => c.SniCodes)
            .HasColumnName("sni_codes")
            .HasColumnType("text[]")
            .IsRequired()
            .Metadata.SetValueComparer(sniComparer);

        builder.Property(c => c.HasAdvertisingBlock)
            .HasColumnName("reklamsparr")
            .IsRequired();

        builder.Property(c => c.ScbStatusRaw)
            .HasColumnName("scb_status_raw")
            .HasMaxLength(2);

        // Coarse lifecycle status stored BY NAME (reorder-safe; parity TaxonomyConcept.Kind).
        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.SyncedAt)
            .HasColumnName("synced_at")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // status — dead-company filter (queries read Active only) + the vanish-sweep predicate.
        builder.HasIndex(c => c.Status)
            .HasDatabaseName("ix_company_register_status");

        // sate_kommun_code — the future smart-bevakning kommun facet (ADR 0091 next PR).
        builder.HasIndex(c => c.SeatMunicipalityCode)
            .HasDatabaseName("ix_company_register_sate_kommun_code");

        // synced_at — the vanish-sweep scans "rows not touched this run" by synced_at < runStartedAt.
        builder.HasIndex(c => c.SyncedAt)
            .HasDatabaseName("ix_company_register_synced_at");
    }
}
