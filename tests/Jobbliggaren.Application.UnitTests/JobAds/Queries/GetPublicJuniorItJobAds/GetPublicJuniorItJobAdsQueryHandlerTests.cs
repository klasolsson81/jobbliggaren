using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJuniorItJobAds;

// CA2012: NSubstitute stubs and receive-checks on a ValueTask-returning port member are the library's own idiom (the
// same waiver as GetPublicJobAdQueryHandlerTests).
#pragma warning disable CA2012

/// <summary>
/// One page of the open junior IT list (ADR 0186): the total, the range, the names, the kommun options and the floor's
/// outcome, over the port. The port is a substitute that keeps the real adapter's contract: one count row per kommun id,
/// each at least one, the ads without a kommun as one null row, a page of at most <c>take</c> rows of the chosen kommun,
/// and a floor that holds only over a whole list of at least the floor's number of ads. The taxonomy
/// port keeps the real adapter's: one row per asked id, a null label for an id the snapshot does not know. Membership,
/// order and SQL are measured on Postgres in <c>JuniorItJobAdQueryReadTests</c>.
/// </summary>
public class GetPublicJuniorItJobAdsQueryHandlerTests
{
    private const string Stockholm = "mun_sthlm";
    private const string Solna = "mun_solna";
    private const string Goteborg = "mun_gbg";
    private const string Ystad = "mun_ystad";
    private const string Are = "mun_are";
    private const string Angelholm = "mun_angelholm";
    private const string Ostersund = "mun_ostersund";
    private const string StockholmsLan = "reg_ab";
    private const string VastraGotalandsLan = "reg_o";
    private const int PageSize = PublicJuniorItJobAdLimits.PageSize;

    private static readonly DateTimeOffset Now = FakeDateTimeProvider.Default.UtcNow;

    private static readonly Dictionary<string, string> SnapshotNames = new(StringComparer.Ordinal)
    {
        [Stockholm] = "Stockholm",
        [Solna] = "Solna",
        [Goteborg] = "Göteborg",
        [Ystad] = "Ystad",
        [Are] = "Åre",
        [Angelholm] = "Ängelholm",
        [Ostersund] = "Östersund",
        [StockholmsLan] = "Stockholms län",
        [VastraGotalandsLan] = "Västra Götalands län",
    };

    private readonly IJuniorItJobAdQuery _port = Substitute.For<IJuniorItJobAdQuery>();
    private readonly ITaxonomyReadModel _taxonomy = Substitute.For<ITaxonomyReadModel>();
    private IDateTimeProvider _clock = FakeDateTimeProvider.Default;

