using System.Data.Common;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The catalogue's read on real Postgres (ADR 0176, M4): the real sampler is the reader, and the handler's content read
/// (<c>List&lt;JobAdId&gt;.Contains</c> on the key, composed with <c>JobAdPublication.IsListed</c>) translates and admits
/// exactly the listed ads. Its own database per test, because the exact set and its order are the assertion.
/// </summary>
/// <remarks>
/// Every row is produced the way production produces it: imported through <c>JobAd.Import</c>, created as
/// <c>POST /api/v1/job-ads</c> stores it, and archived and erased through the aggregate. Instants are whole seconds.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class PublicJobAdCatalogueReadTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = Now };
    private readonly CommandCapture _commands = new();
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(_commands)
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await postgres.DropDatabaseAsync(_connectionString);
    }

    [Fact]
    public async Task Page_ListsExactlyTheListedAds_NewestFirst_WithTiesInTheReadsIdOrder()
    {
        var newest = await AddAsync(Imported("Nyast", Now.AddDays(-1), null));
        var tieA = await AddAsync(Imported("Lika A", Now.AddDays(-2), null));
        var tieB = await AddAsync(Imported("Lika B", Now.AddDays(-2), Now.AddDays(5)));
        var oldest = await AddAsync(Imported("Äldst", Now.AddDays(-3), null));
        await AddAsync(NeverImported("Manuell"));
        await AddAsync(Imported("Arkiverad", Now.AddDays(-1), null), ad => ad.Archive(_clock).IsSuccess.ShouldBeTrue());
        await AddAsync(Imported("Raderad", Now.AddDays(-1), null), ad => ad.Erase(_clock).IsSuccess.ShouldBeTrue());
        await AddAsync(Imported("Utgången", Now.AddDays(-4), Now.AddSeconds(-1)));
        var sampler = await SampledAsync();

        var page = await PageAsync(sampler, 1);

        var ties = new[] { tieA, tieB }.OrderBy(id => id.ToString("D"), StringComparer.Ordinal);
        page.Items.Select(r => r.Id).ShouldBe([newest, .. ties, oldest]);
        page.TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task Page_AnAdErasedAfterTheSample_IsAbsentFromItsPageAtOnce_WhileTheCountStillHoldsIt()
    {
        var kept = await AddAsync(Imported("Kvar", Now.AddDays(-1), null));
        var erased = await AddAsync(Imported("Raderas", Now.AddDays(-2), null));
        var sampler = await SampledAsync();

        await EraseAsync(erased);
        var page = await PageAsync(sampler, 1);

        page.Items.Select(r => r.Id).ShouldBe([kept]);
        page.TotalCount.ShouldBe(2, "the count is the snapshot's, true as of its read");
        sampler.Current!.CatalogueOrder.ShouldContain(erased, "the snapshot still holds it until the next read");
    }

    [Fact]
    public async Task Page_AnAdWhoseDeadlinePassesAfterTheSample_IsAbsent()
    {
        var open = await AddAsync(Imported("Öppen", Now.AddDays(-1), null));
        await AddAsync(Imported("Går ut", Now.AddDays(-2), Now.AddMinutes(1)));
        var sampler = await SampledAsync();

        _clock.UtcNow = Now.AddMinutes(2);
        var page = await PageAsync(sampler, 1);

        page.Items.Select(r => r.Id).ShouldBe([open]);
    }

    [Fact]
    public async Task Page_TheContentRead_SendsThePageIdsAsOneArrayParameter_AndCarriesThePublicationPredicate()
    {
        await AddAsync(Imported("Ett", Now.AddDays(-1), null));
        await AddAsync(Imported("Två", Now.AddDays(-2), null));
        var sampler = await SampledAsync();
        _commands.Clear();

        await PageAsync(sampler, 1);

        var content = _commands.Texts.ShouldHaveSingleItem();
        content.ShouldContain("= ANY (");
        content.ShouldContain("external_source");
        content.ShouldContain("expires_at");
        content.ShouldNotContain("raw_payload", Case.Insensitive, "the content read projects a narrow row, never the entity");
        content.ShouldNotContain("description", Case.Insensitive);
    }

    private async Task<ListedJobAdSnapshotSampler> SampledAsync()
    {
        var sampler = new ListedJobAdSnapshotSampler(
            _provider.GetRequiredService<IServiceScopeFactory>(), _clock, NullLogger<ListedJobAdSnapshotSampler>.Instance);
        await sampler.SampleAsync(Ct);
        return sampler;
    }

    private async Task<Application.Common.PagedResult<PublicJobAdSummaryDto>> PageAsync(
        IListedJobAdSnapshotReader reader, int page)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var answer = await new GetPublicJobAdCatalogueQueryHandler(reader, db, Substitute.For<ITaxonomyReadModel>(), _clock)
            .Handle(new GetPublicJobAdCatalogueQuery(page), Ct);
        return answer.ShouldBeOfType<PublicJobAdCataloguePage.Available>().Rows;
    }

    private async Task<Guid> AddAsync(JobAd ad, Action<JobAd>? lifecycle = null)
    {
        lifecycle?.Invoke(ad);
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobAds.Add(ad);
        await db.SaveChangesAsync(Ct);
        return ad.Id.Value;
    }

    /// <summary>Erases an ad the way <c>EraseRecruiterAdsCommand</c> does: loaded, erased through the aggregate, saved.</summary>
    private async Task EraseAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.Where(j => j.Id == new JobAdId(id)).SingleAsync(Ct);
        ad.Erase(_clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private JobAd Imported(string title, DateTimeOffset publishedAt, DateTimeOffset? expiresAt)
    {
        var externalId = TestIds.ExternalId();
        return JobAd.Import(
            title: title,
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: publishedAt,
            expiresAt: expiresAt,
            clock: _clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
    }

    /// <summary>What <c>POST /api/v1/job-ads</c> stores: any source, and no import provenance.</summary>
    private JobAd NeverImported(string title) =>
        JobAd.Create(
            title, Company.Create("Exempelbolaget AB").Value, "Text som någon skrev själv.",
            "https://example.com/jobb/1", JobSource.Platsbanken, Now.AddDays(-1), null, _clock).Value;

    /// <summary>The text of every command that reads <c>job_ads</c>, in the order EF sends them.</summary>
    private sealed class CommandCapture : DbCommandInterceptor
    {
        private readonly List<string> _texts = [];

        public IReadOnlyList<string> Texts
        {
            get { lock (_texts) return [.. _texts]; }
        }

        public void Clear()
        {
            lock (_texts) _texts.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("job_ads", StringComparison.Ordinal))
                lock (_texts) _texts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
