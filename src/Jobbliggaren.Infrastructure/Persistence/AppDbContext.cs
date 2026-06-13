using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Domain.Waitlist;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<JobAd> JobAds => Set<JobAd>();
    public DbSet<JobSeeker> JobSeekers => Set<JobSeeker>();
    public DbSet<DomainApplication> Applications => Set<DomainApplication>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<RecentJobSearch> RecentJobSearches => Set<RecentJobSearch>();
    public DbSet<SavedJobAd> SavedJobAds => Set<SavedJobAd>();

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
