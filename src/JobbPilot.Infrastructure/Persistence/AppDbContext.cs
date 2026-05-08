using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Auditing;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.Resumes;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<JobAd> JobAds => Set<JobAd>();
    public DbSet<JobSeeker> JobSeekers => Set<JobSeeker>();
    public DbSet<DomainApplication> Applications => Set<DomainApplication>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly,
            t => t.Namespace?.StartsWith(
                "JobbPilot.Infrastructure.Persistence.Configurations",
                StringComparison.Ordinal) == true);
        base.OnModelCreating(modelBuilder);
    }
}
