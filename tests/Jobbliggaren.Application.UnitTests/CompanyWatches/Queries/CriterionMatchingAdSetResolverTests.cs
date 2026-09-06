using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1681 part 2 (ADR 0139) — <see cref="CriterionMatchingAdSetResolver"/> directly: the FOUR arms its
/// closed hierarchy now has, and <see cref="CriterionMatchingAdSetResolver.MatchingBatchAsync"/>, the
/// list-shaped resolution the CTO bound to ONE grading call (senior-cto-advisor 2026-09-06).
///
/// <para>
/// <b>Its own class, beside the handler suites rather than inside one.</b>
/// <c>GetMyMatchingAdCountForCriterionQueryHandlerTests</c> reaches the single-criterion path through
/// a handler because the guard ORDER is what that suite is about; nothing reaches the batch path that
/// way at all, and the arm-separation claims below are about this type rather than about any one
/// surface. Three surfaces consume it, so a collapse here is a collapse on all three.
/// </para>
///
/// <para>
/// <b>Every stubbed port value is one the real adapter emits.</b> <c>CompanyWatchBrowseQuery</c>
/// returns exactly the four <see cref="MaterialisedAdCount"/> / <see cref="MaterialisedAdIds"/>
/// shapes used here — a counted number, a breadth-gate refusal, an absent materialisation, and (for
/// the id set) the LIMIT + 1 refusal — each of them from a state the materialisation job genuinely
/// writes. The pairing of a count with its <c>Saturated</c> flag is the one place a stub COULD invent
/// something the adapter cannot produce, and it is deliberately not exercised here: see
/// <c>GetCriterionAdMagnitudeQueryHandlerTests</c>'s two ceiling-boundary tests, which use the only
/// pairings that occur (CLAUDE.md §5 <c>Tests:</c>).
/// </para>
/// </summary>
public class CriterionMatchingAdSetResolverTests
{
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch =
        Substitute.For<IPerUserJobAdSearchQuery>();
    private readonly ICompanyWatchBrowseQuery _browse = Substitute.For<ICompanyWatchBrowseQuery>();

    private static readonly string[] SniIt = ["62010"];
    private static readonly string[] SniBygg = ["41200"];
    private static readonly string[] KommunStockholm = ["0180"];

    private CriterionMatchingAdSetResolver Sut() => new(_profileBuilder, _perUserSearch, _browse);

    // Non-empty Fast.SsykGroupConceptIds → assessable, so the grade filter IS consulted.
    private static FullCandidateMatchProfile AssessableProfile() =>
        new(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []);

    private static FullCandidateMatchProfile ProfilelessProfile() =>
        new(new CandidateMatchProfile("", [], [], [], []), []);

