using System.Data.Common;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The related section's read on real Postgres (ADR 0182): the tier rank, the NULL ordering, the id tie-break and the
/// publication predicate translate as the handler means them. Its own database per test, because the exact set and its
/// order are the assertion.
/// </summary>
/// <remarks>
/// Every row is produced the way production produces it: imported through <c>JobAd.Import</c>, created as
/// <c>POST /api/v1/job-ads</c> stores it, archived and erased through the aggregate, and past its deadline by the clock.
/// Instants are whole seconds. Ties are ordered by the uuid, whose order on Postgres is the ordinal order of its
/// lower-case <c>"D"</c> string: that string is the oracle.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class RelatedPublicJobAdsReadTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private const string Group = "DJh5_yyF_hEM";
    private const string Kommun = "AvNB_uwa_6n6";
    private const string Lan = "CaRE_1nn_hRb";
    private const string FarKommun = "PVZL_BQT_XtL";
    private const string FarLan = "zdoY_6u5_Krt";
    private const string AnchorEmployer = "Ankarbolaget AB";

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
    public async Task Related_ListsTheGroupsOtherListedEmployers_SameKommunFirst_WithTiesInTheReadsIdOrder()
    {
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan));
        var rest = await AddAsync(Imported("Övriga landet", "A AB", Now.AddHours(-1), FarKommun, FarLan));
        // The tie is inserted against its oracle, the higher "D" string first, so only the id order can put it right.
        var tied = new[]
            {
                Imported("Lika A", "B AB", Now.AddDays(-2), Kommun, Lan),
                Imported("Lika B", "C AB", Now.AddDays(-2), Kommun, Lan, Now.AddDays(5)),
            }
            .OrderByDescending(ad => ad.Id.Value.ToString("D"), StringComparer.Ordinal)
            .ToList();
        foreach (var ad in tied)
            await AddAsync(ad);
        var lan = await AddAsync(Imported("Samma län", "D AB", Now.AddHours(-2), null, Lan));
        await AddAsync(Imported("Samma arbetsgivare", AnchorEmployer, Now.AddHours(-3), Kommun, Lan));
        await AddAsync(Imported("Annan grupp", "E AB", Now.AddHours(-1), Kommun, Lan, group: "Q5DF_juj_8do"));
        await AddAsync(Imported("Arkiverad", "G AB", Now.AddHours(-1), Kommun, Lan), ad => ad.Archive(_clock).IsSuccess.ShouldBeTrue());
        await AddAsync(Imported("Raderad", "H AB", Now.AddHours(-1), Kommun, Lan), ad => ad.Erase(_clock).IsSuccess.ShouldBeTrue());
        await AddAsync(Imported("Utgången", "I AB", Now.AddHours(-1), Kommun, Lan, Now.AddSeconds(-1)));

        var items = await RelatedAsync(anchor);

        var ties = tied.Select(ad => ad.Id.Value).OrderBy(id => id.ToString("D"), StringComparer.Ordinal);
        items.ShouldBe([.. ties, lan, rest]);
    }

    [Fact]
    public async Task Related_ARowWithoutAKommun_NeverOutranksAKommunMatch()
    {
        // Postgres sorts NULL first under DESC. The rank is a CASE whose ELSE is 2, so a NULL kommun cannot reach the
        // kommun tier whichever way the rank is ordered.
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan));
        var noKommunNewest = await AddAsync(Imported("Utan kommun, nyast", "A AB", Now.AddMinutes(-1), null, null));
        var kommunOldest = await AddAsync(Imported("Samma kommun, äldst", "B AB", Now.AddDays(-9), Kommun, Lan));

        var items = await RelatedAsync(anchor);

        items.ShouldBe([kommunOldest, noKommunNewest]);
    }

    [Fact]
    public async Task Related_AnAnchorWithoutAKommun_RanksARowWithoutAKommunInTheLastTier()
    {
        // A null anchor kommun must not become `municipality_concept_id IS NULL`. The anchor's state is producible:
        // PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_PassesAnAddressBlockWithOneIdThroughUnpaired.
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), null, Lan));
        var noPlaceNewest = await AddAsync(Imported("Utan ort, nyast", "A AB", Now.AddMinutes(-1), null, null));
        var sameLanOldest = await AddAsync(Imported("Samma län, äldst", "B AB", Now.AddDays(-9), FarKommun, Lan));

        var items = await RelatedAsync(anchor);

        items.ShouldBe([sameLanOldest, noPlaceNewest]);
    }

    [Fact]
    public async Task Related_TakesOneRowPerEmployer_AndAtMostFive()
    {
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan));
        var expected = new List<Guid>();
        for (var i = 1; i <= 7; i++)
        {
            var first = await AddAsync(Imported($"Annons {i}", $"Arbetsgivare {i} AB", Now.AddHours(-i), FarKommun, FarLan));
            await AddAsync(Imported($"Annons {i}, äldre", $"Arbetsgivare {i} AB", Now.AddHours(-i).AddMinutes(-30), FarKommun, FarLan));
            if (i <= RelatedPublicJobAdLimits.MaxRows)
                expected.Add(first);
        }

        var items = await RelatedAsync(anchor);

        items.ShouldBe(expected);
    }

    [Fact]
    public async Task Related_AnAdErasedThroughTheProductionPath_IsAbsentAtOnce()
    {
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan));
        var kept = await AddAsync(Imported("Kvar", "A AB", Now.AddHours(-2), Kommun, Lan));
        var erased = await AddAsync(Imported("Raderas", "B AB", Now.AddHours(-1), Kommun, Lan));
        (await RelatedAsync(anchor)).ShouldBe([erased, kept]);

        await EraseAsync(erased);

        (await RelatedAsync(anchor)).ShouldBe([kept]);
    }

    [Fact]
    public async Task Related_AnAdWhoseDeadlinePasses_IsAbsent()
    {
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan));
        var open = await AddAsync(Imported("Öppen", "A AB", Now.AddHours(-2), Kommun, Lan));
        await AddAsync(Imported("Går ut", "B AB", Now.AddHours(-1), Kommun, Lan, Now.AddMinutes(1)));

        _clock.UtcNow = Now.AddMinutes(2);

        (await RelatedAsync(anchor)).ShouldBe([open]);
    }

    public static TheoryData<string> NonListedAnchors => ["unknown", "never imported", "archived", "past its deadline", "erased"];

    [Theory]
    [MemberData(nameof(NonListedAnchors))]
    public async Task Related_AnAnchorThatIsNotListed_IsTheSameNotFound_AfterOneReadThatCarriesThePredicate(string state)
    {
        await AddAsync(Imported("Listad", "A AB", Now.AddHours(-2), Kommun, Lan));
        var anchor = state switch
        {
            "unknown" => Guid.NewGuid(),
            "never imported" => await AddAsync(NeverImported("Manuell", AnchorEmployer)),
            "archived" => await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan),
                ad => ad.Archive(_clock).IsSuccess.ShouldBeTrue()),
            "past its deadline" => await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan, Now.AddSeconds(-1))),
            _ => await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan),
                ad => ad.Erase(_clock).IsSuccess.ShouldBeTrue()),
        };
        _commands.Clear();

        var result = await HandleAsync(anchor);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.NotFound);
        var read = _commands.Texts.ShouldHaveSingleItem("a non-listed anchor never reaches the related read");
        read.ShouldContain("external_source");
        read.ShouldContain("expires_at");
    }

    [Fact]
    public async Task Related_TwoReads_EachCarryingThePredicate_TheSecondATierCase_NeitherReadingTheEntity()
    {
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan));
        await AddAsync(Imported("Ett", "A AB", Now.AddHours(-1), Kommun, Lan));
        _commands.Clear();

        await RelatedAsync(anchor);

        _commands.Texts.Count.ShouldBe(2);
        foreach (var text in _commands.Texts)
        {
            text.ShouldContain("external_source");
            text.ShouldContain("expires_at");
            text.ShouldNotContain("raw_payload", Case.Insensitive, "each read projects a narrow row, never the entity");
            text.ShouldNotContain("description", Case.Insensitive);
            text.ShouldContain("organization_number", Case.Insensitive, "the employer is keyed on the org.nr in both reads");
        }

        var candidates = _commands.Texts[1];
        candidates.ShouldContain("CASE");
        candidates.ShouldContain("LIMIT");
    }

    [Fact]
    public async Task Related_AnAnchorWithAKommunButNoLan_RanksARowWithoutALanInTheLastTier()
    {
        // A null anchor län must not become `region_concept_id IS NULL`. The anchor's state is producible:
        // PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_PassesAnAddressBlockWithOneIdThroughUnpaired.
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, null));
        var farNewest = await AddAsync(Imported("Övriga landet, nyast", "A AB", Now.AddMinutes(-1), FarKommun, FarLan));
        var noPlaceOlder = await AddAsync(Imported("Utan ort, äldre", "B AB", Now.AddDays(-9), null, null));

        var items = await RelatedAsync(anchor);

        items.ShouldBe([farNewest, noPlaceOlder]);
    }

    [Fact]
    public async Task Related_AListedAnchorWithoutAGroup_IsAnEmptyList_AfterTheAnchorReadAlone()
    {
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan, group: null));
        await AddAsync(Imported("Också utan grupp", "A AB", Now.AddHours(-1), Kommun, Lan, group: null));
        _commands.Clear();

        (await RelatedAsync(anchor)).ShouldBeEmpty("a null group never matches a null group");
        _commands.Texts.ShouldHaveSingleItem("no candidates read");
    }

    [Fact]
    public async Task Related_AnAnchorWithoutAnOrganizationNumber_IsAnEmptyList_AfterTheAnchorReadAlone()
    {
        var anchor = await AddAsync(Imported("Ankaret", "Okänd arbetsgivare", Now.AddDays(-1), Kommun, Lan, organizationNumber: null));
        await AddAsync(Imported("Med nummer", "A AB", Now.AddHours(-1), Kommun, Lan));
        _commands.Clear();

        (await RelatedAsync(anchor)).ShouldBeEmpty();
        _commands.Texts.ShouldHaveSingleItem("no candidates read");
    }

    [Fact]
    public async Task Related_TheEmployerIsItsOrganizationNumber_AndAnAdWithoutOneTakesNoPart()
    {
        var anchor = await AddAsync(Imported("Ankaret", AnchorEmployer, Now.AddDays(-1), Kommun, Lan));
        await AddAsync(Imported("Ankarets nummer, annan stavning", "Ankarbolaget Sverige AB", Now.AddMinutes(-1), Kommun, Lan,
            organizationNumber: NumberOf(AnchorEmployer)));
        var first = await AddAsync(Imported("Först", "Assistansbolaget AB", Now.AddMinutes(-2), Kommun, Lan,
            organizationNumber: "5561234567"));
        await AddAsync(Imported("Samma nummer, annan stavning", "Assistansbolaget i Sverige AB", Now.AddMinutes(-3), Kommun, Lan,
            organizationNumber: "5561234567"));
        await AddAsync(Imported("Utan nummer", "Okänd arbetsgivare", Now.AddMinutes(-4), Kommun, Lan, organizationNumber: null));
        var last = await AddAsync(Imported("Annan", "Spotify AB", Now.AddMinutes(-5), Kommun, Lan));

        var items = await RelatedAsync(anchor);

        items.ShouldBe([first, last]);
    }

    private async Task<IReadOnlyList<Guid>> RelatedAsync(Guid anchor)
    {
        var result = await HandleAsync(anchor);
        result.IsSuccess.ShouldBeTrue();
        return [.. result.Value.Items.Select(r => r.Id)];
    }

    private async Task<Result<RelatedPublicJobAdsDto>> HandleAsync(Guid anchor)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await new GetRelatedPublicJobAdsQueryHandler(db, Taxonomy(), _clock)
            .Handle(new GetRelatedPublicJobAdsQuery(anchor), Ct);
    }

    // CA2012: an NSubstitute stub on a ValueTask-returning port member is the library's own idiom.
