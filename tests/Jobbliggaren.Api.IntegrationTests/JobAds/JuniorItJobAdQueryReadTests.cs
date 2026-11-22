using System.Data.Common;
using System.Runtime.CompilerServices;
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
using Npgsql;
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
/// read, as the Worker's two-minute sweep does), decided through <c>JobAdJuniorAssessment.Decide</c> (the admin command's
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
    public async Task Read_EveryQuery_ReadsTheStoredClassification_UnderTheListedGate_AndReadsANarrowRow()
    {
        await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        await ClassifyAsync();
        _commands.Clear();

        await HandleWithoutSweepAsync();

        _commands.Commands.Count.ShouldBe(3, "the grouped count, the page and the floor");
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
        _commands.Commands[2].Text.ShouldStartWith("SELECT EXISTS");
        _commands.Commands[2].Text.ShouldContain("organization_number");
        _commands.Results[2].ShouldBe((1, typeof(bool)), "only the floor's outcome leaves the database, never a count per employer");
    }

    [Fact]
    public async Task TheCountAndThePage_SendTheStatementsOfTheirOwnInlineBase_SoTheSharedBaseChangesNoSql()
    {
        await AddAsync(Imported("Junior utvecklare", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1)));
        await ClassifyAsync();
        _commands.Clear();

        await HandleWithoutSweepAsync(Stockholm, JobAdSortBy.ExpiresAtAsc);

        // The chains the two members composed inline before the floor shared their base (#2293), as they were written.
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var groups = JuniorMembership.GroupsOf(await Catalog().ResolveEnabledAsync(Ct));
        var count = db.JobAds
            .AsNoTracking()
            .Where(Jobbliggaren.Application.JobAds.Publication.JobAdPublication.IsListed(Now))
            .Where(JuniorMembership.Of(db.JuniorAssessments, groups))
            .GroupBy(j => j.MunicipalityConceptId)
            .Select(g => new Jobbliggaren.Application.JobAds.Abstractions.JuniorItMunicipalityCount(g.Key, g.Count()));
        IQueryable<Jobbliggaren.Application.JobAds.Abstractions.JuniorItJobAdRow> Page(
            DateTimeOffset now, string? municipalityConceptId, JobAdSortBy sortBy, int skip, int take)
        {
            var source = db.JobAds
                .AsNoTracking()
                .Where(Jobbliggaren.Application.JobAds.Publication.JobAdPublication.IsListed(now))
                .Where(JuniorMembership.Of(db.JuniorAssessments, groups));

            if (municipalityConceptId is { } kommun)
                source = source.Where(j => j.MunicipalityConceptId == kommun);

            return JobAdSearchComposition
                .ApplySort(source, sortBy,
                    new ResolvedSearchText(null, [], Jobbliggaren.Application.JobAds.Abstractions.TaxonomyLabelMatchDto.Empty))
                .Skip(skip)
                .Take(take)
                .Select(j => new Jobbliggaren.Application.JobAds.Abstractions.JuniorItJobAdRow(
                    j.Id.Value, j.Title, j.Company.Name, j.MunicipalityConceptId, j.RegionConceptId, j.PublishedAt, j.ExpiresAt));
        }

        StatementOf(_commands.Commands[0].Text).ShouldBe(StatementOf(count.ToQueryString()));
        StatementOf(_commands.Commands[1].Text).ShouldBe(StatementOf(
            Page(Now, Stockholm, JobAdSortBy.ExpiresAtAsc, 0, PublicJuniorItJobAdLimits.PageSize).ToQueryString()));
    }

    // ToQueryString leads with one comment line per parameter, which the command as sent does not carry; the line breaks
    // are the platform's in one and not the other.
    private static string StatementOf(string sql) =>
        string.Join("\n", sql.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .SkipWhile(line => line.StartsWith("--", StringComparison.Ordinal)));

    // ---- The floor for indexing (ADR 0166, the Amendment 2026-11-22). Each list is seeded at the real thresholds through
    // the importer in one save, never through a hook on the thresholds: a configurable floor is trigger 20.

    [Fact]
    public async Task Floor_Holds_AtAHundredAdsFromTwentyFiveEmployers()
    {
        await SeedAsync(AtTheThresholds());

        (await FloorAsync()).ShouldBeTrue();
        (await FloorReadingAsync())[4].Value.ShouldBe("1");
    }

    [Fact]
    public async Task Floor_Holds_WhenTheTwentyFifthEmployerFilesUnderOneNumberAndTwoNames()
    {
        List<Employer> seed = [.. TwentyFourWithFourAdsEach()];
        seed.AddRange(Enumerable.Repeat(EmployerNo(25), 2));
        seed.AddRange(Enumerable.Repeat(new Employer(NumberOf(25), "Tjugofem AB"), 2));
        await SeedAsync(seed);

        (await FloorAsync()).ShouldBeTrue();
        (await FloorReadingAsync())[1].Value.ShouldBe("25");
    }

    [Fact]
    public async Task Floor_Holds_WithTheAdsThatNameNoKommun()
    {
        var seed = AtTheThresholds();
        await SeedAsync(seed.Take(96));
        await SeedAsync(seed.Skip(96), kommun: null, lan: null);

        (await FloorAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Floor_Fails_OneAdBelowAHundred()
    {
        var seed = AtTheThresholds();
        seed.RemoveAt(seed.Count - 1);
        await SeedAsync(seed);

        (await FloorAsync()).ShouldBeFalse();
    }

    public static TheoryData<string> MergesTheTwentyFifthEmployer =>
    [
        "a number it shares with another employer, under its own name",
        "a name it shares with another employer, under its own number",
        "a name that differs from another employer's only in case",
        "no number on any of its ads",
    ];

    [Theory]
    [MemberData(nameof(MergesTheTwentyFifthEmployer))]
    public async Task Floor_Fails_WhenTheDefinitionCountsTheTwentyFifthEmployerAsNoNewOne(string merge)
    {
        Employer twentyFifth = merge switch
        {
            "a number it shares with another employer, under its own name" => new(NumberOf(24), "Bolag 25 AB"),
            "a name it shares with another employer, under its own number" => new(NumberOf(25), "Bolag 24 AB"),
            "a name that differs from another employer's only in case" => new(NumberOf(25), "BOLAG 24 AB"),
            _ => new(null, "Bolag 25 AB"),
        };
        await SeedAsync(AtTheThresholds().Select(e => e == EmployerNo(25) ? twentyFifth : e));

        (await FloorAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Floor_Fails_ForTwentyFourEmployers_WhenOneUsesTwoNumbersAndAnotherTwoNames()
    {
        // Employer 1 also files under a second number, employer 2 also under a second name: each key alone reads 25.
        List<Employer> seed = [.. TwentyFourWithFourAdsEach()];
        seed.AddRange([new(NumberOf(91), "Bolag 1 AB"), new(NumberOf(91), "Bolag 1 AB")]);
        seed.AddRange([new(NumberOf(2), "Bolag 92 AB"), new(NumberOf(2), "Bolag 92 AB")]);
        await SeedAsync(seed);

        (await FloorAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Floor_Fails_ForTwentyFourEmployers_WhenOneIsChainedAcrossBothKeys()
    {
        // One employer as A–X, A–Y and B–Y: two numbers and two names.
        List<Employer> seed = [.. Enumerable.Range(2, 23).SelectMany(i => Enumerable.Repeat(EmployerNo(i), 4))];
        seed.AddRange(Enumerable.Repeat(new Employer(NumberOf(81), "Kedjan X AB"), 3));
        seed.AddRange(Enumerable.Repeat(new Employer(NumberOf(81), "Kedjan Y AB"), 3));
        seed.AddRange(Enumerable.Repeat(new Employer(NumberOf(82), "Kedjan Y AB"), 2));
        await SeedAsync(seed);

        (await FloorAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Floor_Holds_WhenTheLargestEmployerHasExactlyHalf()
    {
        await SeedAsync(HalfFromOneEmployer());

        (await FloorAsync()).ShouldBeTrue();
    }

    public static TheoryData<string> OneMoreForTheLargestEmployer =>
    [
        "under a second name, with its number",
        "under a second number, with its name",
        "with no number, under any name",
    ];

    [Theory]
    [MemberData(nameof(OneMoreForTheLargestEmployer))]
    public async Task Floor_Fails_WhenTheLargestEmployerHasOneAdMoreThanHalf(string extra)
    {
        var seed = HalfFromOneEmployer();
        seed.Add(extra switch
        {
            "under a second name, with its number" => new(NumberOf(1), "Annat namn AB"),
            "under a second number, with its name" => new(NumberOf(99), "Bolag 1 AB"),
            _ => new(null, "Helt annat AB"),
        });
        await SeedAsync(seed);

        (await FloorAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Floor_Fails_WhenNoAdCarriesANumber()
    {
        await SeedAsync(Enumerable.Range(1, 100).Select(i => new Employer(null, $"Bolag {i} AB")));

        (await FloorAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Floor_Fails_OnAnEmptyList()
    {
        (await FloorAsync()).ShouldBeFalse();
    }

    public static TheoryData<string> LeavesTheList =>
    [
        "archived by the stream's removal event",
        "past its deadline",
        "archived by ExpireJobAdsJob",
        "erased",
        "decided not a junior job",
    ];

    [Theory]
    [MemberData(nameof(LeavesTheList))]
    public async Task Floor_AnAdThatLeavesTheList_ClosesAFloorThatHeldAtTheThreshold(string removal)
    {
        var seed = AtTheThresholds();
        seed.RemoveAt(seed.Count - 1);
        await SeedAsync(seed);
        var deadline = removal is "past its deadline" or "archived by ExpireJobAdsJob" ? Now.AddMinutes(1) : (DateTimeOffset?)null;
        var leaving = Imported("Junior utvecklare sist", DataIt, Stockholm, StockholmsLan, Now.AddDays(-1), deadline,
            company: EmployerNo(25).Name, organizationNumber: EmployerNo(25).Number);
        await AddAsync(leaving);
        (await FloorAsync()).ShouldBeTrue();

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
                break;
            case "erased":
                await EraseAsync(leaving.Id.Value);
                break;
            default:
                await DecideAsync(leaving.Id.Value, JuniorDecision.NotJunior);
                break;
        }

        (await FloorAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Floor_IsTheWholeListsOutcome_ForAKommunTheDeadlineOrderAndALaterPage()
    {
        var seed = AtTheThresholds();
        await SeedAsync(seed.Take(60), Stockholm, StockholmsLan);
        await SeedAsync(seed.Skip(60), Goteborg, VastraGotalandsLan);

        (await PageAsync()).FloorHolds.ShouldBeTrue();
        (await PageAsync(Goteborg)).FloorHolds.ShouldBeTrue();
        (await PageAsync(sortBy: JobAdSortBy.ExpiresAtAsc)).FloorHolds.ShouldBeTrue();
        (await PageAsync(page: 2)).FloorHolds.ShouldBeTrue();
        (await FloorReadingAsync())[4].Value.ShouldBe("0");
    }

    /// <summary>One ad's employer: its organisation number, or none, and its company name.</summary>
    private readonly record struct Employer(string? Number, string Name);

    private static string NumberOf(int employer) => $"556{employer:D7}";

    private static Employer EmployerNo(int employer) => new(NumberOf(employer), $"Bolag {employer} AB");

    private static IEnumerable<Employer> TwentyFourWithFourAdsEach() =>
        Enumerable.Range(1, 24).SelectMany(i => Enumerable.Repeat(EmployerNo(i), 4));

    /// <summary>Twenty-five employers with four ads each: a hundred ads, the floor's thresholds exactly.</summary>
    private static List<Employer> AtTheThresholds() =>
        [.. TwentyFourWithFourAdsEach(), .. Enumerable.Repeat(EmployerNo(25), 4)];

    /// <summary>Fifty ads from one employer and two each from twenty-five others: the largest employer has exactly half.</summary>
    private static List<Employer> HalfFromOneEmployer() =>
        [.. Enumerable.Repeat(EmployerNo(1), 50), .. Enumerable.Range(2, 25).SelectMany(i => Enumerable.Repeat(EmployerNo(i), 2))];

    /// <summary>Imports one junior Data/IT ad per employer entry in one save, as a sync batch lands.</summary>
    private async Task SeedAsync(IEnumerable<Employer> employers, string? kommun = Stockholm, string? lan = StockholmsLan)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hours = 0;
        foreach (var employer in employers)
        {
            hours++;
            db.JobAds.Add(Imported($"Junior utvecklare {hours}", DataIt, kommun, lan, Now.AddHours(-hours),
                company: employer.Name, organizationNumber: employer.Number));
        }

        await db.SaveChangesAsync(Ct);
    }

    private static readonly string[] FloorReadingLabels =
    [
        "junior members (N)",
        "distinct employers (E)",
        "s_ok (no employer above half)",
        "floor holds",
        "kommuner meeting the floor",
    ];

    /// <summary>
    /// The port's answer, checked against the committed merge-day reading over the same seed: the reading computes the same
    /// definition, and prints the labelled totals only (security-auditor's floor-key ruling, pin 3).
    /// </summary>
    private async Task<bool> FloorAsync()
    {
        var page = await PageAsync();
        var holds = page.FloorHolds;

        var reading = await FloorReadingAsync();
        reading.Select(r => r.Label).ShouldBe(FloorReadingLabels);
        reading[0].Value.ShouldBe(page.Rows.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        reading[3].Value.ShouldBe(holds ? "true" : "false", "the merge-day reading computes the port's definition");
        var employers = reading[1].Value;
        if (employers == "E under 25")
        {
            reading[2].Value.ShouldBe("E under 25", "below 25 employers the reading prints neither E nor S");
        }
        else
        {
            int.Parse(employers, System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(25);
            reading[2].Value.ShouldBeOneOf("true", "false");
            if (page.Rows.TotalCount >= 100)
                reading[2].Value.ShouldBe(reading[3].Value);
        }

        return holds;
    }

    /// <summary>
    /// Runs <c>docs/runbooks/junior-list-floor-count.sql</c> as committed. It reads the database's clock and this class
    /// reads its own, so the class's instant stands in for <c>now()</c>.
    /// </summary>
    private async Task<IReadOnlyList<(string Label, string Value)>> FloorReadingAsync()
    {
        var sql = (await File.ReadAllTextAsync(FloorReadingPath(), Ct))
            .Replace("now()", $"'{_clock.UtcNow:O}'::timestamptz", StringComparison.Ordinal);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<(string Label, string Value)>();
        do
        {
            while (await reader.ReadAsync(Ct))
                rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        while (await reader.NextResultAsync(Ct));

        return rows;
    }

    // thisFile = <repo>/tests/Jobbliggaren.Api.IntegrationTests/JobAds/JuniorItJobAdQueryReadTests.cs → up three = repo root.
    private static string FloorReadingPath([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "..", "docs", "runbooks", "junior-list-floor-count.sql"));

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

    /// <summary>
    /// Erases an ad through the aggregate, as <c>EraseRecruiterAdsCommand</c> does. The command also deletes the ad's
    /// assessment; here the sweep that runs before every read purges it, as it purges any assessment left on an erased
    /// ad (<c>ClassifyJuniorJobAdsJob</c>).
    /// </summary>
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
        DateTimeOffset? expiresAt = null, string? externalId = null, string description = "Vi söker en utvecklare.",
        string company = "Exempelbolaget AB", string? organizationNumber = null)
    {
        externalId ??= $"8{Interlocked.Increment(ref _externalIds):D7}";
        return JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
            description: description,
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(
                occupationGroup: group, municipality: kommun, region: lan, organizationNumber: organizationNumber),
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

    /// <summary>
    /// The text and the string parameter values of every command that reads <c>job_ads</c>, in order, and the shape of
    /// each one's result: its number of columns and the type of the first.
    /// </summary>
    private sealed class CommandCapture : DbCommandInterceptor
    {
        private readonly List<(string Text, IReadOnlyList<string> Parameters)> _commands = [];
        private readonly List<(int Columns, Type? FirstColumn)> _results = [];

        public IReadOnlyList<(string Text, IReadOnlyList<string> Parameters)> Commands
        {
            get { lock (_commands) return [.. _commands]; }
        }

        public IReadOnlyList<(int Columns, Type? FirstColumn)> Results
        {
            get { lock (_commands) return [.. _results]; }
        }

        public void Clear()
        {
            lock (_commands)
            {
                _commands.Clear();
                _results.Clear();
            }
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("job_ads", StringComparison.Ordinal))
            {
                lock (_commands) _results.Add((result.FieldCount, result.FieldCount > 0 ? result.GetFieldType(0) : null));
            }

            return ValueTask.FromResult(result);
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
