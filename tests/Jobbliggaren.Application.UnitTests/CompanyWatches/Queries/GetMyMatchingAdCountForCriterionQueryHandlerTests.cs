using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.CompanyWatches.Queries.GetMyMatchingAdCountForCriterion;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1656 (b) — <see cref="GetMyMatchingAdCountForCriterionQueryHandler"/> over the REAL
/// <see cref="CriterionMatchingAdSetResolver"/> with faked ports, so the guards and the gate are the
/// ones production runs.
///
/// <para>
/// What is pinned is the ANSWER gate and the ORDER of its guards — FOUR answers since #1681 part 2,
/// which added "not materialised for the current predicate". Every one of them is rendered by the
/// same line of the UI and three of them carry no number, so a collapse into "0" would be invisible
/// to every other test: a zero says "nothing matches you" where the truth is "nothing was measured".
/// </para>
///
/// <para>
/// The guard ORDER is asserted with <c>DidNotReceive</c>, not with outcomes — the outcomes are
/// identical either way, so nothing else can see it. That includes the CROSSED arm (no occupation
/// AND an oversized watch), which is the only place two no-number answers can be told apart.
/// </para>
///
/// <para>
/// InMemory is enough for the criterion read: the register is not on <c>IAppDbContext</c> (DPIA
/// C-D4), so this handler reads only the user's own criterion and both joins answer through faked
/// ports. The real SQL and the real grade are proven against Postgres in
/// <c>CompanyWatchBrowseQueryPlanTests</c> (the plans),
/// <c>CompanyWatchMaterialisedAdReadTests</c> (the rows, the four states and the staleness guard)
/// and <c>CriterionMatchingAdCountApiTests</c>.
/// </para>
///
/// <para>
/// <b>Every stubbed port answer is one <c>CompanyWatchBrowseQuery</c> emits.</b> <c>MagnitudeIs</c>
/// derives <c>Saturated</c> the way the adapter does (<c>count &gt;= cap</c>, and the cap on this
/// path is <c>CriterionAdMagnitudeDto.Ceiling</c>), so no test here rests on a count/flag pairing
/// production cannot produce (CLAUDE.md §5 <c>Tests:</c>).
/// </para>
/// </summary>
public class GetMyMatchingAdCountForCriterionQueryHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero));

    private static readonly string[] SniIt = ["62010"];
    private static readonly string[] KommunStockholm = ["0180"];

    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();
    private readonly ICompanyWatchBrowseQuery _browse = Substitute.For<ICompanyWatchBrowseQuery>();

    // Non-empty Fast.SsykGroupConceptIds → assessable, so the grade filter IS consulted.
    private static FullCandidateMatchProfile AssessableProfile() =>
        new(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []);

    // Empty Fast.SsykGroupConceptIds → matching is undefined, and FilterToMatchingAsync fail-fasts
    // on exactly this profile rather than returning an empty set that would read as "zero matches".
    private static FullCandidateMatchProfile ProfilelessProfile() =>
        new(new CandidateMatchProfile("", [], [], [], []), []);

    private void MagnitudeIs(int count) =>
        _browse.CountActiveAdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdCount.Counted(
                count, saturated: count >= CriterionAdMagnitudeDto.Ceiling));

    [Fact]
    public async Task Handle_AssessableProfile_CountsTheMatchingSubsetOfTheCriterionsAds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var a1 = new JobAdId(Guid.NewGuid());
        var a2 = new JobAdId(Guid.NewGuid());
        var a3 = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        MagnitudeIs(3);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.Resolved([a1, a2, a3]));
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { a1, a3 });

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        // Two of three, never three: the count is the GRADED subset, not the criterion's ad set.
        result.Count.ShouldBe(2);
        result.TooBroad.ShouldBeFalse();

        // The bound comes from its single source, and the criterion's own predicate is what is
        // counted (a resolver that passed some other key would count somebody else's watch).
        //
        // #1681 part 2 — that key is now the criterion's ID plus the FINGERPRINT of the predicate it
        // carries right now, which is strictly stronger than the spec this assertion used to name:
        // the id alone would be answered from a member set computed for a predicate its owner has
        // since edited, and the fingerprint is what refuses that.
        await _browse.Received(1).ListActiveAdIdsAsync(
            new CompanyWatchCriterionId(criterion.Id.Value),
            CriteriaFingerprint.Of(criterion.Criteria),
            CriterionMatchingAdSetResolver.MaxSetSize,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoStatedOccupation_ReturnsNotAssessed_AndNeverTouchesTheRegister()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(ProfilelessProfile());

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        // NOT a zero. A zero would tell the user nothing matches them; the truth is that they have
        // stated no occupation, and the surface renders a nudge instead.
        result.Count.ShouldBeNull();
        result.TooBroad.ShouldBeFalse();

        // The ORDER of the guards, which the outcome alone cannot witness: the register is not read
        // at all for a caller whose result could not be graded — neither half of it.
        await _browse.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoStatedOccupation_AndAnOversizedWatch_NudgesRatherThanRefuses()
    {
        // The CROSSED arm. Both answers carry no number, so only TooBroad separates them — and they
        // give opposite advice: one says state an occupation, the other says narrow the watch. With
        // the guards in the wrong order this user is told to narrow a watch that would still not be
        // gradeable, and the one action they can take is never named.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(ProfilelessProfile());
        MagnitudeIs(CriterionMatchingAdSetResolver.MaxSetSize + 1);

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.TooBroad.ShouldBeFalse();
        result.Count.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_MagnitudeOneOverTheBound_RefusesWithoutReadingTheSet()
    {
        // The GATE arm. The number is one the request already measured for the headline, so a
        // criterion too broad to grade costs no set query at all.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        MagnitudeIs(CriterionMatchingAdSetResolver.MaxSetSize + 1);

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.TooBroad.ShouldBeTrue();
        result.Count.ShouldBeNull();

        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_MagnitudeExactlyAtTheBound_IsAnswered_NotRefused()
    {
        // The boundary from the other side, and the reason the operator is `>` rather than `>=`:
        // MaxSetSize is derived as the most rows the destination can page to, and exactly that many
        // is exactly pageable. The port agrees — it accepts a set of MaxSetSize and refuses at
        // MaxSetSize + 1 — so this arm is what stops the two encodings of one bound drifting apart.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var ad = new JobAdId(Guid.NewGuid());
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        MagnitudeIs(CriterionMatchingAdSetResolver.MaxSetSize);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.Resolved([ad]));
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { ad });

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        result.TooBroad.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_GateAdmitsButProbeRefuses_StillRefuses()
    {
        // The RACE arm, and the reason the probe survives the gate: the count said the set fits, the
        // set grew before the query ran, and the port refused. Were the gate allowed to replace the
        // probe, this is the request that would have counted a truncated prefix.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        MagnitudeIs(12);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.TooManyAds);

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.TooBroad.ShouldBeTrue();
        result.Count.ShouldBeNull();

        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoAdsAtAll_IsAnHonestZero_NotARefusal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        MagnitudeIs(0);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.Resolved([]));

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        // An empty set is a real answer and must not be confused with the refusal, whose empty
        // LIST would look identical to a consumer reading cardinality alone.
        result.Count.ShouldBe(0);
        result.TooBroad.ShouldBeFalse();

        // `= ANY('{}')` cannot match a row, so the grade round-trip is skipped rather than made.
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotMaterialisedForTheCurrentPredicate_IsUnknown_NeitherZeroNorTooBroad()
    {
        // #1681 part 2 — the FOURTH answer, and the arm the switch in this handler gained. It is the
        // state every criterion is in between its creation and the next materialisation run, and the
        // state every criterion RETURNS to the moment its owner edits the predicate.
        //
        // TooBroad is what a collapse would produce, and it is the wrong advice: "narrow this watch"
        // cannot help a watch that is merely waiting to be counted. A zero is worse still.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _browse.CountActiveAdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdCount.NotMaterialised);

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Count.ShouldBeNull();
        result.NotMaterialised.ShouldBeTrue();
        result.TooBroad.ShouldBeFalse();

        // There was no set to grade, so nothing was graded. On a route the CTO ordered batched
        // precisely because its fan-in cost is the least well characterised, grading a criterion
        // that has no member set is pure cost.
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
        await _perUserSearch.DidNotReceiveWithAnyArgs().FilterToMatchingAsync(
            default!, default!, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_PredicateEditedBetweenTheTwoReads_IsUnknown_NotAStaleNumber()
    {
        // The other route into the fourth answer, and a genuinely reachable race: the count answered
        // against the materialisation, the owner saved a new predicate, and the set read then found a
        // fingerprint that no longer matches. The port reports ignorance for the second read, and the
        // honest answer for the whole request is ignorance — never the count the first read saw.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        MagnitudeIs(12);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.NotMaterialised);

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.NotMaterialised.ShouldBeTrue();
        result.Count.ShouldBeNull();
        result.TooBroad.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_AnotherUsersCriterion_Is404_AndAsksNothingAboutIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var result = await Sut(db, Stranger).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value), ct);

        // null RESPONSE is the authorization signal (→ 404), a different null from the DTO's
        // Count. Unknown and cross-user are the same answer, so the route is never an existence
        // oracle.
        result.ShouldBeNull();

        // The shared resolver is never reached, so it can never become an authorization shortcut.
        await _browse.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolver_MeasuresOncePerRequest_HoweverManyConsumersAsk()
    {
        // The whole reason the resolver is scoped. Three handlers read the same criterion in one
        // request; the register must answer once. Two resolutions are two measurements at two
        // instants, and a response whose count and list came from different instants is the
        // divergence this type exists to close.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var ad = new JobAdId(Guid.NewGuid());
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        MagnitudeIs(1);
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MaterialisedAdIds.Resolved([ad]));
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { ad });

        var resolver = new CriterionMatchingAdSetResolver(_profileBuilder, _perUserSearch, _browse);

        await resolver.MagnitudeAsync(criterion.Id.Value, criterion.Criteria, ct);
        await resolver.MatchingAsync(criterion.Id.Value, criterion.Criteria, ct);
        await resolver.MatchingAsync(criterion.Id.Value, criterion.Criteria, ct);

        await _browse.Received(1).CountActiveAdsAsync(
            Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _browse.Received(1).ListActiveAdIdsAsync(
            Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheGateIsExactOnlyWhileTheBoundSitsStrictlyUnderTheCountsCeiling()
    {
        // STRICT, and the strictness is the point. The gate reads `magnitude > MaxSetSize` against a
        // count that SATURATES at Ceiling. At equality that comparison is never true, so the gate
        // stops firing altogether — a silent universal admission that no outcome assertion could
        // see, because every criterion would simply be answered.
        CriterionMatchingAdSetResolver.MaxSetSize.ShouldBeLessThan(CriterionAdMagnitudeDto.Ceiling);
    }

    [Fact]
    public void Dto_RejectsACountBesideEitherRefusal_AndTheTwoRefusalsTogether()
    {
        // The state a truncating implementation would produce. Making it unconstructable is what
        // keeps "exact or absent" a property of the type rather than of everyone's vigilance. The
        // record copy-constructor cannot reach it either: TooBroad is get-only, so
        // `Counted(3) with { TooBroad = true }` does not compile.
        Should.Throw<ArgumentException>(() =>
            new MyMatchingAdCountDto(3, TooBroad: true, NotMaterialised: false));

        // #1681 part 2 added the fourth state, and with it a second refusal the same rule has to
        // cover — plus a combination that claims both refusals at once.
        Should.Throw<ArgumentException>(() =>
            new MyMatchingAdCountDto(0, TooBroad: false, NotMaterialised: true));
        Should.Throw<ArgumentException>(() =>
            new MyMatchingAdCountDto(null, TooBroad: true, NotMaterialised: true));

        MyMatchingAdCountDto.NotAssessed.Count.ShouldBeNull();
        MyMatchingAdCountDto.NotAssessed.TooBroad.ShouldBeFalse();
        MyMatchingAdCountDto.NotAssessed.NotMaterialised.ShouldBeFalse();
        MyMatchingAdCountDto.TooBroadToCount.Count.ShouldBeNull();
        MyMatchingAdCountDto.TooBroadToCount.TooBroad.ShouldBeTrue();
        MyMatchingAdCountDto.TooBroadToCount.NotMaterialised.ShouldBeFalse();
        MyMatchingAdCountDto.NotMaterialisedYet.Count.ShouldBeNull();
        MyMatchingAdCountDto.NotMaterialisedYet.NotMaterialised.ShouldBeTrue();
        MyMatchingAdCountDto.NotMaterialisedYet.TooBroad.ShouldBeFalse();
        MyMatchingAdCountDto.Counted(0).Count.ShouldBe(0);

        // The three no-number states are three DIFFERENT values. Two of them rendering the same way
        // would be a product decision; two of them BEING the same value is a defect, because the FE
        // branches on exactly these members to pick which sentence to show.
        MyMatchingAdCountDto.NotAssessed.ShouldNotBe(MyMatchingAdCountDto.TooBroadToCount);
        MyMatchingAdCountDto.NotAssessed.ShouldNotBe(MyMatchingAdCountDto.NotMaterialisedYet);
        MyMatchingAdCountDto.TooBroadToCount.ShouldNotBe(MyMatchingAdCountDto.NotMaterialisedYet);
    }

    [Fact]
    public void AddApplication_DoesNotRegisterTheResolver_BecauseBothHostsCallIt()
    {
        // The memo is keyed on criterion id while its value is per-user, so it is safe exactly where
        // a scope IS a request. Api's is; a Worker scope is not -- DigestDispatchJob iterates users
        // inside one. AddApplication() is called by BOTH hosts, so the registration lives in the Api
        // composition root and the Worker container cannot resolve the type at all.
        Jobbliggaren.Application.Common.DependencyInjection
            .AddApplication(new ServiceCollection())
            .Any(d => d.ServiceType == typeof(CriterionMatchingAdSetResolver))
            .ShouldBeFalse();
    }

    private GetMyMatchingAdCountForCriterionQueryHandler Sut(
        AppDbContext db, Guid userId, IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new GetMyMatchingAdCountForCriterionQueryHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>(),
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
}
