using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1681 (ADR 0139) — EF Core configuration for
/// <c>company_watch_criterion_materialisations</c>, the per-criterion state row that makes a missing
/// or refused materialisation degrade HONESTLY instead of into a silent zero (security-auditor
/// Major 3). Infrastructure-internal, migration-schema only — like its sibling, it is not a
/// <c>DbSet</c> on <c>IAppDbContext</c>.
/// </summary>
internal sealed class CompanyWatchCriterionMaterialisationConfiguration
    : IEntityTypeConfiguration<CompanyWatchCriterionMaterialisation>
{
    public void Configure(EntityTypeBuilder<CompanyWatchCriterionMaterialisation> builder)
    {
        builder.ToTable("company_watch_criterion_materialisations");

        // The criterion id is BOTH the primary key and the foreign key — one state row per criterion,
        // made unrepresentable-otherwise rather than merely enforced. A surrogate key plus a unique
        // index would express the same rule in two places, and "which run wrote it" is deliberately
        // not modelled: this table holds the criterion's CURRENT conclusion, not a run history. A
        // history would be a second knowledge piece with its own retention question (Art. 5(1)(e)) for
        // no consumer.
        builder.HasKey(m => m.CriterionId);
        builder.Property(m => m.CriterionId)
            .HasColumnName("criterion_id")
            .HasConversion(id => id.Value, value => new CompanyWatchCriterionId(value))
            .ValueGeneratedNever();

        // Stored BY NAME (reorder-safe; parity company_register.status and TaxonomyConcept.Kind) —
        // never by ordinal, where inserting a member silently re-labels every stored row.
        builder.Property(m => m.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.MemberCount)
            .HasColumnName("member_count")
            .IsRequired();

        builder.Property(m => m.ExcludedPersonnummerShaped)
            .HasColumnName("excluded_personnummer_shaped")
            .IsRequired();

        builder.Property(m => m.MaterialisedAt)
            .HasColumnName("materialised_at")
            .IsRequired();

        // #1681 part 2 — the staleness guard's discriminator. Fixed-width lower-case hex SHA-256, so
        // the length comes from the type rather than a literal repeated here and in the migration
        // (CriteriaFingerprint.Length is the SSOT). NOT nullable: every row is written by a run that
        // knew its predicate, so a NULL would be a state no writer can produce — and a nullable column
        // would silently invite one, whereupon the read's mismatch check would have to grow a
        // third branch for "we don't know what this was computed from", which is exactly the
        // ambiguity the state enum exists to prevent.
        builder.Property(m => m.CriteriaFingerprint)
            .HasColumnName("criteria_fingerprint")
            .HasMaxLength(Application.CompanyWatches.Abstractions.CriteriaFingerprint.Length)
            .IsRequired();

        // Major 5(a) again, for the same reason and with the same force: the state row is derived
        // personal data about the user (it says something about a predicate she saved), so it must not
        // outlive its criterion either. Cascading BOTH tables from the same principal means criterion
        // deletion cannot leave half a materialisation behind. Mutation-verified independently of the
        // sibling: flipping THIS FK alone to Restrict reds
        // CompanyWatchCriterionMaterialisationTests.DeletingTheCriterion_CascadesBothTables_AtTheDatabase.
        //
        // The constraint name drops the principal-table segment for the reason its sibling's docblock
        // measures: the conforming name is 79 characters against PostgreSQL's 63-byte limit.
        builder.HasOne<CompanyWatchCriterion>()
            .WithMany()
            .HasForeignKey(m => m.CriterionId)
            .HasConstraintName("fk_company_watch_criterion_materialisations_criterion_id")
            .OnDelete(DeleteBehavior.Cascade);

        // No index beyond the PK. Every read is by criterion id (or by the set of a user's criterion
        // ids), which the PK serves; nothing queries by state or by staleness today. A "find the stale
        // ones" index is exactly the sort of speculative structure that later has to be explained.
    }
}
