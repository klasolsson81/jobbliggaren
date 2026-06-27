using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
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
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<RecentJobSearch> RecentJobSearches => Set<RecentJobSearch>();
    public DbSet<SavedJobAd> SavedJobAds => Set<SavedJobAd>();
    // ADR 0080 Vag 4 — background match results.
    public DbSet<UserJobAdMatch> UserJobAdMatches => Set<UserJobAdMatch>();

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
