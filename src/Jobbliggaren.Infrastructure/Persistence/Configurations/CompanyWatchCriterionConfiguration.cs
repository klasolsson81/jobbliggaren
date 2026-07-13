using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #560 Fork A1 (senior-cto-advisor 2026-07-12; architect bind 2026-07-13) — EF Core configuration
/// for <see cref="CompanyWatchCriterion"/>.
///
/// <para>
/// <b>Two real <c>text[]</c> columns, NOT a jsonb value object</b> (Fork A1). The
/// <see cref="CompanyWatchCriteriaSpec"/> VO is COMPUTED from two private <c>List&lt;string&gt;</c>
/// backing fields, mapped via shadow-property + field access — the <c>RecentJobSearch</c> precedent.
/// <c>OwnsOne</c> was rejected by the architect: it turns the VO into a KEYED owned entity tracked
/// by reference (a shared instance re-keys and throws), and <c>ComplexProperty</c> — EF's proper VO
/// answer — fails on the InMemory provider when members carry value converters, which is exactly
/// the sanctioned handler-test harness (CLAUDE.md §2.4). Npgsql auto-maps <c>List&lt;string&gt;</c>
/// to <c>text[]</c> and InMemory ignores <c>HasColumnType</c>, so this shape is green on BOTH paths.
/// </para>
///
/// <para>
/// <b>The <see cref="ValueComparer{T}"/> is load-bearing.</b> <c>UpdateCriteria</c> mutates the
/// backing lists IN PLACE (Clear/AddRange). Without a deep, sequence-based comparer EF snapshots the
/// collection BY REFERENCE — old and new snapshot are the same object, EF sees no change, and the
/// update silently never persists.
/// </para>
/// </summary>
public sealed class CompanyWatchCriterionConfiguration : IEntityTypeConfiguration<CompanyWatchCriterion>
{
    public void Configure(EntityTypeBuilder<CompanyWatchCriterion> builder)
    {
        builder.ToTable("company_watch_criteria");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CompanyWatchCriterionId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.UserId)
            .IsRequired();

        builder.Property(c => c.Label)
            .HasMaxLength(CompanyWatchCriterion.LabelMaxLength);

        // text[] columns via shadow backing-fields (RecentJobSearchConfiguration precedent).
        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>(), StringComparer.Ordinal),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
            v => v.ToList());

        var sniCodes = builder.Property<List<string>>("_sniCodes")
            .HasField("_sniCodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("sni_codes")
            .HasColumnType("text[]")
            .IsRequired();
        sniCodes.Metadata.SetValueComparer(stringListComparer);

        // SCB 4-digit SEAT municipality code (matches company_register.sate_kommun_code) — a
        // different namespace from the JobTech municipality_concept_id an ad carries (RF-4,
        // ADR 0105). Kept apart deliberately, in code and in copy.
        var municipalityCodes = builder.Property<List<string>>("_municipalityCodes")
            .HasField("_municipalityCodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("kommun_codes")
            .HasColumnType("text[]")
            .IsRequired();
        municipalityCodes.Metadata.SetValueComparer(stringListComparer);

        // LOAD-BEARING: the VO getter is a computed wrapper over the two backing fields. Without
        // this Ignore, EF tries to model CompanyWatchCriteriaSpec itself (an owned/complex type
        // with collections) → a model error that takes down the WHOLE model, not just this
        // aggregate.
        builder.Ignore(c => c.Criteria);

        // user_id index — NOT partial (deliberately, parity ix_company_watches_user_id). A
        // "WHERE deleted_at IS NULL" filter would exclude the Art. 17 cascade sweep — which runs
        // IgnoreQueryFilters(), i.e. WITHOUT the deleted_at predicate — from using the index,
        // turning the erasure path into a seq scan. Three consumers: the "my criteria" scope query,
        // the MaxPerUser count, and the cascade sweep.
        //
        // No UNIQUE(user_id, sni_codes, kommun_codes): a btree index tuple is capped at ~2704
        // bytes, and a legitimate whole-industry selection (up to MaxSniCodes = 1000 codes ≈ 9 kB)
        // would make INSERT throw "index row size exceeds btree version 4 maximum". A duplicate
        // criterion is a cosmetic, user-deletable nuisance; a write-path landmine is not.
        builder.HasIndex(c => c.UserId)
            .HasDatabaseName("ix_company_watch_criteria_user_id");

        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();
        builder.Property(c => c.DeletedAt);

        // Soft-delete: deleted criteria hidden from normal queries; the Art. 17 cascade
        // IgnoreQueryFilters to erase them on account hard-delete.
        builder.HasQueryFilter(c => c.DeletedAt == null);

        builder.Ignore(c => c.DomainEvents);
    }
}
