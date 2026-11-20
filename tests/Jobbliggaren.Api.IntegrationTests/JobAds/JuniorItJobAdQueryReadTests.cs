using System.Data.Common;
using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.JobAds.Junior;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The open junior IT list on real Postgres (ADR 0186, ADR 0187): the real <c>JuniorItJobAdQuery</c> behind the real
/// handler, the field's groups from the real taxonomy snapshot through the real <c>JuniorFieldResolver</c>, and a
/// synthetic rule file read through the real loader. Its own database per test, because the exact set, the total and the
/// kommun options are the assertion.
/// </summary>
/// <remarks>
/// Every row is produced the way production produces it: imported through <c>JobAd.Import</c>, created as
/// <c>POST /api/v1/job-ads</c> stores it, classified by the real <c>ClassifyJuniorJobAdsJob</c> (which runs before every
/// read, as the Worker's two-minute sweep does), decided through <c>JobAd.DecideJunior</c> (the admin command's
/// aggregate call), archived through <c>ArchiveExternalJobAdCommand</c> and <c>ExpireJobAdsJob</c>, erased through the
/// aggregate, and past its deadline by the clock. Instants are whole seconds; ties are ordered by the uuid, whose order
/// on Postgres is the ordinal order of its lower-case <c>"D"</c> string.
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

    // Synthetic Platsbanken-shaped ids, outside the shipped reading's range.
    private const string CuratedInField = "90000001";
    private const string CuratedOutsideField = "90000002";

    // A synthetic rule file through the real loader: the reading names the two ids above; a junior, trainee or
    // praktikant title is junior automatically; a "handleda" text queues the ad; a senior title excludes it.
    private static string RuleFile(bool titleRuleIsAuto = true) => $$"""
        {
          "ruleSetVersion": "1",
          "occupationFieldConceptId": "{{DataItField}}",
          "enabled": true,
          "reading": { "ruleId": "reading-test", "junior": ["{{CuratedInField}}", "{{CuratedOutsideField}}"] },
          "patterns": {
            "candidate": "\\b(junior|trainee|praktikant)",
            "juniorTitle": "\\b(junior|trainee|praktikant)",
            "seniorTitle": "\\bsenior",
            "mentor": "handleda",
            "years": "\\b(?<from>\\d{1,2}) års erfarenhet"
          },
          "candidatePatterns": ["candidate"],
          "candidateWhenNoExperienceRequired": false,
          "years": { "patterns": ["years"], "numberWords": {}, "plausibleMin": 1, "plausibleMax": 15 },
          "rules": [
            { "id": "title-senior", "verdict": "NotJunior", "auto": true, "all": [ { "on": "title", "pattern": "seniorTitle" } ] },
            { "id": "text-mentor", "verdict": "NotJunior", "auto": false, "all": [ { "on": "text", "pattern": "mentor" } ] },
            { "id": "title-junior", "verdict": "Junior", "auto": {{(titleRuleIsAuto ? "true" : "false")}}, "all": [ { "on": "title", "pattern": "juniorTitle" } ] }
          ],
          "candidateFallbackRuleId": "candidate-only",
          "noSignalRuleId": "no-signal",
          "timeoutRuleId": "match-timeout"
        }
        """;

    private static JuniorRuleSet LoadRules(bool titleRuleIsAuto = true) =>
        JuniorRuleSetLoader.LoadFrom(new MemoryStream(Encoding.UTF8.GetBytes(RuleFile(titleRuleIsAuto))));

    private JuniorRuleSet _rules = LoadRules();

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

        var dataIt = tree.OccupationFields.Single(f => f.ConceptId == _rules.OccupationFieldConceptId);
        dataIt.ConceptId.ShouldBe(DataItField);
        dataIt.OccupationGroups.Select(g => g.ConceptId).ShouldContain(DataIt);
        dataIt.OccupationGroups.Select(g => g.ConceptId).ShouldContain(OtherDataIt);
        dataIt.OccupationGroups.Select(g => g.ConceptId).ShouldNotContain(NotDataIt);
        tree.OccupationFields.SelectMany(f => f.OccupationGroups).Select(g => g.ConceptId).ShouldContain(NotDataIt);
        JuniorRuleSetLoader.LoadDataIt().Reading.JuniorExternalIds.ShouldNotContain(CuratedInField);
        JuniorRuleSetLoader.LoadDataIt().Reading.JuniorExternalIds.ShouldNotContain(CuratedOutsideField);
    }

    [Fact]
    public async Task Read_ListsExactlyTheDataItAdsThatTheReadingOrAnAutomaticRuleClassifiedJunior()
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
    public async Task Read_AnUnclearAd_IsNotListed_UntilADecisionSaysJunior()
    {
        var unclear = Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1),
            description: "Du ska handleda nya kollegor.");
        await AddAsync(unclear);

        (await PageAsync()).Rows.TotalCount.ShouldBe(0);
        (await VerdictOfAsync(unclear.Id.Value)).ShouldBe((JuniorVerdict.Unclear, "text-mentor"));

        await DecideAsync(unclear.Id.Value, JuniorDecision.Junior);

        (await PageAsync()).Rows.Items.Select(r => r.Id).ShouldBe([unclear.Id.Value]);
    }

    [Fact]
    public async Task Read_ADecisionNotJunior_TakesAnAutomaticJuniorOffTheList()
    {
        var shown = await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        (await PageAsync()).Rows.Items.Select(r => r.Id).ShouldBe([shown]);

        await DecideAsync(shown, JuniorDecision.NotJunior);

        (await PageAsync()).Rows.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task ANewRuleVersion_ReclassifiesEveryOpenAd_AndLeavesEveryDecision()
    {
        var decided = await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        var ruled = await AddAsync(Imported("Trainee inom IT", DataIt, Goteborg, VastraGotalandsLan, Now.AddDays(-2)));
        (await PageAsync()).Rows.Items.Select(r => r.Id).ShouldBe([decided, ruled]);
        await DecideAsync(decided, JuniorDecision.Junior);

        // A deploy of a new rule file: the title rule is no longer automatic, so the version (with its content hash)
        // changes and the sweep re-classifies both ads.
        _rules = LoadRules(titleRuleIsAuto: false);

        (await PageAsync()).Rows.Items.Select(r => r.Id).ShouldBe([decided]);
        (await VerdictOfAsync(ruled)).ShouldBe((JuniorVerdict.Unclear, "title-junior"));
        (await VerdictOfAsync(decided)).ShouldBe((JuniorVerdict.Unclear, "title-junior"));
    }

    [Fact]
    public async Task ARewrittenAd_IsClassifiedAgainOnTheNextSweep()
    {
        var ad = Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1));
        await AddAsync(ad);
        (await PageAsync()).Rows.Items.Select(r => r.Id).ShouldBe([ad.Id.Value]);

        await RewriteTitleAsync(ad.Id.Value, "Senior utvecklare");

        (await PageAsync()).Rows.TotalCount.ShouldBe(0);
        // The new title carries no junior signal at all, so the rules never reach title-senior.
        (await VerdictOfAsync(ad.Id.Value)).ShouldBe((JuniorVerdict.NotJunior, "no-signal"));
    }

    [Fact]
    public async Task Read_BothQueries_ReadTheStoredClassification_UnderTheListedGate_AndReadANarrowRow()
    {
        await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        await ClassifyAsync();
        _commands.Clear();

        await HandleWithoutSweepAsync();

        _commands.Commands.Count.ShouldBe(2, "the grouped count and the page");
        foreach (var (text, _) in _commands.Commands)
        {
            text.ShouldNotContain("~*", Case.Sensitive, "no regex runs on the request path");
            text.ShouldContain("job_ad_junior_assessments");
            text.ShouldContain("verdict");
            text.ShouldContain("decision");
            text.ShouldContain("occupation_group_concept_id");
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

    /// <summary>The Worker's sweep has run, then the request reads.</summary>
    private async Task<PublicJuniorItJobAdsPage> HandleAsync(
        string? kommun = null, JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc, int page = 1)
    {
        await ClassifyAsync();
        return await HandleWithoutSweepAsync(kommun, sortBy, page);
    }

    private async Task<PublicJuniorItJobAdsPage> HandleWithoutSweepAsync(
        string? kommun = null, JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc, int page = 1)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var port = new JuniorItJobAdQuery(db, Catalog());
        return await new GetPublicJuniorItJobAdsQueryHandler(port, _taxonomy, _clock)
            .Handle(new GetPublicJuniorItJobAdsQuery(kommun, sortBy, page), Ct);
    }

    private JuniorFieldResolver Catalog() => new([_rules], _taxonomy);

    /// <summary>One run of the real sweep, as the Worker's two-minute tick makes it.</summary>
    private async Task ClassifyAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await new ClassifyJuniorJobAdsJob(
                _provider.GetRequiredService<IServiceScopeFactory>(), db, Catalog(), _clock,
                Options.Create(new ClassifyJuniorJobAdsOptions()), NullLogger<ClassifyJuniorJobAdsJob>.Instance)
            .RunAsync(Ct);
    }

    /// <summary>The admin command's aggregate call: the assessment the sweep created, loaded, decided, saved.</summary>
    private async Task DecideAsync(Guid id, JuniorDecision decision)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assessment = await db.JuniorAssessments.Where(a => a.Id == new JobAdId(id)).SingleAsync(Ct);
        assessment.Decide(decision, _clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The stream's re-import of a changed ad: <c>UpdateFromSource</c> with the source's new title.</summary>
    private async Task RewriteTitleAsync(Guid id, string title)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.Where(j => j.Id == new JobAdId(id)).SingleAsync(Ct);
        ad.UpdateFromSource(
                title, ad.Description, ad.Url, ad.RawPayload, TestFacets.From(occupationGroup: ad.OccupationGroupConceptId,
                    municipality: ad.MunicipalityConceptId, region: ad.RegionConceptId),
                [], ad.PublishedAt, ad.ExpiresAt, TestKeywordExtraction.None, applicationUrl: null)
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private async Task<(JuniorVerdict? Verdict, string? RuleId)> VerdictOfAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.JuniorAssessments.AsNoTracking().Where(a => a.Id == new JobAdId(id))
            .Select(a => new { a.Verdict, a.RuleId }).SingleAsync(Ct);
        return (row.Verdict, row.RuleId);
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
        DateTimeOffset? expiresAt = null, string? externalId = null, string description = "Vi söker en utvecklare.")
    {
        externalId ??= $"8{Interlocked.Increment(ref _externalIds):D7}";
        return JobAd.Import(
            title: title,
            company: Company.Create("Exempelbolaget AB").Value,
            description: description,
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
