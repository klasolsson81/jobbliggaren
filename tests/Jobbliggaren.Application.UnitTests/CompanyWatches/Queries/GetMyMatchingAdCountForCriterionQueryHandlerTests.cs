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
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1656 (b) — <see cref="GetMyMatchingAdCountForCriterionQueryHandler"/>.
///
/// <para>
/// What is pinned is the THREE-ANSWER gate and the ORDER of its two guards. All three answers are
/// rendered by the same line of the UI, and two of them carry no number, so a collapse into "0"
/// would be invisible to every other test: a zero says "nothing matches you" where the truth is
/// "nothing was measured".
/// </para>
///
/// <para>
/// The guard ORDER is asserted too, not just its outcome. Assessability is decided BEFORE the
/// register is touched, so a caller who has stated no occupation never pays for a scan whose result
/// could not be graded — a cost the outcome alone cannot witness.
/// </para>
///
/// <para>
/// InMemory is enough here for the same reason the sibling magnitude handler's tests are: the
/// register is not on <c>IAppDbContext</c> (DPIA C-D4), so this handler reads only the user's own
/// criterion and both joins answer through faked ports. The real SQL and the real grade are proven
/// against Postgres in <c>CompanyWatchBrowseQueryPlanTests</c> and
/// <c>CriterionMatchingAdSetOracleTests</c>.
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

    // An ad magnitude comfortably under the bound: the gate admits, so these arms exercise the
    // path beyond it. The gate's own arm supplies its own number.
    private static readonly CriterionAdMagnitudeDto Fits = new(12, Saturated: false);

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
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobAdId>?>([a1, a2, a3]);
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { a1, a3 });

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value, Fits), ct);

        result.ShouldNotBeNull();
        // Two of three, never three: the count is the GRADED subset, not the criterion's ad set.
        result.Count.ShouldBe(2);
        result.TooBroad.ShouldBeFalse();

        // The bound comes from its single source, and the criterion's own predicate is what is
        // counted (a handler that passed some other spec would count somebody else's watch).
        await _browse.Received(1).ListActiveAdIdsAsync(
            Arg.Is<CompanyWatchCriteriaSpec>(s => s != null
                && s.SniCodes.SequenceEqual(SniIt)
                && s.MunicipalityCodes.SequenceEqual(KommunStockholm)),
            CriterionMatchingAdSet.MaxSetSize,
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
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value, Fits), ct);

        result.ShouldNotBeNull();
        // NOT a zero. A zero would tell the user nothing matches them; the truth is that they have
        // stated no occupation, and the surface renders a nudge instead.
        result.Count.ShouldBeNull();
        result.TooBroad.ShouldBeFalse();

        // The ORDER of the guards, which the outcome alone cannot witness: the register scan is the
        // expensive half and it is skipped entirely for a caller whose result could not be graded.
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default!, default, CancellationToken.None);
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
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
        // The port REFUSES rather than truncating, so there is no prefix here to count.
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobAdId>?>((IReadOnlyList<JobAdId>?)null);

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value, Fits), ct);

        result.ShouldNotBeNull();
        result.TooBroad.ShouldBeTrue();
        // Distinct from the not-assessed arm ONLY by TooBroad, and distinct from every arm by
        // carrying no number: a count beside TooBroad would be a floor sold as an exact figure.
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
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobAdId>?>([]);

        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value, Fits), ct);

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
    public async Task Handle_AnotherUsersCriterion_Is404_AndAsksNothingAboutIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var failedAccess = Substitute.For<IFailedAccessLogger>();

        var result = await Sut(db, Stranger, failedAccess).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value, Fits), ct);

        // null RESPONSE is the authorization signal (→ 404), a different null from the DTO's
        // Count. Unknown and cross-user are the same answer, so the route is never an existence
        // oracle.
        result.ShouldBeNull();

        // Nothing about the stranger's criterion is measured — not even how many ads it has.
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default!, default, CancellationToken.None);
        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MagnitudeAtTheBound_RefusesWithoutTouchingTheRegister()
    {
        // The GATE arm. The number is one the request already measured for the headline, so a
        // criterion too broad to grade costs no register query at all — measured 2026-09-05, the
        // ordered set query it replaces costs seconds on exactly these criteria, and its own LIMIT
        // cannot reduce that (the cost is the join plus the sort over the whole match set).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());

        var atBound = new CriterionAdMagnitudeDto(CriterionMatchingAdSet.MaxSetSize, Saturated: false);
        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value, atBound), ct);

        result.ShouldNotBeNull();
        result.TooBroad.ShouldBeTrue();
        result.Count.ShouldBeNull();

        // The whole point of the gate: no second question to the register.
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default!, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_MagnitudeOneUnderTheBound_IsStillAnswered()
    {
        // The boundary asserted from the other side, so the gate cannot creep into refusing sets it
        // can serve. `>=` means exactly MaxSetSize refuses and one under is answered.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var ad = new JobAdId(Guid.NewGuid());
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _browse.ListActiveAdIdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobAdId>?>([ad]);
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { ad });

        var underBound = new CriterionAdMagnitudeDto(
            CriterionMatchingAdSet.MaxSetSize - 1, Saturated: false);
        var result = await Sut(db, Owner).Handle(
            new GetMyMatchingAdCountForCriterionQuery(criterion.Id.Value, underBound), ct);

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        result.TooBroad.ShouldBeFalse();
    }

    [Fact]
    public void TheGateIsExactOnlyBecauseTheBoundSitsUnderTheCountsCeiling()
    {
        // The invariant the short-circuit rests on. Above the ceiling the count SATURATES, so
        // `magnitude >= MaxSetSize` would be true for every criterion that reached it — including
        // ones whose real ad set fits — and the surface would silently stop answering.
        CriterionMatchingAdSet.MaxSetSize.ShouldBeLessThanOrEqualTo(CriterionAdMagnitudeDto.Ceiling);
    }

    [Fact]
    public void Dto_RejectsACountBesideTooBroad()
    {
        // The state a truncating implementation would produce. Making it unconstructable is what
        // keeps "exact or absent" a property of the type rather than of everyone's vigilance.
        Should.Throw<ArgumentException>(() => new MyMatchingAdCountDto(3, TooBroad: true));

        MyMatchingAdCountDto.NotAssessed.Count.ShouldBeNull();
        MyMatchingAdCountDto.NotAssessed.TooBroad.ShouldBeFalse();
        MyMatchingAdCountDto.TooBroadToCount.Count.ShouldBeNull();
        MyMatchingAdCountDto.TooBroadToCount.TooBroad.ShouldBeTrue();
        MyMatchingAdCountDto.Counted(0).Count.ShouldBe(0);
    }

    private GetMyMatchingAdCountForCriterionQueryHandler Sut(
        AppDbContext db, Guid userId, IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new GetMyMatchingAdCountForCriterionQueryHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>(),
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
}
