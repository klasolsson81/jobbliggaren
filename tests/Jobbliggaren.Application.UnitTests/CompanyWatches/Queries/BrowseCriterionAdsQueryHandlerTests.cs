using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCriterionAds;
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

        var port = PortReturning([ad.Value], totalCount: 1);

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
    public async Task Handle_ReOrdersTheLoadedAds_NewestFirst_RegardlessOfTheIdArraysOrder()
    {
        // THE assertion this handler exists to make safe. `WHERE id = ANY(...)` does not preserve the
        // array's order, so the handler re-states the port's published order rather than trusting the
        // ids to arrive sorted. The ids are handed over in the WRONG order on purpose: if the re-order
        // were dropped, the rows would come back in whatever order the load produced and this test
        // would see the seeded order instead of the published one.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var oldest = SeedAd(db, "Äldst", "A AB", publishedAt: T0.AddDays(-30));
        var newest = SeedAd(db, "Nyast", "B AB", publishedAt: T0.AddDays(-1));
        var middle = SeedAd(db, "Mitten", "C AB", publishedAt: T0.AddDays(-10));

        var port = PortReturning([oldest.Value, middle.Value, newest.Value], totalCount: 3);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldNotBeNull();
        result.Items.Select(i => i.Title).ShouldBe(["Nyast", "Mitten", "Äldst"]);
    }

    [Fact]
    public async Task Handle_EmptyPage_ShortCircuits_ButStillCarriesThePortsTotalCount()
    {
        // Page 100 of a two-row set: no ids, so no ad load is worth a round-trip — but the pagination
        // total is the PORT's answer and must survive the short-circuit, or the pager would collapse
        // to "0 rows" on any page past the end.
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
            "CompanyWatchCriterion", theirs.Id.Value, Owner, nameof(BrowseCriterionAdsQuery));

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
                db, currentUser, Substitute.For<IFailedAccessLogger>(), port)
            .Handle(new BrowseCriterionAdsQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldBeNull();
        await port.DidNotReceiveWithAnyArgs().BrowseAdIdsAsync(default!, CancellationToken.None);
    }

    private static ICompanyWatchBrowseQuery PortReturning(
        Guid[] ids, int totalCount, int page = 1, int pageSize = 20)
    {
        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.BrowseAdIdsAsync(Arg.Any<CompanyBrowseCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Guid>(ids, totalCount, page, pageSize));
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
        return new BrowseCriterionAdsQueryHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>(), port);
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