    public GetPublicJuniorItJobAdsQueryHandlerTests()
    {
        _taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                call.Arg<IReadOnlyList<string>>()
                    .Select(id => new TaxonomyLabelDto(id, SnapshotNames.GetValueOrDefault(id)))
                    .ToList()));
    }

    private void CountsAre(params (string? Kommun, int Count)[] counts) =>
        _port.CountByMunicipalityAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<JuniorItMunicipalityCount>>(
                counts.Select(c => new JuniorItMunicipalityCount(c.Kommun, c.Count)).ToList()));

    // The real adapter can only answer true over a whole list of at least the floor's number of ads, so every test that
    // stubs true also stubs counts that sum to at least that.
    private void FloorIs(bool holds) =>
        _port.MeetsFloorAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<bool>(holds));

    private void PageIs(IReadOnlyList<JuniorItJobAdRow> rows) =>
        _port.ReadPageAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<JobAdSortBy>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<JuniorItJobAdRow>>(rows));

    private static JuniorItJobAdRow Row(string title, string? kommun, string? lan, int hoursAgo, DateTimeOffset? expiresAt = null) =>
        new(Guid.NewGuid(), title, "Exempelbolaget AB", kommun, lan, Now.AddHours(-hoursAgo), expiresAt);

    private static List<JuniorItJobAdRow> RowsIn(string? kommun, string? lan, int count) =>
        [.. Enumerable.Range(1, count).Select(i => Row($"Junior utvecklare {i}", kommun, lan, i))];

    private ValueTask<PublicJuniorItJobAdsPage> HandleAsync(
        string? kommun = null, JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc, int page = 1) =>
        new GetPublicJuniorItJobAdsQueryHandler(_port, _taxonomy, _clock)
            .Handle(new GetPublicJuniorItJobAdsQuery(kommun, sortBy, page), CancellationToken.None);

    private ValueTask<IReadOnlyList<JuniorItJobAdRow>> ThePageWasNotReadAsync() =>
        _port.DidNotReceive().ReadPageAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<JobAdSortBy>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());

    private ValueTask<IReadOnlyList<TaxonomyLabelDto>> ThePortWasNotAskedAsync() =>
        _taxonomy.DidNotReceive().ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Handle_AnEmptyList_AnswersPageOneAsAMeasuredZero_WithoutReadingAPage()
    {
        CountsAre();

        var page = (await HandleAsync()).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.Rows.Items.ShouldBeEmpty();
        page.Rows.TotalCount.ShouldBe(0);
        page.Rows.Page.ShouldBe(1);
        page.Rows.PageSize.ShouldBe(PageSize);
        page.Rows.TotalPages.ShouldBe(0);
        page.Places.ShouldBeEmpty();
        await ThePageWasNotReadAsync();
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_AnEmptyList_HasNoPageTwo()
    {
        CountsAre();

        (await HandleAsync(page: 2)).ShouldBeOfType<PublicJuniorItJobAdsPage.NoSuchPage>();
        await ThePageWasNotReadAsync();
    }

    [Fact]
    public async Task Handle_TheTotal_IsEveryKommunsCount_AndTheAdsWithoutAKommun()
    {
        CountsAre((Stockholm, 3), (Goteborg, 2), (null, 1));
        PageIs([.. RowsIn(Stockholm, StockholmsLan, 3), .. RowsIn(Goteborg, VastraGotalandsLan, 2), .. RowsIn(null, null, 1)]);

        var page = (await HandleAsync()).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.Rows.TotalCount.ShouldBe(6);
        page.Rows.TotalPages.ShouldBe(1);
        page.Rows.Items.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Handle_AChosenKommun_CountsThatKommunAlone_WhileThePlacesStillOfferEveryKommun()
    {
        CountsAre((Stockholm, 3), (Goteborg, 2), (null, 1));
        PageIs(RowsIn(Goteborg, VastraGotalandsLan, 2));

        var page = (await HandleAsync(Goteborg)).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.Rows.TotalCount.ShouldBe(2);
        page.Places.ShouldBe([
            new PublicJuniorItPlaceDto(Goteborg, "Göteborg", 2),
            new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 3),
        ]);
        await _port.Received(1).ReadPageAsync(Now, Goteborg, JobAdSortBy.PublishedAtDesc, 0, PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AKommunWithNoListedAd_IsAnEmptyFirstPage_AndHasNoSecond()
    {
        CountsAre((Stockholm, 3));

        var one = (await HandleAsync("mun_no_ads")).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();
        var two = await HandleAsync("mun_no_ads", page: 2);

        one.Rows.Items.ShouldBeEmpty();
        one.Rows.TotalCount.ShouldBe(0);
        one.Places.ShouldBe([new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 3)]);
        two.ShouldBeOfType<PublicJuniorItJobAdsPage.NoSuchPage>();
        await ThePageWasNotReadAsync();
    }

    [Fact]
    public async Task Handle_OnePageAndOneAd_HasAPageTwoOfThatAd_AndNoPageThree()
    {
        CountsAre((Stockholm, PageSize + 1));
        var last = Row("Sist", Stockholm, StockholmsLan, 100);
        PageIs([last]);

        var two = (await HandleAsync(page: 2)).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();
        var three = await HandleAsync(page: 3);

        two.Rows.Items.Select(r => r.Id).ShouldBe([last.Id]);
        two.Rows.Page.ShouldBe(2);
        two.Rows.TotalPages.ShouldBe(2);
        three.ShouldBeOfType<PublicJuniorItJobAdsPage.NoSuchPage>();
        await _port.Received(1).ReadPageAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<JobAdSortBy>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReadsThePage_InTheChosenKommunAndOrder_AtThePagesSlice()
    {
        CountsAre((Stockholm, 2 * PageSize + 5), (Goteborg, 1));
        PageIs(RowsIn(Stockholm, StockholmsLan, 5));

        await HandleAsync(Stockholm, JobAdSortBy.ExpiresAtAsc, 3);

        await _port.Received(1).ReadPageAsync(
            Now, Stockholm, JobAdSortBy.ExpiresAtAsc, 2 * PageSize, PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReadsTheClockOnce_SoTheCountThePageAndTheFloorDescribeOneInstant()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now, Now.AddMinutes(1));
        _clock = clock;
        CountsAre((Stockholm, 1));
        PageIs(RowsIn(Stockholm, StockholmsLan, 1));

        await HandleAsync();

        _ = clock.Received(1).UtcNow;
        await _port.Received(1).CountByMunicipalityAsync(Now, Arg.Any<CancellationToken>());
        await _port.Received(1).ReadPageAsync(
            Now, null, JobAdSortBy.PublishedAtDesc, 0, PageSize, Arg.Any<CancellationToken>());
        await _port.Received(1).MeetsFloorAsync(Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_AnswersTheFloorsOutcome_AsThePortGivesIt(bool holds)
    {
        CountsAre((Stockholm, 60), (Goteborg, 50));
        PageIs(RowsIn(Stockholm, StockholmsLan, PageSize));
        FloorIs(holds);

        var page = (await HandleAsync()).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.FloorHolds.ShouldBe(holds);
    }

    [Fact]
    public async Task Handle_TheFloor_IsTheWholeListsOutcome_ForAKommunTheDeadlineOrderAndALaterPage()
    {
        CountsAre((Stockholm, 60), (Goteborg, 50));
        PageIs(RowsIn(Goteborg, VastraGotalandsLan, PageSize));
        FloorIs(true);

        var page = (await HandleAsync(Goteborg, JobAdSortBy.ExpiresAtAsc, 2))
            .ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.Rows.TotalCount.ShouldBe(50);
        page.FloorHolds.ShouldBeTrue();
        await _port.Received(1).MeetsFloorAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_APagePastTheEnd_NeverReadsTheFloor()
    {
        CountsAre((Stockholm, 3));

        (await HandleAsync(page: 2)).ShouldBeOfType<PublicJuniorItJobAdsPage.NoSuchPage>();

        await _port.DidNotReceive().MeetsFloorAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AFloorReadThatThrows_Throws_AndAnswersNoOutcome()
    {
        CountsAre((Stockholm, 3));
        PageIs(RowsIn(Stockholm, StockholmsLan, 3));
        _port.MeetsFloorAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<bool>>(_ => throw new DatabaseFailure());

        await Should.ThrowAsync<DatabaseFailure>(async () => await HandleAsync());
    }

    // The adapter's database errors are DbExceptions (NpgsqlException is one).
    private sealed class DatabaseFailure() : System.Data.Common.DbException("The database did not answer.");

    [Fact]
    public async Task Handle_ShapesEachRow_AsItsSevenValues_InThePortsOrder_WithKommunAndLanNamed()
    {
        var named = Row("Junior utvecklare", Stockholm, StockholmsLan, 1, Now.AddDays(10));
        var drifted = Row("Trainee", "mun_drifted", VastraGotalandsLan, 2);
        var unplaced = Row("Praktikant", null, null, 3);
        CountsAre((Stockholm, 1), ("mun_drifted", 1), (null, 1));
        PageIs([named, drifted, unplaced]);

        var page = (await HandleAsync()).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.Rows.Items.ShouldBe([
            new PublicJuniorItJobAdDto(
                named.Id, "Junior utvecklare", "Exempelbolaget AB", "Stockholm", "Stockholms län", named.PublishedAt, Now.AddDays(10)),
            new PublicJuniorItJobAdDto(
                drifted.Id, "Trainee", "Exempelbolaget AB", null, "Västra Götalands län", drifted.PublishedAt, null),
            new PublicJuniorItJobAdDto(
                unplaced.Id, "Praktikant", "Exempelbolaget AB", null, null, unplaced.PublishedAt, null),
        ]);
    }

    [Fact]
    public async Task Handle_ThePlaces_AreTheNamedKommuner_InSwedishAlphabeticalOrder()
    {
        // A kommun the taxonomy cannot name is no option, but its ads still count; so do the ads without a kommun.
        CountsAre((Stockholm, 2), (Goteborg, 5), (Solna, 2), (null, 4), ("mun_drifted", 7));
        PageIs([
            .. RowsIn(Stockholm, StockholmsLan, 2), .. RowsIn(Goteborg, VastraGotalandsLan, 5),
            .. RowsIn(Solna, StockholmsLan, 2), .. RowsIn(null, null, 4), .. RowsIn("mun_drifted", null, 7),
        ]);

        var page = (await HandleAsync()).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.Places.ShouldBe([
            new PublicJuniorItPlaceDto(Goteborg, "Göteborg", 5),
            new PublicJuniorItPlaceDto(Solna, "Solna", 2),
            new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 2),
        ]);
        page.Rows.TotalCount.ShouldBe(20);
    }

    [Fact]
    public async Task Handle_ThePlaces_PutÅÄÖAfterZ_InThatOrder()
    {
        CountsAre((Ostersund, 1), (Angelholm, 1), (Are, 1), (Ystad, 1));
        PageIs([.. RowsIn(Ostersund, null, 1), .. RowsIn(Angelholm, null, 1), .. RowsIn(Are, null, 1), .. RowsIn(Ystad, null, 1)]);

        var page = (await HandleAsync()).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.Places.Select(p => p.Name).ShouldBe(["Ystad", "Åre", "Ängelholm", "Östersund"]);
    }

    [Fact]
    public async Task Handle_AsksTheTaxonomyOnce_WithEachPlaceIdOnce_AndNeverANullId()
    {
        CountsAre((Stockholm, 2), (Goteborg, 1), (null, 1));
        PageIs([
            Row("Ett", Stockholm, StockholmsLan, 1),
            Row("Två", Stockholm, StockholmsLan, 2),
            Row("Tre", Goteborg, VastraGotalandsLan, 3),
            Row("Fyra", null, VastraGotalandsLan, 4),
        ]);

        await HandleAsync();

        string[] asked = [Goteborg, Stockholm, StockholmsLan, VastraGotalandsLan];
        await _taxonomy.Received(1).ResolveLabelsAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.Order(StringComparer.Ordinal).SequenceEqual(asked)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AListWhoseAdsHaveNoPlace_NeverAsksTheTaxonomy()
    {
        // An absent address block gives both ids null (PlatsbankenJobSource.MapFacets,
        // PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_YieldsEmptyFacets_WhenTheSourceCarriesNone).
        CountsAre((null, 1));
        PageIs(RowsIn(null, null, 1));

        var page = (await HandleAsync()).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();

        page.Rows.TotalCount.ShouldBe(1);
        page.Places.ShouldBeEmpty();
        page.Rows.Items.Single().MunicipalityName.ShouldBeNull();
        await ThePortWasNotAskedAsync();
    }
}
#pragma warning restore CA2012