#pragma warning disable CA2012
    /// <summary>The real adapter's contract: one row per asked id, a null label for an id the snapshot does not know.</summary>
    private static ITaxonomyReadModel Taxonomy()
    {
        var taxonomy = Substitute.For<ITaxonomyReadModel>();
        taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                call.Arg<IReadOnlyList<string>>().Select(id => new TaxonomyLabelDto(id, null)).ToList()));
        return taxonomy;
    }
#pragma warning restore CA2012

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

    /// <summary>Marks that the ad's organization number is its employer's own, the one <see cref="NumberOf"/> gives.</summary>
    private const string TheEmployersNumber = "the employer's";

    /// <summary>A legal entity's number per employer name, so one name is one number unless a test says otherwise.</summary>
    private static string NumberOf(string employer)
    {
        var hash = 17;
        foreach (var c in employer)
            hash = unchecked((hash * 31) + c);
        return $"556{(uint)hash % 10_000_000:D7}";
    }

    private JobAd Imported(
        string title, string employer, DateTimeOffset publishedAt, string? kommun, string? lan,
        DateTimeOffset? expiresAt = null, string? group = Group, string? organizationNumber = TheEmployersNumber)
    {
        var externalId = TestIds.ExternalId();
        return JobAd.Import(
            title: title,
            company: Company.Create(employer).Value,
            description: "Vi söker en utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(
                occupationGroup: group, municipality: kommun, region: lan,
                organizationNumber: organizationNumber == TheEmployersNumber ? NumberOf(employer) : organizationNumber),
            declaredContacts: [],
            publishedAt: publishedAt,
            expiresAt: expiresAt,
            clock: _clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
    }

    /// <summary>What <c>POST /api/v1/job-ads</c> stores: any source, and no import provenance.</summary>
    private JobAd NeverImported(string title, string employer) =>
        JobAd.Create(
            title, Company.Create(employer).Value, "Text som någon skrev själv.",
            "https://example.com/jobb/1", JobSource.Platsbanken, Now.AddHours(-1), null, _clock).Value;

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
