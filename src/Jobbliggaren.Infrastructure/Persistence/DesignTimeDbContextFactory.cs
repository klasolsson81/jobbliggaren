using Microsoft.EntityFrameworkCore.Design;

namespace Jobbliggaren.Infrastructure.Persistence;

/// <summary>
/// Används av `dotnet ef migrations add` utan faktisk app-host. Delegerar till
/// <see cref="MigrationsOptionsFactory.BuildAppOptions"/> — single source of truth för
/// EF-konfig per ADR 0034.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Lokal docker-default. `Password=local` undviker gitleaks-false-positive
        // (pattern `Password=jobbliggaren` matchade tidigare som secret).
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=jobbliggaren;Username=jobbliggaren;Password=local";
        return new AppDbContext(MigrationsOptionsFactory.BuildAppOptions(cs));
    }
}
