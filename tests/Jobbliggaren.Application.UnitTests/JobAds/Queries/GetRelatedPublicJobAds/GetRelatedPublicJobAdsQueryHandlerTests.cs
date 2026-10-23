using Jobbliggaren.Application.JobAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetRelatedPublicJobAds;

// CA2012: NSubstitute stubs and receive-checks on a ValueTask-returning port member are the library's own idiom (the
// same waiver as GetPublicJobAdQueryHandlerTests).
#pragma warning disable CA2012

/// <summary>
/// The related section's selection rule (ADR 0182). Premises come from production: ads through <c>JobAd.Import</c>, a
/// never-imported one through <c>JobAd.Create</c> (what <c>POST /api/v1/job-ads</c> calls), lifecycle changes through
/// <c>Archive</c> and <c>Erase</c>, and a passed deadline through the clock. The taxonomy port keeps the real adapter's
/// contract (one row per asked id, a null label for an id the snapshot does not know, never a throw).
/// </summary>
/// <remarks>
/// This runs on the InMemory provider, where C# null semantics and <c>Guid</c> order apply, not Postgres's. So every ad
/// here has its own publication instant and no assertion rests on an id tie; ties, NULL ordering and the SQL itself are
/// measured on Postgres in <c>RelatedPublicJobAdsReadTests</c>.
/// </remarks>
public class GetRelatedPublicJobAdsQueryHandlerTests
{
    private const string Group = "grp_dev";
    private const string OtherGroup = "grp_ssk";
    private const string Kommun = "mun_sthlm";
    private const string OtherKommunSameLan = "mun_solna";
    private const string FarKommun = "mun_gbg";
    private const string Lan = "reg_ab";
    private const string FarLan = "reg_o";
    private const string AnchorEmployer = "Ankarbolaget AB";

    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly DateTimeOffset Now = Clock.UtcNow;

    private static readonly Dictionary<string, string> SnapshotNames = new(StringComparer.Ordinal)
    {
        [Kommun] = "Stockholm",
        [OtherKommunSameLan] = "Solna",
        [FarKommun] = "Göteborg",
        [Lan] = "Stockholms län",
        [FarLan] = "Västra Götalands län",
    };

    private readonly ITaxonomyReadModel _taxonomy = Substitute.For<ITaxonomyReadModel>();

