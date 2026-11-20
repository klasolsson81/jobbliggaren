using System.Data.Common;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;
using Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.JobAds.JuniorIt;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The open junior IT list on real Postgres (ADR 0186, security-auditor S8): the real <c>JuniorItJobAdQuery</c> behind
/// the real handler, the field's groups from the real taxonomy snapshot, the shipped headline pattern, and synthetic
/// curated ids. Its own database per test, because the exact set, the total and the kommun options are the assertion.
/// </summary>
/// <remarks>
/// Every row is produced the way production produces it: imported through <c>JobAd.Import</c>, created as
/// <c>POST /api/v1/job-ads</c> stores it, archived through <c>ArchiveExternalJobAdCommand</c> and
/// <c>ExpireJobAdsJob</c>, erased through the aggregate, and past its deadline by the clock. Instants are whole seconds;
/// ties are ordered by the uuid, whose order on Postgres is the ordinal order of its lower-case <c>"D"</c> string.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class JuniorItJobAdQueryReadTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private const string DataItField = "apaJ_2ja_LuF";
    private const string DataIt = "DJh5_yyF_hEM";
    private const string OtherDataIt = "hmaC_cfi_UKg";
    private const string NotDataIt = "2kLc_pto_DpV";
    private const string Stockholm = "AvNB_uwa_6n6";
    private const string Solna = "zHxw_uJZ_NJ8";
    private const string Goteborg = "PVZL_BQT_XtL";
    private const string StockholmsLan = "CifL_Rzy_Mku";
    private const string VastraGotalandsLan = "zdoY_6u5_Krt";

    // Synthetic Platsbanken-shaped ids, outside the shipped list's range.
    private const string CuratedInField = "90000001";
    private const string CuratedOutsideField = "90000002";

    private static readonly JuniorItList List =
        JuniorItListLoader.Load() with { CuratedExternalIds = [CuratedInField, CuratedOutsideField] };

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = Now };
    private readonly CommandCapture _commands = new();
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;
    private TaxonomyReadModel _taxonomy = default!;
    private int _externalIds;

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

        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(scopeFactory, environment, NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(Ct);
        _taxonomy = new TaxonomyReadModel(scopeFactory);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await postgres.DropDatabaseAsync(_connectionString);
    }

    [Fact]
    public async Task TheSnapshot_PutsThisClassesGroupsWhereItsAssertionsNeedThem()
    {
        var tree = await _taxonomy.GetTreeAsync(Ct);

        var dataIt = tree.OccupationFields.Single(f => f.ConceptId == List.OccupationFieldConceptId);
        dataIt.ConceptId.ShouldBe(DataItField);
        dataIt.OccupationGroups.Select(g => g.ConceptId).ShouldContain(DataIt);
        dataIt.OccupationGroups.Select(g => g.ConceptId).ShouldContain(OtherDataIt);
        dataIt.OccupationGroups.Select(g => g.ConceptId).ShouldNotContain(NotDataIt);
        tree.OccupationFields.SelectMany(f => f.OccupationGroups).Select(g => g.ConceptId).ShouldContain(NotDataIt);
        JuniorItListLoader.Load().CuratedExternalIds.ShouldNotContain(CuratedInField);
        JuniorItListLoader.Load().CuratedExternalIds.ShouldNotContain(CuratedOutsideField);
    }

    [Fact]
    public async Task Read_ListsExactlyTheDataItAdsThatTheCuratedListOrTheTitleAdmits()
    {
        var curated = await AddAsync(Imported("Systemutvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-2),
            externalId: CuratedInField));
        var titled = await AddAsync(Imported("Junior utvecklare", OtherDataIt, Goteborg, VastraGotalandsLan, Now.AddDays(-1)));
        await AddAsync(Imported("Junior ekonomiassistent", NotDataIt, Stockholm, StockholmsLan, Now.AddHours(-1)));
        await AddAsync(Imported("Systemutvecklare", NotDataIt, Solna, StockholmsLan, Now.AddHours(-2),
            externalId: CuratedOutsideField));
        await AddAsync(Imported("Senior utvecklare", DataIt, Solna, StockholmsLan, Now.AddHours(-3)));
        // An ad whose source carries no occupation group (PlatsbankenJobSource.MapFacets,
        // PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_YieldsEmptyFacets_WhenTheSourceCarriesNone).
        await AddAsync(Imported("Junior utvecklare", null, Solna, StockholmsLan, Now.AddHours(-4)));
        await AddAsync(NeverImported("Junior utvecklare"));

        var page = await PageAsync();

        page.Rows.Items.Select(r => r.Id).ShouldBe([titled, curated]);
        page.Rows.TotalCount.ShouldBe(2);
        page.Places.ShouldBe([
            new PublicJuniorItPlaceDto(Goteborg, "Göteborg", 1),
            new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 1),
        ]);
    }

    [Fact]
    public async Task Read_ShapesTheRow_WithItsKommunAndLanNamed_AndItsLastDay()
    {
        var ad = Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1), Now.AddDays(10));
        await AddAsync(ad);

        var page = await PageAsync();

        page.Rows.Items.ShouldBe([new PublicJuniorItJobAdDto(
            ad.Id.Value, "Junior utvecklare", "Exempelbolaget AB", "Stockholm", "Stockholms län", Now.AddDays(-1), Now.AddDays(10))]);
    }

    public static TheoryData<string> Removals =>
    [
        "archived by the stream's removal event",
        "past its deadline",
        "archived by ExpireJobAdsJob",
        "erased",
    ];

    [Theory]
    [MemberData(nameof(Removals))]
    public async Task Read_AnAdThatStopsBeingListed_LeavesTheRowsTheTotalAndTheKommunOptionsAtOnce(string removal)
    {
        var kept = await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-2)));
        var deadline = removal is "past its deadline" or "archived by ExpireJobAdsJob" ? Now.AddMinutes(1) : (DateTimeOffset?)null;
        var leaving = Imported("Trainee inom IT", DataIt, Goteborg, VastraGotalandsLan, Now.AddDays(-1), deadline);
        await AddAsync(leaving);

        var before = await PageAsync();
        before.Rows.Items.Select(r => r.Id).ShouldBe([leaving.Id.Value, kept]);
        before.Rows.TotalCount.ShouldBe(2);
        before.Places.Select(p => p.MunicipalityConceptId).ShouldBe([Goteborg, Stockholm]);

        switch (removal)
        {
            case "archived by the stream's removal event":
                await ArchiveThroughTheStreamAsync(leaving.External!.ExternalId);
                break;
            case "past its deadline":
                _clock.UtcNow = Now.AddMinutes(2);
                break;
            case "archived by ExpireJobAdsJob":
                _clock.UtcNow = Now.AddMinutes(2);
                await RunExpireJobAdsJobAsync();
                (await StatusOfAsync(leaving.Id.Value)).ShouldBe(JobAdStatus.Archived);
                break;
            default:
                await EraseAsync(leaving.Id.Value);
                break;
        }

        var after = await PageAsync();
        after.Rows.Items.Select(r => r.Id).ShouldBe([kept]);
        after.Rows.TotalCount.ShouldBe(1);
        after.Places.ShouldBe([new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 1)]);
    }

    [Fact]
    public async Task Places_AKommunWithNoListedJuniorItAd_IsNoOption_AndChoosingItIsAnEmptyList()
    {
        var listed = await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        await AddAsync(Imported("Senior utvecklare", DataIt, Solna, StockholmsLan, Now.AddDays(-1)));
        await AddAsync(Imported("Junior ekonomiassistent", NotDataIt, Solna, StockholmsLan, Now.AddDays(-1)));
        await AddAsync(Imported("Junior utvecklare", DataIt, Solna, StockholmsLan, Now.AddDays(-1)),
            ad => ad.Archive(_clock).IsSuccess.ShouldBeTrue());
        await AddAsync(Imported("Junior utvecklare", DataIt, Solna, StockholmsLan, Now.AddDays(-1), Now.AddSeconds(-1)));

        var all = await PageAsync();
        var solna = await PageAsync(Solna);
        var solnaTwo = await HandleAsync(Solna, page: 2);

        all.Rows.Items.Select(r => r.Id).ShouldBe([listed]);
        all.Places.ShouldBe([new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 1)]);
        solna.Rows.Items.ShouldBeEmpty();
        solna.Rows.TotalCount.ShouldBe(0);
        solna.Places.ShouldBe(all.Places);
        solnaTwo.ShouldBeOfType<PublicJuniorItJobAdsPage.NoSuchPage>();
    }

    [Fact]
    public async Task Read_AnAdWithoutAKommun_CountsInTheTotal_ButIsNoKommunOption()
    {
        // An absent address block gives both ids null (PlatsbankenJobSource.MapFacets,
        // PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_YieldsEmptyFacets_WhenTheSourceCarriesNone).
        var placed = await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        var unplaced = await AddAsync(Imported("Praktikant inom IT", DataIt, null, null, Now.AddDays(-2)));

        var page = await PageAsync();

        page.Rows.Items.Select(r => r.Id).ShouldBe([placed, unplaced]);
        page.Rows.TotalCount.ShouldBe(2);
        page.Places.ShouldBe([new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 1)]);
    }

    [Fact]
    public async Task Page_NewestFirst_WithTiesInTheReadsIdOrder()
    {
        var newest = await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        // Inserted against the oracle, the higher "D" string first, so only the id order can put the tie right.
        var tied = new[]
            {
                Imported("Trainee A", DataIt, Stockholm, StockholmsLan, Now.AddDays(-2)),
                Imported("Trainee B", OtherDataIt, Goteborg, VastraGotalandsLan, Now.AddDays(-2), Now.AddDays(5)),
            }
            .OrderByDescending(ad => ad.Id.Value.ToString("D"), StringComparer.Ordinal)
            .ToList();
        foreach (var ad in tied)
            await AddAsync(ad);
        var oldest = await AddAsync(Imported("Praktikant", DataIt, Solna, StockholmsLan, Now.AddDays(-3)));

        var page = await PageAsync();

        var ties = tied.Select(ad => ad.Id.Value).OrderBy(id => id.ToString("D"), StringComparer.Ordinal);
        page.Rows.Items.Select(r => r.Id).ShouldBe([newest, .. ties, oldest]);
    }

    [Fact]
    public async Task Page_SoonestLastDayFirst_AndTheAdsWithoutALastDayLast_InTheReadsIdOrder()
    {
        var later = await AddAsync(Imported("Junior A", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1), Now.AddDays(10)));
        var soonest = await AddAsync(Imported("Junior B", DataIt, Stockholm, StockholmsLan, Now.AddDays(-3), Now.AddDays(1)));
        var open = new[]
            {
                Imported("Junior C", DataIt, Stockholm, StockholmsLan, Now.AddHours(-1)),
                Imported("Junior D", DataIt, Stockholm, StockholmsLan, Now.AddDays(-5)),
            }
            .OrderByDescending(ad => ad.Id.Value.ToString("D"), StringComparer.Ordinal)
            .ToList();
        foreach (var ad in open)
            await AddAsync(ad);

        var page = await PageAsync(sortBy: JobAdSortBy.ExpiresAtAsc);

        var openInIdOrder = open.Select(ad => ad.Id.Value).OrderBy(id => id.ToString("D"), StringComparer.Ordinal);
        page.Rows.Items.Select(r => r.Id).ShouldBe([soonest, later, .. openInIdOrder]);
    }

    [Fact]
    public async Task Page_AChosenKommun_ListsAndCountsThatKommunAlone_WhileOfferingEveryKommun()
    {
        var newer = await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        var older = await AddAsync(Imported("Trainee", OtherDataIt, Stockholm, StockholmsLan, Now.AddDays(-3)));
        await AddAsync(Imported("Praktikant", DataIt, Goteborg, VastraGotalandsLan, Now.AddDays(-2)));

        var page = await PageAsync(Stockholm);

        page.Rows.Items.Select(r => r.Id).ShouldBe([newer, older]);
        page.Rows.TotalCount.ShouldBe(2);
        page.Places.ShouldBe([
            new PublicJuniorItPlaceDto(Goteborg, "Göteborg", 1),
            new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 2),
        ]);
    }

    [Fact]
    public async Task Read_BothQueries_CarryBothPredicates_SendThePatternToTildeStarAsAParameter_AndReadANarrowRow()
    {
        await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        _commands.Clear();

        await PageAsync();

        _commands.Commands.Count.ShouldBe(2, "the grouped count and the page");
        foreach (var (text, parameters) in _commands.Commands)
        {
            text.ShouldContain("~*");
            parameters.ShouldContain(List.HeadlinePattern, "the pattern is bound as a parameter");
            text.ShouldNotContain("trainee", Case.Insensitive, "the pattern never reaches the SQL text");
            parameters.ShouldContain(CuratedInField, "the curated ids are bound as an array parameter");
            text.ShouldNotContain(CuratedInField, Case.Sensitive, "a curated id never reaches the SQL text");
            text.ShouldContain("occupation_group_concept_id");
            text.ShouldContain("external_id");
            text.ShouldContain("external_source");
            text.ShouldContain("expires_at");
            text.ShouldNotContain("raw_payload", Case.Insensitive, "each read projects a narrow row, never the entity");
            text.ShouldNotContain("description", Case.Insensitive);
        }

        _commands.Commands[0].Text.ShouldContain("GROUP BY");
        _commands.Commands[1].Text.ShouldContain("LIMIT");
    }

    private async Task<PublicJuniorItJobAdsPage.Available> PageAsync(
        string? kommun = null, JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc, int page = 1) =>
        (await HandleAsync(kommun, sortBy, page)).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

    private async Task<PublicJuniorItJobAdsPage> HandleAsync(
        string? kommun = null, JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc, int page = 1)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var port = new JuniorItJobAdQuery(db, new JuniorItSetResolver(List, _taxonomy));
        return await new GetPublicJuniorItJobAdsQueryHandler(port, _taxonomy, _clock)
            .Handle(new GetPublicJuniorItJobAdsQuery(kommun, sortBy, page), Ct);
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

    /// <summary>The stream's removal path: the command handler, then the save the unit-of-work behaviour makes.</summary>
    private async Task ArchiveThroughTheStreamAsync(string externalId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await new ArchiveExternalJobAdCommandHandler(
                db, _clock, NullLogger<ArchiveExternalJobAdCommandHandler>.Instance)
            .Handle(new ArchiveExternalJobAdCommand(JobSource.Platsbanken, externalId), Ct);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ArchiveOutcome.Archived);
        await db.SaveChangesAsync(Ct);
    }

    private async Task RunExpireJobAdsJobAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await new ExpireJobAdsJob(db, _clock, Substitute.For<ISystemEventAuditor>(), NullLogger<ExpireJobAdsJob>.Instance)
            .RunAsync(Ct);
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

    private async Task<JobAdStatus> StatusOfAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobAds.AsNoTracking().Where(j => j.Id == new JobAdId(id)).Select(j => j.Status).SingleAsync(Ct);
    }

    private JobAd Imported(
        string title, string? group, string? kommun, string? lan, DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt = null, string? externalId = null)
    {
        externalId ??= $"8{Interlocked.Increment(ref _externalIds):D7}";
        return JobAd.Import(
            title: title,
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(occupationGroup: group, municipality: kommun, region: lan),
            declaredContacts: [],
            publishedAt: publishedAt,
            expiresAt: expiresAt,
            clock: _clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
    }

    /// <summary>What <c>POST /api/v1/job-ads</c> stores: any source, and no import provenance or facets.</summary>
    private JobAd NeverImported(string title) =>
        JobAd.Create(
            title, Company.Create("Exempelbolaget AB").Value, "Text som någon skrev själv.",
            "https://example.com/jobb/1", JobSource.Platsbanken, Now.AddHours(-1), null, _clock).Value;

    /// <summary>The text and the string parameter values of every command that reads <c>job_ads</c>, in order.</summary>
    private sealed class CommandCapture : DbCommandInterceptor
    {
        private readonly List<(string Text, IReadOnlyList<string> Parameters)> _commands = [];

        public IReadOnlyList<(string Text, IReadOnlyList<string> Parameters)> Commands
        {
            get { lock (_commands) return [.. _commands]; }
        }

        public void Clear()
        {
            lock (_commands) _commands.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("job_ads", StringComparison.Ordinal))
            {
                IReadOnlyList<string> parameters =
                    [.. command.Parameters.Cast<DbParameter>().SelectMany(p => p.Value switch
                    {
                        string value => new[] { value },
                        IEnumerable<string> values => values,
                        _ => Array.Empty<string>(),
                    })];
                lock (_commands) _commands.Add((command.CommandText, parameters));
            }

            return ValueTask.FromResult(result);
        }
    }
}
