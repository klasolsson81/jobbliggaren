using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Taxonomy;
using Microsoft.Extensions.DependencyInjection;

namespace JobbPilot.Api.IntegrationTests.Configuration;

/// <summary>
/// ADR 0043 defekt-triage #3 (CTO Variant A). Prod-lika startup-fixturer
/// triggar host-start (Services.CreateScope) FÖRE MigrateAsync (catch-22),
/// så <c>IHostedService</c>-seedrar som fail-loud:ar 42P01 i Production
/// (CLAUDE.md §3.4) måste plockas ur DI i fixturen. Prod-defensen
/// verifieras separat via *ProdBubbleTests + *Seeder.IsSchemaInitGracePeriod.
///
/// Delad extension (DRY/SPOT, Hunt/Thomas 1999): "plocka bort prod-seedrar
/// i prod-startup-fixtur" var ordagrant duplicerat i ≥4 fixturer. Nya
/// framtida seedrar läggs till HÄR — en SPOT — inte i varje fixtur.
/// Idempotent: säker att anropa även när en seeder inte är registrerad.
/// </summary>
internal static class StartupSeederTestExtensions
{
    public static void RemoveStartupSeeders(this IServiceCollection services)
    {
        var descriptors = services
            .Where(d => d.ImplementationType == typeof(IdempotentAdminRoleSeeder)
                     || d.ImplementationType == typeof(TaxonomySnapshotSeeder))
            .ToList();
        foreach (var d in descriptors)
            services.Remove(d);
    }
}