    public GetRelatedPublicJobAdsQueryHandlerTests()
    {
        _taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                call.Arg<IReadOnlyList<string>>()
                    .Select(id => new TaxonomyLabelDto(id, SnapshotNames.GetValueOrDefault(id)))
                    .ToList()));
    }

    private static JobAd Import(
        string title, string employer, int hoursAgo,
        string? group = Group, string? kommun = null, string? lan = null, DateTimeOffset? expiresAt = null)
    {
        var externalId = TestIds.ExternalId();
        return JobAd.Import(
            title: title,
            company: Company.Create(employer).Value,
            description: "Vi söker en utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(occupationGroup: group, municipality: kommun, region: lan),
            declaredContacts: [],
            publishedAt: Now.AddHours(-hoursAgo),
            expiresAt: expiresAt,
            clock: Clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
    }

    private static JobAd Anchor(string? group = Group, string? kommun = Kommun, string? lan = Lan) =>
        Import("Ankaret", AnchorEmployer, 1, group, kommun, lan);

    /// <summary>Saves the ads, then applies an optional lifecycle change to each saved row, as production does.</summary>
    private static async Task SeedAsync(AppDbContext db, params JobAd[] ads)
    {
        db.JobAds.AddRange(ads);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
    }

    private static async Task ThenAsync(AppDbContext db, JobAd ad, Func<JobAd, bool> change)
    {
        var saved = await db.JobAds.FindAsync([ad.Id], CancellationToken.None);
        change(saved!).ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
    }

    private ValueTask<Domain.Common.Result<RelatedPublicJobAdsDto>> HandleAsync(AppDbContext db, JobAd anchor) =>
        HandleAsync(db, anchor.Id.Value);

    private ValueTask<Domain.Common.Result<RelatedPublicJobAdsDto>> HandleAsync(AppDbContext db, Guid id) =>
        new GetRelatedPublicJobAdsQueryHandler(db, _taxonomy, Clock)
            .Handle(new GetRelatedPublicJobAdsQuery(id), CancellationToken.None);

    private async Task<IReadOnlyList<PublicJobAdSummaryDto>> ItemsAsync(AppDbContext db, JobAd anchor)
    {
        var result = await HandleAsync(db, anchor);
        result.IsSuccess.ShouldBeTrue();
        return result.Value.Items;
    }

    private ValueTask<IReadOnlyList<TaxonomyLabelDto>> ThePortWasNotAskedAsync() =>
        _taxonomy.DidNotReceive().ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Handle_ARelatedAd_IsTheSixMemberRowWithItsKommunAndLanNamed()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var related = Import("Systemutvecklare", "Spotify AB", 5, kommun: Kommun, lan: Lan);
        await SeedAsync(db, anchor, related);

        var items = await ItemsAsync(db, anchor);

        items.ShouldBe([new PublicJobAdSummaryDto(
            related.Id.Value, "Systemutvecklare", "Spotify AB", "Stockholm", "Stockholms län", related.PublishedAt)]);
    }

    [Fact]
    public async Task Handle_TheAnchorItselfAndEveryAdOfItsEmployer_AreNeverRelated()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var sameEmployer = Import("Samma arbetsgivare", AnchorEmployer, 2, kommun: Kommun, lan: Lan);
        var other = Import("Annan arbetsgivare", "Spotify AB", 3, kommun: FarKommun, lan: FarLan);
        await SeedAsync(db, anchor, sameEmployer, other);

        var items = await ItemsAsync(db, anchor);

        items.Select(r => r.Id).ShouldBe([other.Id.Value]);
    }

    [Fact]
    public async Task Handle_TakesOneRowPerEmployer_TheFirstInTheReadsOrder()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var newer = Import("Nyare", "Spotify AB", 2, kommun: FarKommun, lan: FarLan);
        var older = Import("Äldre", "Spotify AB", 3, kommun: FarKommun, lan: FarLan);
        var localOlder = Import("Lokal men äldst", "Spotify AB", 9, kommun: Kommun, lan: Lan);
        var another = Import("Annan", "Klarna AB", 4, kommun: FarKommun, lan: FarLan);
        await SeedAsync(db, anchor, newer, older, localOlder, another);

        var items = await ItemsAsync(db, anchor);

        items.Select(r => r.Id).ShouldBe([localOlder.Id.Value, another.Id.Value],
            "the employer's first row in the read's order is its kommun-tier row, so its newer far rows are skipped");
    }

    [Fact]
    public async Task Handle_ShowsAtMostFiveRows()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var ads = Enumerable.Range(1, 7)
            .Select(i => Import($"Annons {i}", $"Arbetsgivare {i} AB", i + 1, kommun: FarKommun, lan: FarLan))
            .ToArray();
        await SeedAsync(db, [anchor, .. ads]);

        var items = await ItemsAsync(db, anchor);

        items.Count.ShouldBe(RelatedPublicJobAdLimits.MaxRows);
        items.Select(r => r.Id).ShouldBe(ads.Take(5).Select(a => a.Id.Value));
    }

    [Fact]
    public async Task Handle_PutsTheSameKommunFirst_ThenTheSameLan_ThenTheRest_NewestFirstWithinEachTier()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var restNewest = Import("Övriga landet, nyast", "A AB", 2, kommun: FarKommun, lan: FarLan);
        var lanNewer = Import("Samma län, nyare", "B AB", 3, kommun: OtherKommunSameLan, lan: Lan);
        var kommunNewer = Import("Samma kommun, nyare", "C AB", 4, kommun: Kommun, lan: Lan);
        var lanOlder = Import("Samma län, äldre", "D AB", 5, kommun: OtherKommunSameLan, lan: Lan);
        var kommunOlder = Import("Samma kommun, äldre", "E AB", 6, kommun: Kommun, lan: Lan);
        await SeedAsync(db, anchor, restNewest, lanNewer, kommunNewer, lanOlder, kommunOlder);

        var items = await ItemsAsync(db, anchor);

        items.Select(r => r.Title).ShouldBe([
            "Samma kommun, nyare", "Samma kommun, äldre", "Samma län, nyare", "Samma län, äldre", "Övriga landet, nyast",
        ]);
    }

    [Fact]
    public async Task Handle_AnAnchorWithALanButNoKommun_SkipsTheKommunTier_SoARowWithoutAKommunNeverRanksFirst()
    {
        // The actor: PlatsbankenJobSource.MapFacets reads the kommun and län ids independently, so an address block
        // with only a län id is producible though unmeasured on the box (#839). The pin that MapFacets admits it is
        // PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_PassesAnAddressBlockWithOneIdThroughUnpaired.
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor(kommun: null, lan: Lan);
        var noPlace = Import("Utan ort, nyast", "A AB", 2, kommun: null, lan: null);
        var sameLan = Import("Samma län, äldre", "B AB", 3, kommun: OtherKommunSameLan, lan: Lan);
        await SeedAsync(db, anchor, noPlace, sameLan);

        var items = await ItemsAsync(db, anchor);

        items.Select(r => r.Title).ShouldBe(["Samma län, äldre", "Utan ort, nyast"],
            "a null anchor kommun must never match a null kommun: the row without a place belongs in the last tier");
    }

    [Fact]
    public async Task Handle_AnAnchorWithNoPlace_ListsTheGroupNewestFirst_SoNoTierMatchesANull()
    {
        // An absent address block gives both ids null (MapFacets, PlatsbankenJobSourceFacetMappingTests
        // .RefetchByExternalIdAsync_YieldsEmptyFacets_WhenTheSourceCarriesNone).
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor(kommun: null, lan: null);
        var placed = Import("Med ort, nyare", "A AB", 2, kommun: Kommun, lan: Lan);
        var noPlace = Import("Utan ort, äldre", "B AB", 3, kommun: null, lan: null);
        await SeedAsync(db, anchor, placed, noPlace);

        var items = await ItemsAsync(db, anchor);

        items.Select(r => r.Title).ShouldBe(["Med ort, nyare", "Utan ort, äldre"]);
    }

    [Fact]
    public async Task Handle_NeverCrossesToAnotherGroup_EvenInTheSameKommun()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var otherGroup = Import("Sjuksköterska", "Region AB", 2, group: OtherGroup, kommun: Kommun, lan: Lan);
        var noGroup = Import("Utan grupp", "Okänd AB", 3, group: null, kommun: Kommun, lan: Lan);
        await SeedAsync(db, anchor, otherGroup, noGroup);

        var items = await ItemsAsync(db, anchor);

        items.ShouldBeEmpty("there is no fallback across groups or by place alone");
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_AListedAnchorWithoutAGroup_AnswersAnEmptyList_AndNeverAsksThePort()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor(group: null);
        var noGroup = Import("Också utan grupp", "Spotify AB", 2, group: null, kommun: Kommun, lan: Lan);
        await SeedAsync(db, anchor, noGroup);

        var items = await ItemsAsync(db, anchor);

        items.ShouldBeEmpty("a null group never matches a null group");
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_LeavesOutEveryAdThatIsNotListed()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var listed = Import("Listad", "A AB", 6, kommun: FarKommun, lan: FarLan);
        var archived = Import("Arkiverad", "B AB", 2, kommun: Kommun, lan: Lan);
        var erased = Import("Raderad", "C AB", 3, kommun: Kommun, lan: Lan);
        // The clock, not a writer, makes this state: the ad is still Active until ExpireJobAdsJob runs.
        var pastDeadline = Import("Utgången", "D AB", 4, kommun: Kommun, lan: Lan, expiresAt: Now.AddSeconds(-1));
        // What POST /api/v1/job-ads stores: Source = Platsbanken with no import provenance.
        var manual = JobAd.Create(
            "Manuell", Company.Create("E AB").Value, "Text som någon skrev själv.",
            "https://example.com/jobb/1", JobSource.Platsbanken, Now.AddHours(-5), null, Clock).Value;
        await SeedAsync(db, anchor, listed, archived, erased, pastDeadline, manual);
        await ThenAsync(db, archived, ad => ad.Archive(Clock).IsSuccess);
        await ThenAsync(db, erased, ad => ad.Erase(Clock).IsSuccess);

        var items = await ItemsAsync(db, anchor);

        items.Select(r => r.Id).ShouldBe([listed.Id.Value]);
    }

    public static TheoryData<string> NonListedAnchors => ["unknown", "never imported", "archived", "past its deadline", "erased"];

    [Theory]
    [MemberData(nameof(NonListedAnchors))]
    public async Task Handle_AnAnchorThatIsNotListed_AnswersTheSameNotFound_AndNeverAsksThePort(string state)
    {
        await using var db = TestAppDbContextFactory.Create();
        var related = Import("Listad", "Spotify AB", 5, kommun: Kommun, lan: Lan);
        Guid anchorId;
        switch (state)
        {
            case "unknown":
                await SeedAsync(db, related);
                anchorId = Guid.NewGuid();
                break;
            case "never imported":
                var manual = JobAd.Create(
                    "Manuell", Company.Create(AnchorEmployer).Value, "Text som någon skrev själv.",
                    "https://example.com/jobb/1", JobSource.Platsbanken, Now.AddHours(-1), null, Clock).Value;
                await SeedAsync(db, manual, related);
                anchorId = manual.Id.Value;
                break;
            case "past its deadline":
                var expired = Import("Ankaret", AnchorEmployer, 10, kommun: Kommun, lan: Lan, expiresAt: Now.AddSeconds(-1));
                await SeedAsync(db, expired, related);
                anchorId = expired.Id.Value;
                break;
            default:
                var anchor = Anchor();
                await SeedAsync(db, anchor, related);
                await ThenAsync(db, anchor, ad => state == "archived" ? ad.Archive(Clock).IsSuccess : ad.Erase(Clock).IsSuccess);
                anchorId = anchor.Id.Value;
                break;
        }

        var result = await HandleAsync(db, anchorId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.NotFound,
            "every anchor that is not listed answers alike, so the related read tells no state of a non-listed ad apart");
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_AsksThePortOnce_ForTheShownRowsPlacesAlone()
    {
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var ads = new[]
        {
            Import("Ett", "A AB", 2, kommun: Kommun, lan: Lan),
            Import("Två", "B AB", 3, kommun: OtherKommunSameLan, lan: Lan),
            Import("Tre", "C AB", 4, kommun: "mun_drifted", lan: Lan),
            Import("Fyra", "D AB", 5),
            Import("Fem", "E AB", 6, kommun: FarKommun, lan: FarLan),
            Import("Sex, under taket", "F AB", 7, kommun: "mun_never_asked", lan: "reg_never_asked"),
        };
        await SeedAsync(db, [anchor, .. ads]);

        var items = await ItemsAsync(db, anchor);

        items.Select(r => (r.MunicipalityName, r.RegionName)).ShouldBe([
            ("Stockholm", "Stockholms län"),
            ("Solna", "Stockholms län"),
            (null, "Stockholms län"),
            (null, null),
            ("Göteborg", "Västra Götalands län"),
        ]);
        string[] asked = [Kommun, Lan, OtherKommunSameLan, "mun_drifted", FarKommun, FarLan];
        await _taxonomy.Received(1).ResolveLabelsAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(asked)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AWindowHeldByFourEmployers_ShowsFour_AndNeverReadsPastTheWindow()
    {
        // The declared underfill of CandidateWindow: when at most four employers besides the anchor's hold every row
        // of the window, the section is shorter, and every row it shows is still true.
        await using var db = TestAppDbContextFactory.Create();
        var anchor = Anchor();
        var window = Enumerable.Range(0, RelatedPublicJobAdLimits.CandidateWindow)
            .Select(i => Import($"Fönster {i}", $"Arbetsgivare {i % 4} AB", i + 2, kommun: FarKommun, lan: FarLan))
            .ToArray();
        var belowTheWindow = Import("Under fönstret", "Femte AB", RelatedPublicJobAdLimits.CandidateWindow + 10,
            kommun: FarKommun, lan: FarLan);
        await SeedAsync(db, [anchor, .. window, belowTheWindow]);

        var items = await ItemsAsync(db, anchor);

        items.Select(r => r.CompanyName).ShouldBe(["Arbetsgivare 0 AB", "Arbetsgivare 1 AB", "Arbetsgivare 2 AB", "Arbetsgivare 3 AB"]);
    }
}
#pragma warning restore CA2012
