using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1681 (ADR 0139) — EF Core configuration for <c>company_watch_criterion_members</c> (parity
/// <see cref="ScbCompanyRegisterEntryConfiguration"/>). The table is NOT a <c>DbSet</c> on
/// <c>IAppDbContext</c> — it is reached only via raw SQL inside Infrastructure. This configuration
/// exists for the migration schema, and it must live in this namespace because that is what
/// <c>AppDbContext.OnModelCreating</c> scans.
/// </summary>
internal sealed class CompanyWatchCriterionMemberConfiguration
    : IEntityTypeConfiguration<CompanyWatchCriterionMember>
{
    public void Configure(EntityTypeBuilder<CompanyWatchCriterionMember> builder)
    {
        builder.ToTable("company_watch_criterion_members");

        // Composite natural key. There is no surrogate id because there is no identity to have: the
        // row IS the pair. It also gives the read path its index for free — the whole member set of
        // one criterion is a leading-column range scan, which is what the measured 1,49 ms p95 at
        // 1 000 members is served by (Index Only Scan on this PK, Heap Fetches: 0), and it makes a
        // duplicate member unrepresentable rather than merely unlikely.
        builder.HasKey(m => new { m.CriterionId, m.OrganizationNumber });

        builder.Property(m => m.CriterionId)
            .HasColumnName("criterion_id")
            .HasConversion(id => id.Value, value => new CompanyWatchCriterionId(value))
            .IsRequired();

        builder.Property(m => m.OrganizationNumber)
            .HasColumnName("organization_number")
            .HasMaxLength(10)
            .IsRequired();

        // security-auditor Major 5(a), 2026-09-06 — the Art. 17 / Art. 5(1)(e) erasure cascade, at
        // the DATABASE, not in a handler.
        //
        // Criterion deletion is HARD (C-D8 / Fork G1): DeleteCompanyWatchCriterionCommandHandler
        // calls Remove, there is no soft-delete state and no sweeper. Member rows are derived
        // personal data ABOUT THE USER and must not outlive the criterion they were derived from. A
        // handler line could do that — and could also be forgotten, by the next person adding a
        // second delete path. A DB-level FK cannot: "not handler discipline, the only form that
        // cannot be forgotten."
        //
        // The reference is declared from THIS side with no navigation and no inverse collection, so
        // CompanyWatchCriterion stays free of any knowledge that a materialised extension exists —
        // the aggregate does not own these rows, the register-derived read model does. Declaring the
        // FK by column against the principal's key is what expresses that asymmetry in EF.
        //
        // NOTE ON THE COST: this cascade is NOT covered by AccountHardDeleteCascadeFitnessTests. That
        // partition is over AggregateRoots, and under ADR 0139's Infrastructure-scoped form this is a
        // read model, not an aggregate — security-auditor named that price explicitly and accepted it
        // ("a smaller and more honest problem than a vacuous firewall"). The compensating measurement
        // is the behavioural oracle in HardDeleteAccountsJobIntegrationTests, which is therefore
        // MANDATORY here rather than recommended (Major 5(b)), plus the direct criterion-delete oracle
        // CompanyWatchCriterionMaterialisationTests.DeletingTheCriterion_CascadesBothTables_AtTheDatabase.
        // Neither is optional; between them they are what makes this FK's effect measured rather than
        // declared, and both were mutation-verified per table (flipping either FK alone reds its own).
        //
        // THE CONSTRAINT NAME drops the principal-table segment every other explicitly named FK here
        // carries (fk_resume_versions_resumes_resume_id). That is forced, not sloppy: the conforming
        // names would be 70 and 79 characters against PostgreSQL's 63-byte identifier limit, so both
        // would be SILENTLY TRUNCATED — and a truncated constraint name is worse than a short one,
        // because it looks deliberate and cannot be grepped. Recompute with
        // `SELECT length('fk_company_watch_criterion_members_company_watch_criteria_criterion_id');`
        builder.HasOne<CompanyWatchCriterion>()
            .WithMany()
            .HasForeignKey(m => m.CriterionId)
            .HasConstraintName("fk_company_watch_criterion_members_criterion_id")
            .OnDelete(DeleteBehavior.Cascade);

        // NO index on organization_number alone, and that is a stated choice rather than an omission.
        // The read path drives from the MEMBER side — "this criterion's org.nr, then those ads" — so
        // it uses the PK's leading column and never looks a member up by org.nr. Measured 2026-09-06:
        // the join form that WOULD want such an index (drive from job_ads, probe members) is the
        // slower shape below ~10 000 members and is not the shape the bound was derived against. An
        // index nothing reads is write amplification on every materialisation run plus a second thing
        // to keep correct. Add it when a reader that needs it exists, and measure it then.
    }
}
