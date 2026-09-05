using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCriterionAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1656 (b) — <see cref="BrowseCriterionAdsQueryHandler"/>'s "bara matchande" arm. Its own class
/// (house pattern: one concern per class) because the SUT path is different — the unfiltered arm
/// pages the PORT's page, this one pages the MATCHING SET.
///
/// <para>
/// The distinction is the whole arm and it is invisible in the happy case: filter a loaded page of
/// 20 and the first page looks identical to a filtered set's first page. It diverges on the TOTAL
/// and on page 2, which is exactly where a user clicking "9 matchande" would find something other
/// than nine.
/// </para>
///
/// <para>
/// The two INERT arms are pinned as DELIVERY, not as emptiness. A caller who stated no occupation
/// and a watch too broad to grade both get the unfiltered list: an empty page would say "nothing
/// matches you", which is not what either arm means.
/// </para>
/// </summary>
public class BrowseCriterionAdsMatchingArmTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly FakeDateTimeProvider Clock = new(T0);

    private static readonly string[] SniIt = ["62010"];
    private static readonly string[] KommunStockholm = ["0180"];

    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();
    private readonly ICompanyWatchBrowseQuery _browse = Substitute.For<ICompanyWatchBrowseQuery>();

    private static FullCandidateMatchProfile AssessableProfile() =>
        new(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []);

    private static FullCandidateMatchProfile ProfilelessProfile() =>
        new(new CandidateMatchProfile("", [], [], [], []), []);

    [Fact]
    public async Task Handle_OnlyMatching_PagesTheMatchingSet_NotThePortsPage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        // Five ads in the criterion, three of which match. Page size 2 is what separates "page the
        // set" from "filter the page": filtering a loaded page of 2 could return at most 2 of the
        // 3, and would report a total of 5.
        var a1 = SeedAd(db, "Matchar 1", T0.AddDays(-1));
        var a2 = SeedAd(db, "Matchar inte 1", T0.AddDays(-2));
        var a3 = SeedAd(db, "Matchar 2", T0.AddDays(-3));
        var a4 = SeedAd(db, "Matchar inte 2", T0.AddDays(-4));
        var a5 = SeedAd(db, "Matchar 3", T0.AddDays(-5));

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobAdId>?>([a1, a2, a3, a4, a5]);
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { a1, a3, a5 });

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, Page: 1, PageSize: 2, OnlyMatching: true),
            ct);

        result.ShouldNotBeNull();
        // Three, not five: the total describes the matching SET, which is what the number linking
        // here counted.
        result.TotalCount.ShouldBe(3);
        result.Items.Select(i => i.Title).ShouldBe(["Matchar 1", "Matchar 2"]);

        // The unfiltered path is not taken at all — a handler that took both would pay twice and
        // could serve the wrong one.
        await _browse.DidNotReceiveWithAnyArgs()
            .BrowseAdIdsAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_OnlyMatching_SecondPage_ContinuesTheMatchingSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var a1 = SeedAd(db, "Matchar 1", T0.AddDays(-1));
        var a2 = SeedAd(db, "Matchar inte", T0.AddDays(-2));
        var a3 = SeedAd(db, "Matchar 2", T0.AddDays(-3));
        var a5 = SeedAd(db, "Matchar 3", T0.AddDays(-5));

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobAdId>?>([a1, a2, a3, a5]);
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { a1, a3, a5 });

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, Page: 2, PageSize: 2, OnlyMatching: true),
            ct);

        result.ShouldNotBeNull();
        // Page 2 of the SET is the third match. Page 2 of the criterion's ads, filtered, would be
        // empty or hold "Matchar 2" — both wrong, and both invisible on page 1.
        result.Items.Select(i => i.Title).ShouldBe(["Matchar 3"]);
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_OnlyMatching_KeepsThePortsOrder_NotTheMembershipSets()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var newest = SeedAd(db, "Nyast", T0.AddDays(-1));
        var middle = SeedAd(db, "Mitten", T0.AddDays(-10));
        var oldest = SeedAd(db, "Äldst", T0.AddDays(-30));

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        // The port publishes the order; the handler must follow THIS sequence.
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobAdId>?>([newest, middle, oldest]);
        // A HashSet has its own iteration order and it is not the port's. Enumerating the set
        // instead of filtering the list is the mistake this arm pins.
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { oldest, newest, middle });

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20, OnlyMatching: true), ct);

        result.ShouldNotBeNull();
        result.Items.Select(i => i.Title).ShouldBe(["Nyast", "Mitten", "Äldst"]);
    }

    [Fact]
    public async Task Handle_OnlyMatching_NoStatedOccupation_DeliversTheUnfilteredList()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);
        var ad = SeedAd(db, "Utvecklare", T0.AddDays(-1));

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(ProfilelessProfile());
        _browse.BrowseAdIdsAsync(Arg.Any<CompanyBrowseCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<JobAdId>([ad], 1, 1, 20));

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20, OnlyMatching: true), ct);

        result.ShouldNotBeNull();
        // The filter is INERT, not empty. An empty page would assert that nothing matches this
        // user, which is precisely what an unassessable profile cannot establish.
        result.Items.Select(i => i.Title).ShouldBe(["Utvecklare"]);
    }

    [Fact]
    public async Task Handle_OnlyMatching_SetTooLarge_DeliversTheUnfilteredList()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);
        var ad = SeedAd(db, "Utvecklare", T0.AddDays(-1));

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobAdId>?>((IReadOnlyList<JobAdId>?)null);
        _browse.BrowseAdIdsAsync(Arg.Any<CompanyBrowseCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<JobAdId>([ad], 1, 1, 20));

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20, OnlyMatching: true), ct);

        result.ShouldNotBeNull();
        result.Items.Select(i => i.Title).ShouldBe(["Utvecklare"]);
    }

    [Fact]
    public async Task Handle_WithoutTheFlag_NeverResolvesTheMatchingSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);
        var ad = SeedAd(db, "Utvecklare", T0.AddDays(-1));

        _browse.BrowseAdIdsAsync(Arg.Any<CompanyBrowseCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<JobAdId>([ad], 1, 1, 20));

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldNotBeNull();
        // The default arm pays for neither the profile nor the set scan. A grade computed for a
        // page nobody asked to filter is pure cost on the route's shared rate-limit budget.
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default!, default, CancellationToken.None);
        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    private BrowseCriterionAdsQueryHandler Sut(AppDbContext db, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new BrowseCriterionAdsQueryHandler(
            db, currentUser, Substitute.For<IFailedAccessLogger>(),
            _browse, _perUserSearch, _profileBuilder);
    }

    private static async Task<CompanyWatchCriterion> SeedCriterionAsync(
        AppDbContext db, Guid userId, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholm).Value;
        var criterion = CompanyWatchCriterion.Create(userId, spec, label: null, Clock).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion;
    }

    private static JobAdId SeedAd(AppDbContext db, string title, DateTimeOffset publishedAt)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var import = JobAd.Import(
            title: title,
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: publishedAt,
            expiresAt: publishedAt.AddDays(60),
            clock: new FakeDateTimeProvider(publishedAt),
            declaredContacts: [],
            extractTerms: TestKeywordExtraction.None);
        import.IsSuccess.ShouldBeTrue($"seed: JobAd.Import måste lyckas ({import.Error?.Code})");
        db.JobAds.Add(import.Value);
        db.SaveChanges();
        return import.Value.Id;
    }
}
