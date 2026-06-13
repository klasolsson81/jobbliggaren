using Jobbliggaren.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Persistence;

/// <summary>
/// Single source of truth för DbContextOptions vid migration-tid.
/// Konsumeras av:
/// <list type="bullet">
///   <item><see cref="DesignTimeDbContextFactory"/> (dotnet ef-CLI mot AppDbContext)</item>
///   <item><see cref="DesignTimeIdentityDbContextFactory"/> (dotnet ef-CLI mot AppIdentityDbContext)</item>
///   <item><c>Jobbliggaren.Migrate</c> Phase E (<c>schema</c>-mode) och Bootstrap-mode</item>
/// </list>
/// DRY-disciplin per Hunt/Thomas 1999 — konfig-konventioner för migration-history-tabeller
/// och snake-case-konvention får inte divergera mellan tooling och deploy-tid (ADR 0034).
/// </summary>
public static class MigrationsOptionsFactory
{
    /// <summary>
    /// Options för <see cref="AppDbContext"/>. Migrations-history hamnar i <c>public</c>-schema
    /// med default-tabellnamn; snake-case-konvention applicerar på domain-tabeller och
    /// history-table-kolumner (<c>migration_id</c>, <c>product_version</c>).
    /// </summary>
    public static DbContextOptions<AppDbContext> BuildAppOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                // F6 P4 (2026-05-20) — migration-tids-CommandTimeout 600s.
                // Npgsql-default är 30s; tunga DDL-migrations (GIN-trigram-index
                // på job_ads.description, ~52k rader stor fri-text) överskrider
                // det. Gäller ENBART migration-tid (BuildAppOptions konsumeras av
                // Migrate schema-mode + dotnet ef-CLI) — runtime-appens DbContext
                // byggs via separat options-väg och påverkas inte.
                npgsql.CommandTimeout(600);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

    /// <summary>
    /// Options för <see cref="AppIdentityDbContext"/>. Explicit
    /// <c>MigrationsHistoryTable("__EFMigrationsHistory", "identity")</c> behövs eftersom
    /// Identity-context använder <c>HasDefaultSchema("identity")</c> + snake-case-konvention
    /// — utan explicit override skulle EF default-skapa tabellen utanför identity-schemat.
    /// </summary>
    public static DbContextOptions<AppIdentityDbContext> BuildIdentityOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
            })
            .UseSnakeCaseNamingConvention()
            .Options;
}
