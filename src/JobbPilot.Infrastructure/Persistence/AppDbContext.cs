using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<JobAd> JobAds => Set<JobAd>();
    public DbSet<JobSeeker> JobSeekers => Set<JobSeeker>();

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
