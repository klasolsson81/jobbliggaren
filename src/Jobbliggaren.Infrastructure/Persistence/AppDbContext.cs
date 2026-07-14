using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedSearches;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<JobAd> JobAds => Set<JobAd>();
    public DbSet<JobSeeker> JobSeekers => Set<JobSeeker>();
    public DbSet<DomainApplication> Applications => Set<DomainApplication>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<ParsedResume> ParsedResumes => Set<ParsedResume>();
    // Fas 4b PR-9a (ADR 0093 §D5) — original-file binary store (Form C).
    public DbSet<ResumeFile> ResumeFiles => Set<ResumeFile>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<RecentJobSearch> RecentJobSearches => Set<RecentJobSearch>();
    public DbSet<SavedJobAd> SavedJobAds => Set<SavedJobAd>();
    // ADR 0080 Vag 4 — background match results.
    public DbSet<UserJobAdMatch> UserJobAdMatches => Set<UserJobAdMatch>();
    // ADR 0087 D3 (#311 PR-3) — user follows of an employer by org.nr.
    public DbSet<CompanyWatch> CompanyWatches => Set<CompanyWatch>();
    // ADR 0087 D5 (#311 PR-4) — company-follow notification hits.
    public DbSet<FollowedCompanyAdHit> FollowedCompanyAdHits => Set<FollowedCompanyAdHit>();
    // #560 Fork A1 — criteria-based company watches (SNI ∧ kommun discovery predicate).
    public DbSet<CompanyWatchCriterion> CompanyWatchCriteria => Set<CompanyWatchCriterion>();

    public void Detach(object entity) => Entry(entity).State = EntityState.Detached;

    /// <summary>
    /// #884 — the collation Swedish natural-language text columns are declared with. Å, Ä and Ö are
    /// three distinct letters that sort AFTER Z; the cluster's default (<c>en_US.utf8</c>) folds them
    /// into A and O, which put "Åkesson AB" between "Ahlberg" and "Bok".
    ///
    /// <para>
    /// ICU, not libc, and that is not a preference: the <c>postgres:18.3</c> image ships exactly five
    /// libc collations (C, C.utf8, POSIX, en_US, en_US.utf8) and <c>locale -a</c> confirms no Swedish
    /// locale is generated. A libc route would need <c>locale-gen sv_SE.UTF-8</c> baked into every
    /// image that ever runs a migration — dev compose, Testcontainers, CI, prod — making the schema's
    /// correctness depend on the OS rather than on the database. ICU ships 871 collations here,
    /// including a deterministic <c>sv-SE</c>. Deterministic (the default) is required, not incidental:
    /// under a non-deterministic collation equality stops being byte equality, which would change
    /// <c>=</c> semantics on the column.
    /// </para>
    ///
    /// <para>
    /// EF-modelled rather than raw <c>CREATE COLLATION</c> so it lives in the model snapshot: an
    /// unmodelled collation object is invisible to the differ, and the next <c>migrations add</c>
    /// would happily emit a <c>DROP COLLATION</c> for it.
    /// </para>
    ///
    /// <para>
    /// Which columns carry it is a <b>rule, not a list</b> (ADR 0107): Swedish natural-language text
    /// (<c>company_name</c>, <c>sate_kommun_name</c>) gets it, because there the collation is
    /// CORRECTNESS. Machine identifiers (<c>organization_number</c>, <c>sni_codes</c>,
    /// <c>sate_kommun_code</c>) do not: under any deterministic collation, equality on an identifier
    /// is byte equality, so there is no defect to fix.
    /// </para>
    /// </summary>
    internal const string SwedishCollation = "swedish";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCollation(
            SwedishCollation, locale: "sv-SE", provider: "icu", deterministic: true);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly,
            t => t.Namespace?.StartsWith(
                "Jobbliggaren.Infrastructure.Persistence.Configurations",
                StringComparison.Ordinal) == true);
        base.OnModelCreating(modelBuilder);
    }
}
