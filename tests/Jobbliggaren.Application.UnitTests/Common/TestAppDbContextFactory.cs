using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Application.UnitTests.Common;

internal static class TestAppDbContextFactory
{
    internal static AppDbContext Create(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        if (interceptors.Length > 0)
        {
            // IMaterializationInterceptor is a SINGLETON interceptor: EF captures the
            // first instance in the CACHED internal service provider and reuses it for
            // every later context with equivalent options — another test class's
            // hydrator then silently "wins" over this one (suite-order contamination:
            // class green in isolation, red in the full suite). Caching is disabled
            // per-context when instance interceptors are supplied, so every test gets
            // ITS OWN interceptor; the interceptor-free default path keeps the cache.
            builder = builder
                .AddInterceptors(interceptors)
                .EnableServiceProviderCaching(false);
        }

        var options = builder
            // ADR 0062 — JobAdConfiguration mappar shadow-propertyn
            // JobAd.SearchVector (NpgsqlTsVector, STORED tsvector generated
            // column). Den EF Core InMemory-providern saknar stöd för
            // NpgsqlTsVector → modell-validering kastar för HELA modellen, inte
            // bara JobAd. SearchVector är en Postgres-FTS-detalj som testas mot
            // riktig Postgres (Api.IntegrationTests/JobAds/ListJobAdsFtsTests) —
            // den hör inte hemma i InMemory-unit-modellen. Strippa den via en
            // model-customizer så InMemory-modellen validerar.
            .ReplaceService<IModelCustomizer, IgnoreSearchVectorModelCustomizer>()
            .Options;
        return new AppDbContext(options);
    }

    // Kör efter AppDbContext.OnModelCreating + JobAdConfiguration: tar bort
    // SearchVector-shadow-propertyn så InMemory-providern slipper se
    // NpgsqlTsVector. Påverkar ENBART unit-test-InMemory-modellen — produktions-
    // DbContext (Npgsql) och integration-tester rör detta inte.
    private sealed class IgnoreSearchVectorModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            var jobAd = modelBuilder.Model.FindEntityType(typeof(JobAd));
            var searchVector = jobAd?.FindProperty("SearchVector");
            if (searchVector is not null)
                ((IMutableEntityType)jobAd!).RemoveProperty("SearchVector");
        }
    }
}
