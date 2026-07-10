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

    public void Detach(object entity) => Entry(entity).State = EntityState.Detached;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly,
            t => t.Namespace?.StartsWith(
                "Jobbliggaren.Infrastructure.Persistence.Configurations",
                StringComparison.Ordinal) == true);
        base.OnModelCreating(modelBuilder);
    }
}
