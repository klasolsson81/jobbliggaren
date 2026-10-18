using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJobAdCatalogue;

// CA2012: NSubstitute stubs and receive-checks on a ValueTask-returning port member are the library's own idiom (the
// same waiver as GetPublicJobAdQueryHandlerTests).
#pragma warning disable CA2012

/// <summary>
/// One catalogue page (ADR 0176): membership, order and count from the snapshot, content read now through the
/// publication predicate. Premises come from production: ads through <c>JobAd.Import</c>, lifecycle changes through
/// <c>Erase</c>, and a snapshot built from the same (id, publication date) rows the sampler's read projects. The
/// taxonomy port is a substitute that keeps the real adapter's contract (one row per asked id, a null label for an id
/// the snapshot does not know, never a throw). The exact SQL set and its translation are measured on Postgres in
/// <c>PublicJobAdCatalogueReadTests</c>.
/// </summary>
public class GetPublicJobAdCatalogueQueryHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly DateTimeOffset Now = Clock.UtcNow;

    private static readonly Dictionary<string, string> SnapshotNames = new(StringComparer.Ordinal)
    {
        ["mun_sthlm"] = "Stockholm",
        ["reg_ab"] = "Stockholms län",
        ["reg_o"] = "Västra Götalands län",
    };

    private static readonly string[] ThreePlaceIds = ["mun_sthlm", "reg_ab", "reg_o"];

    private readonly ITaxonomyReadModel _taxonomy = Substitute.For<ITaxonomyReadModel>();
    private readonly IListedJobAdSnapshotReader _reader = Substitute.For<IListedJobAdSnapshotReader>();

    public GetPublicJobAdCatalogueQueryHandlerTests()
    {
        _taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                call.Arg<IReadOnlyList<string>>()
                    .Select(id => new TaxonomyLabelDto(id, SnapshotNames.GetValueOrDefault(id)))
                    .ToList()));
    }

    private static JobAd Import(string title, DateTimeOffset publishedAt, DateTimeOffset? expiresAt, JobAdFacets facets)
    {
        var externalId = TestIds.ExternalId();
        return JobAd.Import(
            title: title,
            company: Company.Create("Klarna").Value,
            description: "Vi söker en utvecklare.",
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: facets,
            declaredContacts: [],
            publishedAt: publishedAt,
            expiresAt: expiresAt,
            clock: Clock,
            extractTerms: TestKeywordExtraction.None).Value;
    }

    private static async Task<JobAd[]> SeedAsync(AppDbContext db, params JobAd[] ads)
    {
        db.JobAds.AddRange(ads);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        return ads;
    }

    /// <summary>The snapshot the sampler would publish for these ads, read at <paramref name="asOf"/>.</summary>
    private void SnapshotOf(DateTimeOffset asOf, params JobAd[] ads) =>
        _reader.Current.Returns(ListedJobAdSnapshot.Create(
            asOf,
            ads.Select(a => new ListedJobAdRow(a.Id.Value, a.PublishedAt))
                .OrderBy(r => r.Id.ToString("D"), StringComparer.Ordinal)
                .ToList()));

    /// <summary>A fresh snapshot of <paramref name="count"/> ids with no rows behind them, for the range rules alone.</summary>
    private void SnapshotOfCount(int count, DateTimeOffset asOf) =>
        _reader.Current.Returns(ListedJobAdSnapshot.Create(
            asOf,
            Enumerable.Range(0, count).Select(i => new ListedJobAdRow(Guid.NewGuid(), Now.AddMinutes(-i))).ToList()));

    private ValueTask<PublicJobAdCataloguePage> HandleAsync(AppDbContext db, int page) =>
        new GetPublicJobAdCatalogueQueryHandler(_reader, db, _taxonomy, Clock)
            .Handle(new GetPublicJobAdCatalogueQuery(page), CancellationToken.None);

    /// <summary>A context whose every read throws: proof that an answer was given without touching the database.</summary>
    private static async Task<AppDbContext> UnreadableDbAsync()
    {
        var db = TestAppDbContextFactory.Create();
        await db.DisposeAsync();
        return db;
    }

    private ValueTask<IReadOnlyList<TaxonomyLabelDto>> ThePortWasNotAskedAsync() =>
        _taxonomy.DidNotReceive().ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Handle_BeforeAnyReadHasSucceeded_IsUnavailable()
    {
        _reader.Current.Returns((ListedJobAdSnapshot?)null);

        (await HandleAsync(await UnreadableDbAsync(), 1)).ShouldBeOfType<PublicJobAdCataloguePage.Unavailable>();
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_AStaleSnapshot_IsUnavailable_EvenForAPagePastItsEnd()
    {
        // Availability is judged before the range, so an outage never reads as a page that does not exist.
        SnapshotOfCount(0, Now - ListedJobAdSnapshotLimits.MaxAge - TimeSpan.FromTicks(1));

        (await HandleAsync(await UnreadableDbAsync(), 5)).ShouldBeOfType<PublicJobAdCataloguePage.Unavailable>();
    }

    [Fact]
    public async Task Handle_ASnapshotStampedAheadOfTheClockBeyondTheTolerance_IsUnavailable()
    {
        SnapshotOfCount(3, Now + ListedJobAdSnapshotLimits.FutureTolerance + TimeSpan.FromTicks(1));

        (await HandleAsync(await UnreadableDbAsync(), 1)).ShouldBeOfType<PublicJobAdCataloguePage.Unavailable>();
    }

    [Fact]
    public async Task Handle_ExactlyTwoPagesOfAds_HasAPageTwo_AndNoPageThree()
    {
        SnapshotOfCount(2 * PublicJobAdCatalogueLimits.PageSize, Now.AddMinutes(-1));
        await using var db = TestAppDbContextFactory.Create();

        var two = (await HandleAsync(db, 2)).ShouldBeOfType<PublicJobAdCataloguePage.Available>();
        two.Rows.TotalCount.ShouldBe(2 * PublicJobAdCatalogueLimits.PageSize);
        two.Rows.TotalPages.ShouldBe(2);
        (await HandleAsync(db, 3)).ShouldBeOfType<PublicJobAdCataloguePage.NoSuchPage>();
    }

    [Fact]
    public async Task Handle_AnEmptyCorpus_AnswersPageOneAsAMeasuredZero_WithoutReadingAnything()
    {
        SnapshotOfCount(0, Now.AddMinutes(-1));

        var page = (await HandleAsync(await UnreadableDbAsync(), 1)).ShouldBeOfType<PublicJobAdCataloguePage.Available>();

        page.Rows.Items.ShouldBeEmpty();
        page.Rows.TotalCount.ShouldBe(0);
        page.Rows.Page.ShouldBe(1);
        page.Rows.PageSize.ShouldBe(PublicJobAdCatalogueLimits.PageSize);
        page.Rows.TotalPages.ShouldBe(0);
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_AnEmptyCorpus_HasNoPageTwo()
    {
        SnapshotOfCount(0, Now.AddMinutes(-1));

        (await HandleAsync(await UnreadableDbAsync(), 2)).ShouldBeOfType<PublicJobAdCataloguePage.NoSuchPage>();
    }

    [Fact]
    public async Task Handle_AListedPage_ReturnsTheSixValuesPerAd_NewestFirst_WithPlaceNames()
    {
        await using var db = TestAppDbContextFactory.Create();
        var ads = await SeedAsync(db,
            Import("Äldst", Now.AddDays(-3), null, TestFacets.From(municipality: "mun_sthlm", region: "reg_ab")),
            Import("Nyast", Now.AddDays(-1), Now.AddDays(10), TestFacets.From(region: "reg_o")),
            Import("Mitten", Now.AddDays(-2), null, TestFacets.From(municipality: "mun_drifted", region: "reg_ab")));
        SnapshotOf(Now.AddMinutes(-1), ads);

        var page = (await HandleAsync(db, 1)).ShouldBeOfType<PublicJobAdCataloguePage.Available>();

        page.Rows.Items.ShouldBe(
        [
            new PublicJobAdSummaryDto(ads[1].Id.Value, "Nyast", "Klarna", null, "Västra Götalands län", Now.AddDays(-1)),
            new PublicJobAdSummaryDto(ads[2].Id.Value, "Mitten", "Klarna", null, "Stockholms län", Now.AddDays(-2)),
            new PublicJobAdSummaryDto(ads[0].Id.Value, "Äldst", "Klarna", "Stockholm", "Stockholms län", Now.AddDays(-3)),
        ]);
        page.Rows.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_AsksThePortOnce_WithEachPlaceIdOnce_AndNeverANullId()
    {
        await using var db = TestAppDbContextFactory.Create();
        var ads = await SeedAsync(db,
            Import("Ett", Now.AddDays(-1), null, TestFacets.From(municipality: "mun_sthlm", region: "reg_ab")),
            Import("Två", Now.AddDays(-2), null, TestFacets.From(municipality: "mun_sthlm", region: "reg_ab")),
            Import("Tre", Now.AddDays(-3), null, TestFacets.From(region: "reg_o")));
        SnapshotOf(Now.AddMinutes(-1), ads);

        await HandleAsync(db, 1);

        await _taxonomy.Received(1).ResolveLabelsAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.Order(StringComparer.Ordinal)
                .SequenceEqual(ThreePlaceIds)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_APageWhoseAdsHaveNoPlace_DoesNotAskThePort()
    {
        await using var db = TestAppDbContextFactory.Create();
        var ads = await SeedAsync(db, Import("Ingen ort", Now.AddDays(-1), null, TestFacets.None));
        SnapshotOf(Now.AddMinutes(-1), ads);

        var page = (await HandleAsync(db, 1)).ShouldBeOfType<PublicJobAdCataloguePage.Available>();

        page.Rows.Items.Single().MunicipalityName.ShouldBeNull();
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_AnAdErasedAfterTheSnapshot_IsAbsentAtOnce_AndNothingMovesUpToFillItsPlace()
    {
        await using var db = TestAppDbContextFactory.Create();
        var ads = await SeedAsync(db,
            Import("Först", Now.AddDays(-1), null, TestFacets.None),
            Import("Raderas", Now.AddDays(-2), null, TestFacets.None),
            Import("Sist", Now.AddDays(-3), null, TestFacets.None));
        SnapshotOf(Now.AddMinutes(-1), ads);

        var erased = await db.JobAds.FindAsync([ads[1].Id], CancellationToken.None);
        erased!.Erase(Clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var page = (await HandleAsync(db, 1)).ShouldBeOfType<PublicJobAdCataloguePage.Available>();

        page.Rows.Items.Select(r => r.Title).ShouldBe(["Först", "Sist"]);
        page.Rows.TotalCount.ShouldBe(3, "the count is the snapshot's, true as of its read");
    }

    [Fact]
    public async Task Handle_AnAdWhoseDeadlinePassedAfterTheSnapshot_IsAbsent()
    {
        // The snapshot was read a minute before the deadline; the clock has since passed it.
        await using var db = TestAppDbContextFactory.Create();
        var ads = await SeedAsync(db,
            Import("Öppen", Now.AddDays(-1), null, TestFacets.None),
            Import("Utgången", Now.AddDays(-2), Now.AddSeconds(-30), TestFacets.None));
        SnapshotOf(Now.AddMinutes(-1), ads);

        var page = (await HandleAsync(db, 1)).ShouldBeOfType<PublicJobAdCataloguePage.Available>();

        page.Rows.Items.Select(r => r.Title).ShouldBe(["Öppen"]);
    }

    [Fact]
    public async Task Handle_ReadsTheSnapshotOnce_SoTheAgeTheOrderAndTheCountComeFromTheSameRead()
    {
        SnapshotOfCount(3, Now.AddMinutes(-1));
        await using var db = TestAppDbContextFactory.Create();

        await HandleAsync(db, 1);

        _ = _reader.Received(1).Current;
    }
}
