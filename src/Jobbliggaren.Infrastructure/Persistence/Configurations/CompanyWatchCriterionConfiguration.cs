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
/// <b>The explicit <see cref="ValueComparer{T}"/> is defense-in-depth, NOT load-bearing —
/// mutation-verified 2026-07-13.</b> <c>UpdateCriteria</c> mutates the backing lists IN PLACE
/// (Clear/AddRange), which WOULD be lost if EF snapshotted the collection by reference. It does not:
/// Npgsql's array type mapping supplies its own deep comparer, and with both
/// <c>SetValueComparer</c> calls below commented out the persistence suite stays GREEN. The explicit
/// comparer is kept anyway — it is the house precedent (<c>RecentJobSearchConfiguration</c>,
/// <c>ScbCompanyRegisterEntryConfiguration</c>) and it pins the snapshot semantics we depend on
/// rather than inheriting them from a provider detail that could change. Do not describe it as the
/// thing preventing the silent-lost-update: the earlier version of this comment did, and it was
/// wrong (code-reviewer Minor 3).
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

        // user_id index — plain, covering every row. Three consumers: the "my criteria" scope
        // query, the MaxPerUser count, and the Art. 17 cascade sweep. Under hard delete (G1) there
        // is no lifecycle column to partition on and every row is live, so no partial-index
        // question arises at all — the earlier "NOT partial, deliberately" note existed only to
        // stop a "WHERE deleted_at IS NULL" filter from de-indexing the erasure sweep. That hazard
        // died with the column. The index is pinned as existing and non-partial by
        // CompanyWatchCriterionPersistenceTests.UserIdIndex_OnCompanyWatchCriteria_Exists_AndIsNotPartial,
        // which is also what proves the deleted_at drop did not take the index with it (DROP COLUMN
        // silently drops dependent indexes).
        //
        // No UNIQUE(user_id, sni_codes, kommun_codes): a btree index tuple is capped at ~2704
        // bytes, and a legitimate whole-industry selection (up to MaxSniCodes = 1000 codes ≈ 9 kB)
        // would make INSERT throw "index row size exceeds btree version 4 maximum". A duplicate
        // criterion is a cosmetic, user-deletable nuisance; a write-path landmine is not.
        builder.HasIndex(c => c.UserId)
            .HasDatabaseName("ix_company_watch_criteria_user_id");

        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // NO QUERY FILTER, deliberately — this aggregate has no lifecycle state to filter on. User
        // delete is HARD (C-D8/G1): the row is gone, not hidden. The vestigial
        // `deleted_at IS NULL` filter PR-3 carried was demolished together with the column and the
        // aggregate's SoftDelete; it never excluded a row, because nothing ever set the stamp.
        // Parity: RecentJobSearch / SavedJobAd, the other user-owned aggregates that delete for real.
        //
        // Adding a filter here later does NOT silently de-scope the Art. 17 cascade sweep — that is
        // the point, and it is a gate rather than a hope. AccountHardDeleteCascadeFitnessTests
        // .Cascade_reads_carry_IgnoreQueryFilters_iff_the_entity_declares_a_query_filter reads this
        // very model: declare a filter here and the criteria arm in AccountHardDeleter goes RED until
        // it carries IgnoreQueryFilters. A comment is not a gate, so this comment does not pretend to
        // be one — it names the one that is.
        builder.Ignore(c => c.DomainEvents);
    }
}
