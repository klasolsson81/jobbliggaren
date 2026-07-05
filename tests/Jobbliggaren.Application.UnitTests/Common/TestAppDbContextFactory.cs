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
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptors)
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
