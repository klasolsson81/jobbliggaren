using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobbPilot.Infrastructure.Identity;

/// <summary>
/// Används av `dotnet ef migrations add --context AppIdentityDbContext`.
/// </summary>
public sealed class DesignTimeIdentityDbContextFactory
    : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql(
                Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                    ?? "Host=localhost;Port=5432;Database=jobbpilot;Username=jobbpilot;Password=jobbpilot",
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppIdentityDbContext(options);
    }
}
