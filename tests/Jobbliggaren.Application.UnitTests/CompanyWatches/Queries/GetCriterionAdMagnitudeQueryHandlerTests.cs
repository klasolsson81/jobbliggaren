using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1559 — <see cref="GetCriterionAdMagnitudeQueryHandler"/>. Unit-testable against InMemory for the
/// same reason its siblings are: the register is not on <c>IAppDbContext</c> (DPIA C-D4), so the
/// handler can only read the user's own criterion and the join answers through a faked port. The
/// port's real SQL is proven against Postgres in <c>CompanyWatchBrowseQueryPlanTests</c> (the plans)
/// and <c>CompanyWatchMaterialisedAdReadTests</c> (the rows and the states), plus the endpoint suite.
///
/// <para>
/// <b>#1681 part 2 (ADR 0139) — the port is now keyed by criterion id + predicate FINGERPRINT.</b>
/// The call assertions below therefore read "the port was asked about THIS criterion AS IT IS NOW"
/// where they used to read "about THIS predicate". That is strictly stronger: an id alone would
/// happily be answered from a member set computed for a predicate its owner has since edited, which
/// is the whole defect the fingerprint exists to close.
/// </para>
/// </summary>
public class GetCriterionAdMagnitudeQueryHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero));

    private static readonly string[] SniIt = ["62010"];
    private static readonly string[] KommunStockholm = ["0180"];

    private static void CountReturns(ICompanyWatchBrowseQuery port, MaterialisedAdCount answer) =>
        port.CountActiveAdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(answer);

    [Fact]
    public async Task Handle_OwnCriterion_CountsThroughThePort_WithTheProductCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        CountReturns(port, MaterialisedAdCount.Counted(167, saturated: false));

        var result = await HandlerFor(db, Owner, port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Magnitude.ShouldBe(167);
        result.Saturated.ShouldBeFalse();
        result.TooBroad.ShouldBeFalse();
        result.NotMaterialised.ShouldBeFalse();

        // The ceiling is passed from its single source rather than re-stated at the call site.
        // It does NOT distinguish the two constants: CriterionAdMagnitudeDto.Ceiling and
        // CriterionMatchMagnitudeDto.Ceiling are both 10 000 today, so swapping them keeps this
        // green. Which constant is used is a compile-time fact, not a runtime one (test-writer V3).
        //
        // #1681 part 2 — the KEY is the criterion's id AND the fingerprint of the predicate it
        // carries right now. Asking with the id alone would let a stale member set answer.
        await port.Received(1).CountActiveAdsAsync(
            new CompanyWatchCriterionId(criterion.Id.Value),
            CriteriaFingerprint.Of(criterion.Criteria),
            CriterionAdMagnitudeDto.Ceiling,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CountAtTheCeiling_IsReportedSaturated()
    {
        // The saturation arm is what makes the copy say "10 000+" instead of a bare number the join
        // cannot stand behind (#859). It is REACHABLE, not decorative: the breadth gate bounds
        // COMPANIES, not ads, so a bound-legal 1 000-member criterion can still carry 28 971 active
        // ads (the adversarial worst case on the real register,
        // docs/reviews/2026-09-06-1681-part2-read-form-measurement.md) — about 3x this ceiling.
        //
        // #1681 part 2 moved the COMPARISON into the port, which knows the cap it applied. The
        // pairing stubbed here is therefore the one the adapter actually emits at this ceiling
        // (`saturated: count >= cap`); a stub pairing a low count with `saturated: true` would be a
        // premise no production path produces (CLAUDE.md §5 Tests:), and it is not used.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        CountReturns(
            port, MaterialisedAdCount.Counted(CriterionAdMagnitudeDto.Ceiling, saturated: true));

        var result = await HandlerFor(db, Owner, port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Saturated.ShouldBeTrue();
        result.Magnitude.ShouldBe(CriterionAdMagnitudeDto.Ceiling);
    }

    [Fact]
    public async Task Handle_JustBelowTheCeiling_IsNotSaturated()
    {
        // The boundary in the other direction — without this the flag could be hard-wired true (or
        // the port's own `>=` become `>`) with the saturated-arm test still green.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        CountReturns(
            port,
            MaterialisedAdCount.Counted(CriterionAdMagnitudeDto.Ceiling - 1, saturated: false));

        var result = await HandlerFor(db, Owner, port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Saturated.ShouldBeFalse();
        result.Magnitude.ShouldBe(CriterionAdMagnitudeDto.Ceiling - 1);
    }

    [Fact]
    public async Task Handle_TooBroadCriterion_RefusesToCount_NeverReportsAZero()
    {
        // #1681 part 2 — the breadth gate refused this criterion, so no member set exists and there
        // is no number to render. A zero here would tell the user their watch matches nothing, which
        // is the opposite of the truth: it matches too much.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        CountReturns(port, MaterialisedAdCount.TooBroad);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Magnitude.ShouldBeNull();
        result.TooBroad.ShouldBeTrue();
        result.NotMaterialised.ShouldBeFalse();
        result.Saturated.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_NotMaterialisedYet_IsUnknown_NeitherAZeroNorForBred()
    {
        // #1681 part 2 — the COMMON state, not an exotic one: every criterion sits here between its
        // creation and the next materialisation run, and every criterion returns here the moment its
        // owner edits the predicate. Rendering it as "för bred" would tell the user to narrow a watch
        // that is merely waiting to be counted; rendering it as 0 would be the dishonest zero.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        CountReturns(port, MaterialisedAdCount.NotMaterialised);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Magnitude.ShouldBeNull();
        result.NotMaterialised.ShouldBeTrue();
        result.TooBroad.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_UnknownCriterion_ReturnsNotFound_AndLogsNoCrossUserAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var result = await HandlerFor(db, Owner, Substitute.For<ICompanyWatchBrowseQuery>(), failedAccess)
            .Handle(new GetCriterionAdMagnitudeQuery(Guid.NewGuid()), ct);

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
            .Handle(new GetCriterionAdMagnitudeQuery(theirs.Id.Value), ct);

        // Detected (ADR 0031), and the operation name is THIS surface's — a shared constant would
        // erase which surface was probed.
        failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", theirs.Id.Value, Owner, CriterionReadOperation.GetCriterionAdMagnitude);

        // The materialised set is never read on behalf of a stranger.
        await port.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);

        // Indistinguishable from the unknown-id answer: both literally null, so they cannot drift into
        // two shapes an attacker could tell apart (IDOR).
        var unknownId = await HandlerFor(db, Owner, port, Substitute.For<IFailedAccessLogger>())
            .Handle(new GetCriterionAdMagnitudeQuery(Guid.NewGuid()), ct);

        crossUser.ShouldBeNull();
        unknownId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ReturnsNotFound_WithoutReadingTheMaterialisedSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var result = await new GetCriterionAdMagnitudeQueryHandler(
                db, currentUser, Substitute.For<IFailedAccessLogger>(), Resolver(port))
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        // What this measures is the null-user arm, NOT the Guid.Empty fallback the loader's own
        // docblock warns about — a criterion owned by Guid.Empty cannot exist
        // (CompanyWatchCriterionTests pins that Create refuses it), so no test at this layer can
        // distinguish the fallback (test-writer V1: the proposed shape is unbuildable, and the
        // guarantee is the Domain's, not this handler's).
        result.ShouldBeNull();
        await port.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public void Dto_RejectsANumberBesideEitherRefusal_AndTheTwoRefusalsTogether()
    {
        // #1681 part 2 — the DTO grew from two states to three, so its guard grew a case that no
        // handler path constructs and nothing else would try. A magnitude next to "för bred" is a
        // figure with no measurement behind it; "för bred" AND "not materialised" together is two
        // different answers claimed at once.
        Should.Throw<ArgumentException>(() =>
            new CriterionAdMagnitudeDto(3, Saturated: false, TooBroad: true, NotMaterialised: false));

        Should.Throw<ArgumentException>(() =>
            new CriterionAdMagnitudeDto(0, Saturated: false, TooBroad: false, NotMaterialised: true));

        Should.Throw<ArgumentException>(() =>
            new CriterionAdMagnitudeDto(null, Saturated: false, TooBroad: true, NotMaterialised: true));

        // An answerable question with no number is a measurement thrown away.
        Should.Throw<ArgumentException>(() =>
            new CriterionAdMagnitudeDto(null, Saturated: false, TooBroad: false, NotMaterialised: false));

        // Saturation is a property of a NUMBER, not of a refusal.
        Should.Throw<ArgumentException>(() =>
            new CriterionAdMagnitudeDto(null, Saturated: true, TooBroad: true, NotMaterialised: false));
    }

    [Fact]
    public void Dto_Factories_CarryTheThreeStatesApart()
    {
        // The negative control for the guard above: an UNCONDITIONAL throw would satisfy every
        // Should.Throw there while making the type unusable. And zero is a real answer.
        CriterionAdMagnitudeDto.Counted(0, saturated: false).Magnitude.ShouldBe(0);
        CriterionAdMagnitudeDto.Counted(0, saturated: false).TooBroad.ShouldBeFalse();
        CriterionAdMagnitudeDto.Counted(0, saturated: false).NotMaterialised.ShouldBeFalse();

        CriterionAdMagnitudeDto.TooBroadToCount.Magnitude.ShouldBeNull();
        CriterionAdMagnitudeDto.TooBroadToCount.TooBroad.ShouldBeTrue();
        CriterionAdMagnitudeDto.TooBroadToCount.NotMaterialised.ShouldBeFalse();

        CriterionAdMagnitudeDto.NotMaterialisedYet.Magnitude.ShouldBeNull();
        CriterionAdMagnitudeDto.NotMaterialisedYet.NotMaterialised.ShouldBeTrue();
        CriterionAdMagnitudeDto.NotMaterialisedYet.TooBroad.ShouldBeFalse();

        CriterionAdMagnitudeDto.TooBroadToCount.ShouldNotBe(CriterionAdMagnitudeDto.NotMaterialisedYet);
    }

    private static GetCriterionAdMagnitudeQueryHandler HandlerFor(
        AppDbContext db,
        Guid userId,
        ICompanyWatchBrowseQuery port,
        IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new GetCriterionAdMagnitudeQueryHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>(), Resolver(port));
    }

    private static CriterionMatchingAdSetResolver Resolver(ICompanyWatchBrowseQuery port) =>
        new(Substitute.For<IMatchProfileBuilder>(),
            Substitute.For<IPerUserJobAdSearchQuery>(),
            port);

    private static async Task<CompanyWatchCriterion> SeedCriterionAsync(
        AppDbContext db, Guid userId, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholm).Value;
        var criterion = CompanyWatchCriterion.Create(userId, spec, label: null, Clock).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion;
    }
}
