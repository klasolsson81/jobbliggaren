using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
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
/// #1559 — <see cref="BrowseCriterionAdsQueryHandler"/>: the criterion's own ad list. The port (which
/// owns the register JOIN) is faked; the ad LOAD runs against the real <c>JobAds</c> set, because the
/// re-order and the projection are this handler's own responsibility and are what a fake would hide.
/// </summary>
public class BrowseCriterionAdsQueryHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly DateTimeOffset T0 = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly FakeDateTimeProvider Clock = new(T0);

    private static readonly string[] SniIt = ["62010"];
    private static readonly string[] KommunStockholm = ["0180"];

    [Fact]
    public async Task Handle_OwnCriterion_LoadsThePortsIds_AndProjectsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var ad = SeedAd(db, "Utvecklare", "Acme AB", publishedAt: T0.AddDays(-1));

        var port = PortReturning([ad], totalCount: 1);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCriterionAdsQuery(criterion.Id.Value, Page: 1, PageSize: 20), ct);

        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(1);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);

        var row = result.Items.Single();
        row.Id.ShouldBe(ad.Value);
        row.Title.ShouldBe("Utvecklare");
        row.CompanyName.ShouldBe("Acme AB");
        row.Status.ShouldBe(JobAdStatus.Active.Value);

        // The port receives the criterion's OWN predicate and the request's transport bounds — the
        // request can influence the paging and nothing else.
        await port.Received(1).BrowseAdIdsAsync(
            Arg.Is<CompanyBrowseCriteria>(c => c != null
                && c.Criteria.SniCodes.SequenceEqual(SniIt)
                && c.Criteria.MunicipalityCodes.SequenceEqual(KommunStockholm)
                && c.Page == 1
                && c.PageSize == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FollowsThePortsSequence_EvenWhenItDisagreesWithPublishedAt()
    {
        // The handler does NOT sort — it re-sequences by the port's ordinal, and this pins that on
        // the PublishedAt axis (its sibling pins it on the id axis). The port is made to hand over an
        // order that published_at would not produce: any handler that re-derived "newest first"
        // would return them sorted and fail here.
        //
        // Newest-first is the PORT's guarantee, not this handler's: it is pinned on the SQL by
        // AdIdsQuery_OrdersByATotalKey and end-to-end by Ad_browse_orders_newest_first_across_the_wire.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var oldest = SeedAd(db, "Äldst", "A AB", publishedAt: T0.AddDays(-30));
        var newest = SeedAd(db, "Nyast", "B AB", publishedAt: T0.AddDays(-1));
        var middle = SeedAd(db, "Mitten", "C AB", publishedAt: T0.AddDays(-10));

        var port = PortReturning([oldest, middle, newest], totalCount: 3);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldNotBeNull();
        result.Items.Select(i => i.Title).ShouldBe(["Äldst", "Mitten", "Nyast"]);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheIdsThePortGave_NeverEveryAd()
    {
        // `.Where(j => page.Items.Contains(j.Id))` is the ONLY thing stopping this handler from
        // projecting the whole job_ads table. Without a second seeded ad the filter can be deleted
        // with every other test in this class still green (test-writer V6).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var wanted = SeedAd(db, "Efterfrågad", "A AB", publishedAt: T0.AddDays(-1));
        SeedAd(db, "Icke efterfrågad", "B AB", publishedAt: T0.AddDays(-2));

        var port = PortReturning([wanted], totalCount: 1);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldNotBeNull();
        result.Items.Count.ShouldBe(1);
        result.Items.Single().Id.ShouldBe(wanted.Value);
    }

    [Fact]
    public async Task Handle_PreservesThePortsSequence_NeverAReDerivedOrder()
    {
        // The tiebreak half, and the oracle is the PORT'S sequence rather than a re-derivation of it.
        // An earlier version asserted `.OrderBy(g => g)` — the CLR's Guid order — which is a THIRD
        // order: Postgres compares uuid bytewise while Guid.CompareTo reads the first field as a
        // signed Int32, so the two disagree on about half of all pairs. That oracle mirrored the
        // implementation and would have pinned the divergence in place instead of measuring it
        // (code-reviewer / dotnet-architect re-check, 2026-09-04).
        //
        // The ids are handed over in an order the CLR comparer would NOT produce, so a handler that
        // re-derived the order instead of following the array would be visible here.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var sameInstant = T0.AddDays(-3);
        var a = SeedAd(db, "Först", "A AB", publishedAt: sameInstant);
        var b = SeedAd(db, "Sedan", "B AB", publishedAt: sameInstant);

        // Whichever of the two the CLR would sort LAST is put FIRST on the wire.
        var clrOrder = new[] { a.Value, b.Value }.OrderBy(g => g).ToList();
        var portOrder = new[] { new JobAdId(clrOrder[1]), new JobAdId(clrOrder[0]) };

        var port = PortReturning(portOrder, totalCount: 2);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldNotBeNull();
        result.Items.Select(i => i.Id).ShouldBe(portOrder.Select(i => i.Value).ToList());
    }

    [Fact]
    public async Task Handle_EmptyPage_CarriesThePortsTotalCountAndPageNumber()
    {
        // Page 100 of a two-row set. What this pins is that the PORT's pagination total survives an
        // empty page — otherwise the pager would collapse to "0 rows" on any page past the end.
        // It does NOT pin the short-circuit itself: deleting that early return leaves this green,
        // because an empty id set produces the same result through the query path. The
        // short-circuit is an optimisation, and on InMemory there is no command to count
        // (test-writer V2 — the name says what is measured).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);
        SeedAd(db, "Finns", "A AB", publishedAt: T0);

        var port = PortReturning([], totalCount: 2, page: 100);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCriterionAdsQuery(criterion.Id.Value, Page: 100, PageSize: 20), ct);

        result.ShouldNotBeNull();
        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(2);
        result.Page.ShouldBe(100);
    }

    [Fact]
    public async Task Handle_UnknownCriterion_ReturnsNotFound_AndLogsNoCrossUserAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var result = await HandlerFor(db, Owner, Substitute.For<ICompanyWatchBrowseQuery>(), failedAccess)
            .Handle(new BrowseCriterionAdsQuery(Guid.NewGuid(), 1, 20), ct);

        result.ShouldBeNull();
        failedAccess.DidNotReceiveWithAnyArgs().LogCrossUserAttempt(default!, default, default, default!);
    }

    [Fact]
    public async Task Handle_AnotherUsersCriterion_ReturnsTheIDENTICAL_NotFound_AndLogsTheCrossUserAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var theirs = await SeedCriterionAsync(db, Stranger, ct);

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var crossUser = await HandlerFor(db, Owner, port, failedAccess)
            .Handle(new BrowseCriterionAdsQuery(theirs.Id.Value, 1, 20), ct);

        failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", theirs.Id.Value, Owner, CriterionReadOperation.BrowseCriterionAds);

        await port.DidNotReceiveWithAnyArgs().BrowseAdIdsAsync(default!, CancellationToken.None);

        var unknownId = await HandlerFor(db, Owner, port, Substitute.For<IFailedAccessLogger>())
            .Handle(new BrowseCriterionAdsQuery(Guid.NewGuid(), 1, 20), ct);

        crossUser.ShouldBeNull();
        unknownId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ReturnsNotFound_WithoutTouchingTheRegister()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var result = await new BrowseCriterionAdsQueryHandler(
                db, currentUser, Substitute.For<IFailedAccessLogger>(), port,
                Substitute.For<IPerUserJobAdSearchQuery>(), Substitute.For<IMatchProfileBuilder>())
            .Handle(new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldBeNull();
        await port.DidNotReceiveWithAnyArgs().BrowseAdIdsAsync(default!, CancellationToken.None);
    }

    private static ICompanyWatchBrowseQuery PortReturning(
        JobAdId[] ids, int totalCount, int page = 1, int pageSize = 20)
    {
        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.BrowseAdIdsAsync(Arg.Any<CompanyBrowseCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<JobAdId>(ids, totalCount, page, pageSize));
        return port;
    }

    private static BrowseCriterionAdsQueryHandler HandlerFor(
        AppDbContext db,
        Guid userId,
        ICompanyWatchBrowseQuery port,
        IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        // The matching collaborators are inert for every arm in this class: OnlyMatching is false,
        // so the handler never reaches CriterionMatchingAdSet. #1656 (b)'s filtered arm has its own
        // class, which wires them for real.
        return new BrowseCriterionAdsQueryHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>(), port,
            Substitute.For<IPerUserJobAdSearchQuery>(), Substitute.For<IMatchProfileBuilder>());
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

    /// <summary>
    /// Seeds through the production ingest entry point (<c>JobAd.Import</c>), so the rows this handler
    /// reads are ones the ingest actually produces — the state under assertion is not hand-built
    /// (CLAUDE.md §5 <c>Tests:</c>).
    /// </summary>
    private static JobAdId SeedAd(
        AppDbContext db, string title, string companyName, DateTimeOffset publishedAt)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var import = JobAd.Import(
            title: title,
            company: Company.Create(companyName).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: publishedAt,
            expiresAt: publishedAt.AddDays(60),
            clock: new FakeDateTimeProvider(publishedAt), declaredContacts: [], extractTerms: TestKeywordExtraction.None);
        import.IsSuccess.ShouldBeTrue($"seed: JobAd.Import måste lyckas ({import.Error?.Code})");
        db.JobAds.Add(import.Value);
        db.SaveChanges();
        return import.Value.Id;
    }
}
