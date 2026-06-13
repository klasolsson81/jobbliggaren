using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Jobbliggaren.Infrastructure.Identity;

/// <summary>
/// Används av `dotnet ef migrations add --context AppIdentityDbContext`. Delegerar till
/// <see cref="MigrationsOptionsFactory.BuildIdentityOptions"/> — single source of truth
/// för EF-konfig per ADR 0034.
/// </summary>
public sealed class DesignTimeIdentityDbContextFactory
    : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        // Lokal docker-default. `Password=local` undviker gitleaks-false-positive
        // (pattern `Password=jobbliggaren` matchade tidigare som secret).
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=jobbliggaren;Username=jobbliggaren;Password=local";
        return new AppIdentityDbContext(MigrationsOptionsFactory.BuildIdentityOptions(cs));
    }
}