    // The stubs key on the CRITERION ID (so a batch can hand different criteria different answers)
    // and leave the fingerprint open, so a key mismatch surfaces as a failed Received assertion with
    // the two fingerprints printed rather than as a null-reference crash inside the resolver. The
    // fingerprint half is pinned separately, and exactly, by
    // MatchingAsync_AsksThePortAboutTheCriterionsCURRENTPredicate.
    private void CountFor(Guid criterionId, MaterialisedAdCount answer) =>
        _browse.CountActiveAdsAsync(
                new CompanyWatchCriterionId(criterionId), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(answer);

    private void IdSetFor(Guid criterionId, MaterialisedAdIds answer) =>
        _browse.ListActiveAdIdsAsync(
                new CompanyWatchCriterionId(criterionId), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(answer);

    private void GradesTo(params JobAdId[] matching) =>
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId>(matching));

    private static CompanyWatchCriteriaSpec Spec(string[] sni) =>
        CompanyWatchCriteriaSpec.Create(sni, KommunStockholm).Value;

    // ----- the four arms -------------------------------------------------------------------------

    [Fact]
    public async Task MatchingAsync_NotMaterialised_IsItsOwnArm_NeverSetTooLarge_AndNeverAZero()
    {
        // THE new arm, and the one collapse that would be invisible everywhere else: SetTooLarge and
        // NotMaterialised both render "no number", so a resolver mapping the missing materialisation
        // onto the refusal would look correct in every count assertion in the repo. They give
        // OPPOSITE advice — narrow the watch, versus wait for the next run — so telling a user to
        // narrow a watch that is merely waiting to be counted is advice that cannot work.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(id, MaterialisedAdCount.NotMaterialised);

        var resolved = await Sut().MatchingAsync(id, Spec(SniIt), ct);

        resolved.ShouldBeOfType<CriterionMatchingAds.NotMaterialised>();

        // ...and it is not the refusal, nor a zero-length Resolved.
        resolved.ShouldNotBeOfType<CriterionMatchingAds.SetTooLarge>();
        resolved.ShouldNotBeOfType<CriterionMatchingAds.Resolved>();

        // Nothing was graded: there was no set to grade, and a grading round trip here would be paid
        // for on every criterion of every list between a criterion's creation and the next run.
        await _perUserSearch.DidNotReceiveWithAnyArgs().FilterToMatchingAsync(
            default!, default!, CancellationToken.None);
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public async Task MatchingAsync_TooBroad_IsSetTooLarge_NotNotMaterialised()
    {
        // The other side of the same separation, from the OTHER cause: the criterion's company set
        // exceeded the breadth gate, so the materialisation job refused it. Determinate, and the user
        // can act on it.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(id, MaterialisedAdCount.TooBroad);

        var resolved = await Sut().MatchingAsync(id, Spec(SniIt), ct);

        resolved.ShouldBeOfType<CriterionMatchingAds.SetTooLarge>();
        resolved.ShouldNotBeOfType<CriterionMatchingAds.NotMaterialised>();
    }

    [Fact]
    public async Task MatchingAsync_TheIdSetsOwnRefusal_IsSetTooLarge_NotNotMaterialised()
    {
        // The THIRD way this hierarchy reaches SetTooLarge, and the one the magnitude gate cannot
        // decide: the criterion was materialised and its ad count fits, but the id-set query found
        // one row more than its own bound (the race the port's LIMIT + 1 shape refuses rather than
        // truncates). It must land on the refusal, never on ignorance.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(id, MaterialisedAdCount.Counted(12, saturated: false));
        IdSetFor(id, MaterialisedAdIds.TooManyAds);

        var resolved = await Sut().MatchingAsync(id, Spec(SniIt), ct);

        resolved.ShouldBeOfType<CriterionMatchingAds.SetTooLarge>();

        await _perUserSearch.DidNotReceiveWithAnyArgs().FilterToMatchingAsync(
            default!, default!, CancellationToken.None);
    }

    [Fact]
    public async Task MatchingAsync_NotAssessed_IsNeitherRefusalNorZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(ProfilelessProfile());

        var resolved = await Sut().MatchingAsync(id, Spec(SniIt), ct);

        resolved.ShouldBeOfType<CriterionMatchingAds.NotAssessed>();

        // Assessability first: a caller who has stated no occupation never pays for a read whose
        // result could not be graded — neither half of the port is touched.
        await _browse.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public async Task MatchingAsync_AnEmptyMaterialisedSet_IsAnHonestZero()
    {
        // The fourth arm's degenerate case, and the reason the other three may not borrow it: zero is
        // a MEASUREMENT. A criterion that matches companies with no active ads right now really does
        // have zero matching ads.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(id, MaterialisedAdCount.Counted(0, saturated: false));
        IdSetFor(id, MaterialisedAdIds.Resolved([]));

        var resolved = await Sut().MatchingAsync(id, Spec(SniIt), ct);

        resolved.ShouldBeOfType<CriterionMatchingAds.Resolved>()
            .Matching.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchingAsync_AsksThePortAboutTheCriterionsCURRENTPredicate()
    {
        // The re-keying, asserted where it can fail. Before #1681 part 2 the port took the spec and
        // this assertion read "the port was asked about THIS predicate"; it now reads "about THIS
        // criterion AS IT IS NOW", which is strictly stronger — the id alone would happily answer
        // with a member set computed for a predicate its owner has since edited.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var spec = Spec(SniIt);
        var ad = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(id, MaterialisedAdCount.Counted(1, saturated: false));
        IdSetFor(id, MaterialisedAdIds.Resolved([ad]));
        GradesTo(ad);

        await Sut().MatchingAsync(id, spec, ct);

        await _browse.Received(1).CountActiveAdsAsync(
            new CompanyWatchCriterionId(id),
            CriteriaFingerprint.Of(spec),
            CriterionAdMagnitudeDto.Ceiling,
            Arg.Any<CancellationToken>());

        await _browse.Received(1).ListActiveAdIdsAsync(
            new CompanyWatchCriterionId(id),
            CriteriaFingerprint.Of(spec),
            CriterionMatchingAdSetResolver.MaxSetSize,
            Arg.Any<CancellationToken>());
    }

    // ----- the batch -----------------------------------------------------------------------------

    [Fact]
    public async Task MatchingBatchAsync_GradesTheWholeListInEXACTLYOneCall()
    {
        // BINDING (senior-cto-advisor 2026-09-06): "the list's fan-in is ONE batched
        // FilterToMatchingAsync call over the union, never 20 calls." The call COUNT is the whole
        // assertion — a per-criterion fan-out returns identical numbers, so no outcome assertion
        // anywhere could see the regression. The dominant term (GradeRankExpression) is unmeasured
        // and the route sits on the 300 ms MeListRead budget, which is why this is a bound and not a
        // preference.
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var ad1 = new JobAdId(Guid.NewGuid());
        var ad2 = new JobAdId(Guid.NewGuid());
        var ad3 = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        foreach (var (id, ads) in new[] { (a, ad1), (b, ad2), (c, ad3) })
        {
            CountFor(id, MaterialisedAdCount.Counted(1, saturated: false));
            IdSetFor(id, MaterialisedAdIds.Resolved([ads]));
        }

        GradesTo(ad1, ad3);

        var results = await Sut().MatchingBatchAsync(
            [
                new CriterionToResolve(a, Spec(SniIt)),
                new CriterionToResolve(b, Spec(SniBygg)),
                new CriterionToResolve(c, Spec(SniIt)),
            ],
            ct);

        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());

        // ...and the one call carried the UNION, not one criterion's share of it.
        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyCollection<JobAdId>>(ids =>
                ids.Count == 3 && ids.Contains(ad1) && ids.Contains(ad2) && ids.Contains(ad3)),
            Arg.Any<CancellationToken>());

        // The per-criterion statements stay per-criterion — that half was measured DEARER to batch,
        // so a "symmetry" refactor merging them is a regression, not a tidy-up.
        await _browse.Received(3).ListActiveAdIdsAsync(
            Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task MatchingBatchAsync_AttributesTheGradedSetBackToTheRightCriterion()
    {
        // ONE grading call answers for N criteria, so the attribution step is new surface: a phase-3
        // bug that handed every criterion the whole matching set, or swapped two of them, would leave
        // the call-count assertion above perfectly green while every row on /oversikt showed somebody
        // else's number.
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var onlyA = new JobAdId(Guid.NewGuid());
        var shared = new JobAdId(Guid.NewGuid());
        var onlyB = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(a, MaterialisedAdCount.Counted(2, saturated: false));
        IdSetFor(a, MaterialisedAdIds.Resolved([onlyA, shared]));
        CountFor(b, MaterialisedAdCount.Counted(2, saturated: false));
        IdSetFor(b, MaterialisedAdIds.Resolved([shared, onlyB]));

        // onlyB is graded OUT, so neither criterion's answer is simply its own input list.
        GradesTo(onlyA, shared);

        var results = await Sut().MatchingBatchAsync(
            [new CriterionToResolve(a, Spec(SniIt)), new CriterionToResolve(b, Spec(SniBygg))], ct);

        results[a].ShouldBeOfType<CriterionMatchingAds.Resolved>()
            .Matching.ShouldBe([onlyA, shared]);
        results[b].ShouldBeOfType<CriterionMatchingAds.Resolved>()
            .Matching.ShouldBe([shared]);
    }

    [Fact]
    public async Task MatchingBatchAsync_KeepsEachCriterionsOwnPortOrder()
    {
        // The membership set is a HashSet with its own iteration order, and it is not the port's
        // (published_at DESC, id). Enumerating the SET instead of filtering each criterion's ORDERED
        // list is the mistake the single-criterion path already documents; the batch has its own copy
        // of that step, so it needs its own pin.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var newest = new JobAdId(Guid.NewGuid());
        var middle = new JobAdId(Guid.NewGuid());
        var oldest = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(id, MaterialisedAdCount.Counted(3, saturated: false));
        IdSetFor(id, MaterialisedAdIds.Resolved([newest, middle, oldest]));
        GradesTo(oldest, newest, middle);

        var results = await Sut().MatchingBatchAsync([new CriterionToResolve(id, Spec(SniIt))], ct);

        results[id].ShouldBeOfType<CriterionMatchingAds.Resolved>()
            .Matching.ShouldBe([newest, middle, oldest]);
    }

    [Fact]
    public async Task MatchingBatchAsync_ACriterionThatRefuses_ContributesNoIdsToTheGradingUnion()
    {
        // Phase 1 resolves every criterion's set BEFORE anything is graded, precisely so a refusal
        // costs no grading input. If a refused criterion leaked ids into the union, the batch would
        // pay to grade a set whose answer is thrown away — on the one route the CTO ordered batched
        // because its fan-in cost is the least well characterised.
        var ct = TestContext.Current.CancellationToken;
        var answered = Guid.NewGuid();
        var refused = Guid.NewGuid();
        var unknown = Guid.NewGuid();

        var ad = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(answered, MaterialisedAdCount.Counted(1, saturated: false));
        IdSetFor(answered, MaterialisedAdIds.Resolved([ad]));
        CountFor(refused, MaterialisedAdCount.TooBroad);
        CountFor(unknown, MaterialisedAdCount.NotMaterialised);
        GradesTo(ad);

        var results = await Sut().MatchingBatchAsync(
            [
                new CriterionToResolve(answered, Spec(SniIt)),
                new CriterionToResolve(refused, Spec(SniBygg)),
                new CriterionToResolve(unknown, Spec(SniIt)),
            ],
            ct);

        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyCollection<JobAdId>>(ids => ids.Count == 1 && ids.Contains(ad)),
            Arg.Any<CancellationToken>());

        // ...and the two non-answers keep their OWN identities through the batch, exactly as they do
        // through the single-criterion path.
        results[answered].ShouldBeOfType<CriterionMatchingAds.Resolved>().Matching.ShouldBe([ad]);
        results[refused].ShouldBeOfType<CriterionMatchingAds.SetTooLarge>();
        results[unknown].ShouldBeOfType<CriterionMatchingAds.NotMaterialised>();

        // Neither refusal was probed for a set it cannot have.
        await _browse.Received(1).ListActiveAdIdsAsync(
            Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchingBatchAsync_DeduplicatesTheUnion_WhenTwoCriteriaShareAnAd()
    {
        // Neighbouring watches (adjacent industries, adjacent kommuner) genuinely overlap, so the
        // union is normally smaller than the sum of its parts. Handing the grader the same id twice
        // is paid work with no answer attached.
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var shared = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(a, MaterialisedAdCount.Counted(1, saturated: false));
        IdSetFor(a, MaterialisedAdIds.Resolved([shared]));
        CountFor(b, MaterialisedAdCount.Counted(1, saturated: false));
        IdSetFor(b, MaterialisedAdIds.Resolved([shared]));
        GradesTo(shared);

        var results = await Sut().MatchingBatchAsync(
            [new CriterionToResolve(a, Spec(SniIt)), new CriterionToResolve(b, Spec(SniBygg))], ct);

        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyCollection<JobAdId>>(ids => ids.Count == 1),
            Arg.Any<CancellationToken>());

        // ...and both criteria still get their own copy of the shared match.
        results[a].ShouldBeOfType<CriterionMatchingAds.Resolved>().Matching.ShouldBe([shared]);
        results[b].ShouldBeOfType<CriterionMatchingAds.Resolved>().Matching.ShouldBe([shared]);
    }

    [Fact]
    public async Task MatchingBatchAsync_UnassessableProfile_IsNotAssessedForEveryRow_NeverAZero()
    {
        // Assessability first, list-wide: a user who has stated no occupation gets a nudge on EVERY
        // row rather than a column of zeros, and pays for no port read at all. A batch that fell
        // through to per-criterion resolution here would spend up to 40 statements to produce numbers
        // that cannot be graded.
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(ProfilelessProfile());

        var results = await Sut().MatchingBatchAsync(
            [new CriterionToResolve(a, Spec(SniIt)), new CriterionToResolve(b, Spec(SniBygg))], ct);

        results[a].ShouldBeOfType<CriterionMatchingAds.NotAssessed>();
        results[b].ShouldBeOfType<CriterionMatchingAds.NotAssessed>();

        await _browse.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
        await _perUserSearch.DidNotReceiveWithAnyArgs().FilterToMatchingAsync(
            default!, default!, CancellationToken.None);
    }

    [Fact]
    public async Task MatchingBatchAsync_MemoisesIntoTheSameMapsTheSingleCriterionPathReads()
    {
        // The list handler reads the magnitude per row AFTER the batch, and its comment claims that
        // is "free... so this cannot become a SECOND measurement of the same fact". That claim is
        // about these two maps being shared, and nothing else measured it: an unshared batch would
        // double every port read on the list route while returning identical numbers.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var spec = Spec(SniIt);
        var ad = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(id, MaterialisedAdCount.Counted(1, saturated: false));
        IdSetFor(id, MaterialisedAdIds.Resolved([ad]));
        GradesTo(ad);

        var sut = Sut();
        await sut.MatchingBatchAsync([new CriterionToResolve(id, spec)], ct);

        // Both single-criterion entry points, after the batch.
        (await sut.MagnitudeAsync(id, spec, ct)).Magnitude.ShouldBe(1);
        (await sut.MatchingAsync(id, spec, ct))
            .ShouldBeOfType<CriterionMatchingAds.Resolved>().Matching.ShouldBe([ad]);

        await _browse.Received(1).CountActiveAdsAsync(
            Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await _browse.Received(1).ListActiveAdIdsAsync(
            Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchingBatchAsync_AnEmptyList_AsksNothing_NotEvenForTheProfile()
    {
        // A user with no criteria is the common case on a fresh account. Building the match profile
        // for an empty list is a read nobody can consume.
        var ct = TestContext.Current.CancellationToken;

        (await Sut().MatchingBatchAsync([], ct)).ShouldBeEmpty();

        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
        await _browse.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public async Task MatchingBatchAsync_AsksThePortAboutEachCriterionsOwnCurrentPredicate()
    {
        // A batch pairs ids with predicates, and CriterionToResolve exists because two index-aligned
        // lists are an argument-swap surface — "and here a swap would silently attribute one watch's
        // ads to another". This is what makes that claim measurable: each criterion's id must arrive
        // with the fingerprint of ITS OWN predicate, never the neighbour's.
        var ct = TestContext.Current.CancellationToken;
        var itId = Guid.NewGuid();
        var byggId = Guid.NewGuid();
        var itSpec = Spec(SniIt);
        var byggSpec = Spec(SniBygg);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(itId, MaterialisedAdCount.Counted(0, saturated: false));
        IdSetFor(itId, MaterialisedAdIds.Resolved([]));
        CountFor(byggId, MaterialisedAdCount.Counted(0, saturated: false));
        IdSetFor(byggId, MaterialisedAdIds.Resolved([]));

        await Sut().MatchingBatchAsync(
            [
                new CriterionToResolve(itId, itSpec),
                new CriterionToResolve(byggId, byggSpec),
            ],
            ct);

        // The two fingerprints differ (CriteriaFingerprintTests pins that), so a crossed pairing
        // cannot satisfy both of these.
        await _browse.Received(1).CountActiveAdsAsync(
            new CompanyWatchCriterionId(itId), CriteriaFingerprint.Of(itSpec),
            CriterionAdMagnitudeDto.Ceiling, Arg.Any<CancellationToken>());
        await _browse.Received(1).CountActiveAdsAsync(
            new CompanyWatchCriterionId(byggId), CriteriaFingerprint.Of(byggSpec),
            CriterionAdMagnitudeDto.Ceiling, Arg.Any<CancellationToken>());
    }
}
