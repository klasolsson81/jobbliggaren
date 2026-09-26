using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCriterionAds;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
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
/// The INERT arms are pinned as DELIVERY, not as emptiness. A caller who stated no occupation and a
/// watch too broad to grade both get the unfiltered list: an empty page would say "nothing matches
/// you", which is not what either arm means.
/// </para>
///
/// <para>
/// <b>#1681 part 2 (ADR 0139) added a THIRD inert arm, and it is the only one that cannot deliver a
/// list.</b> When the criterion has not been materialised for its CURRENT predicate the filter is
/// inert exactly as the other two are — the handler falls through to the unfiltered browse — but
/// that browse has no member set to read either, so what comes back is an empty page. The two facts
/// are separate and the second is not this arm's doing: the fall-through is pinned here, and the
/// unfiltered arm's own no-page states are pinned in
/// <c>BrowseCriterionAdsQueryHandlerTests.Handle_NotMaterialisedYet_IsAnEmptyPage_NeverA404</c>.
/// </para>
/// </summary>
public class BrowseCriterionAdsMatchingArmTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly FakeDateTimeProvider Clock = new(T0);

    private static readonly string[] SniIt = ["62010"];
    private static readonly string[] KommunStockholm = ["0180"];

    private void MagnitudeIs(int count) =>
        _browse.CountActiveAdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdCount.Counted(
                count, saturated: count >= CriterionAdMagnitudeDto.Ceiling));

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
        MagnitudeIs(12);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.Resolved([a1, a2, a3, a4, a5]));
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
            .BrowseAdIdsAsync(default, default, default, default, CancellationToken.None);
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
        MagnitudeIs(12);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.Resolved([a1, a2, a3, a5]));
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
        // The magnitude has to agree with the set the port hands over — the gate reads it before the
        // set query runs. Before #1681 part 2 this stub could be omitted, because an unstubbed
        // ValueTask<int> defaulted to 0 and the gate happened to admit; the port now answers with a
        // three-state record whose default is null, so the omission is no longer survivable. The
        // value is the fixture's own ad count rather than a number picked to pass.
        MagnitudeIs(3);
        // The port publishes the order; the handler must follow THIS sequence.
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.Resolved([newest, middle, oldest]));
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
        _browse.BrowseAdIdsAsync(
                    Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                    Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdPage.Resolved(new PagedResult<JobAdId>([ad], 1, 1, 20)));

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
        MagnitudeIs(12);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.TooManyAds);
        _browse.BrowseAdIdsAsync(
                    Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                    Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdPage.Resolved(new PagedResult<JobAdId>([ad], 1, 1, 20)));

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20, OnlyMatching: true), ct);

        result.ShouldNotBeNull();
        result.Items.Select(i => i.Title).ShouldBe(["Utvecklare"]);
    }

    [Fact]
    public async Task Handle_OnlyMatching_NotMaterialised_FallsThroughToTheUnfilteredArm()
    {
        // #1681 part 2 — the third inert arm. The filter cannot be honoured (nothing was graded), so
        // the handler must NOT cut a page from an empty matching set and present it as the answer:
        // that page would say "none of these ads match you", about a watch nobody has counted.
        //
        // Both stubbed answers come from ONE state of the world — no materialisation exists for this
        // criterion's current predicate — so the count and the page agree, exactly as the two
        // statements do against real Postgres.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);
        SeedAd(db, "Utvecklare", T0.AddDays(-1));

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _browse.CountActiveAdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdCount.NotMaterialised);
        _browse.BrowseAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdPage.NotMaterialised);

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20, OnlyMatching: true), ct);

        result.ShouldNotBeNull();
        result.Items.ShouldBeEmpty();

        // THE assertion: the unfiltered arm was ENTERED. Without it this test cannot tell a
        // fall-through from a filtered page that happened to be empty — the two produce the same
        // rows, and only one of them is honest.
        await _browse.Received(1).BrowseAdIdsAsync(
            new CompanyWatchCriterionId(criterion.Id.Value),
            CriteriaFingerprint.Of(criterion.Criteria),
            1, 20, Arg.Any<CancellationToken>());

        // ...and no set was read for a criterion that has none.
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_OnlyMatching_TooBroad_DeliversTheUnfilteredList_WithoutASecondSetQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);
        var ad = SeedAd(db, "Utvecklare", T0.AddDays(-1));

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _browse.BrowseAdIdsAsync(
                    Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                    Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdPage.Resolved(new PagedResult<JobAdId>([ad], 1, 1, 20)));

        MagnitudeIs(CriterionMatchingAdSetResolver.MaxSetSize + 1);
        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20, OnlyMatching: true), ct);

        result.ShouldNotBeNull();
        result.Items.Select(i => i.Title).ShouldBe(["Utvecklare"]);

        // The gate decided it from the magnitude the request already measured, so the ordered set
        // query is never issued. #1681 part 2 changed what that saving IS: before ADR 0139 the set
        // query re-resolved the predicate against 1,07M register rows, and skipping it saved seconds
        // on exactly this criterion; it is now a bounded index join over the materialised member set,
        // so what the gate saves is a round trip whose answer the count already determined. Cheaper
        // either way — and, more importantly, the gate can only skip a query whose outcome is already
        // known. It can never admit one the probe would have refused.
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_OnlyMatching_CapsTheTotalAtWhatTheSurfaceCanServe()
    {
        // TotalPages = ceil(TotalCount / PageSize) while the validator 400s past MaxPage, so an
        // uncapped total advertises pages the pager cannot fetch -- not slow, FALSE. The unfiltered
        // arm gets the cap from the port; this arm has to apply it itself, and at pageSize 20 against
        // a small set the cap is inert, so only a small page size can see it.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var ids = Enumerable.Range(0, 150).Select(_ => new JobAdId(Guid.NewGuid())).ToList();

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        MagnitudeIs(ids.Count);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.Resolved(ids));
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(ids.ToHashSet());

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 1, OnlyMatching: true), ct);

        result.ShouldNotBeNull();
        // 150 matching ads at pageSize 1: the surface can serve 100, so that is what the pager may
        // advertise. Uncapped it would offer 150 pages of which 100 are fetchable.
        result.TotalCount.ShouldBe(CompanyBrowseCriteria.MaxServableRows(1));
    }

    [Fact]
    public async Task Handle_WithoutTheFlag_NeverResolvesTheMatchingSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);
        var ad = SeedAd(db, "Utvecklare", T0.AddDays(-1));

        _browse.BrowseAdIdsAsync(
                    Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                    Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdPage.Resolved(new PagedResult<JobAdId>([ad], 1, 1, 20)));

        var result = await Sut(db, Owner).Handle(
            new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldNotBeNull();
        // The default arm pays for neither the profile nor the set scan. A grade computed for a
        // page nobody asked to filter is pure cost on the route's shared rate-limit budget.
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
        await _browse.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    private BrowseCriterionAdsQueryHandler Sut(AppDbContext db, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new BrowseCriterionAdsQueryHandler(
            db, currentUser, Substitute.For<IFailedAccessLogger>(), _browse,
            new CriterionMatchingAdSetResolver(_profileBuilder, _perUserSearch, _browse));
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
