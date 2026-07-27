using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Jobbliggaren.QA.Corpus.Harness;

/// <summary>
/// An EF InMemory <see cref="AppDbContext"/> for the layout corpus, so the REAL import and
/// auto-promote handlers can run with no database server, no container and no network.
///
/// <para><b>Why a provider at all, rather than a substituted DbSet.</b> Both handlers call
/// <c>FirstOrDefaultAsync</c> and <c>AnyAsync</c> on <c>IAppDbContext</c>'s <c>DbSet&lt;T&gt;</c>
/// members, which require a real <c>IAsyncQueryProvider</c>; a <c>DbSet</c> substituted over a
/// plain <c>IQueryable</c> throws. InMemory is forced by the port's own shape, not preferred.</para>
///
/// <para><b>What this does and does not exercise.</b> Real change tracking and the provider's
/// support for global query filters. Note that the corpus never actually reaches the
/// <c>parsed_resumes</c> soft-delete filter: it performs no read after promote, so the filter is
/// available rather than demonstrated. NOT exercised at all: the DEK envelope round-trip, SQL
/// translation, SmartEnum translation. Those stay proven by
/// <c>AutoPromoteParsedResumeEncryptionTests</c> and the integration suites, and the report says
/// so in its divergence disclosure.</para>
///
/// <para><b>Knowing duplication.</b> This is a near-copy of
/// <c>tests/Jobbliggaren.Application.UnitTests/Common/TestAppDbContextFactory.cs</c>, which is
/// <c>internal</c> to that assembly. Lifting it to <c>tests/Shared/</c> would edit
/// <c>Jobbliggaren.Application.UnitTests.csproj</c> — a file other lanes touch — and is a second
/// change-reason for this PR. The duplication's failure mode is loud rather than silent (model
/// validation throws for the WHOLE model, not for one entity), which is what makes it acceptable;
/// it is named in the PR body as a follow-up sweep.</para>
/// </summary>
internal static class CorpusAppDbContextFactory
{
    internal static AppDbContext Create() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // JobAdConfiguration maps the JobAd.SearchVector shadow property as an
            // NpgsqlTsVector STORED generated column (ADR 0062). The InMemory provider cannot
            // model that type and throws during validation for the ENTIRE model, not just
            // JobAd — so a corpus that never touches JobAd still cannot build a context
            // without stripping it. Postgres FTS is tested against real Postgres
            // (Api.IntegrationTests/JobAds/ListJobAdsFtsTests); it does not belong in this model.
            .ReplaceService<IModelCustomizer, IgnoreSearchVectorModelCustomizer>()
            .Options);

    private sealed class IgnoreSearchVectorModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            if (modelBuilder.Model.FindEntityType(typeof(JobAd)) is IMutableEntityType jobAd
                && jobAd.FindProperty("SearchVector") is not null)
            {
                jobAd.RemoveProperty("SearchVector");
            }
        }
    }
}
