using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;
using Jobbliggaren.Domain.JobAds;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries;

/// <summary>
/// F4-13 (ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 A1/B2/C2a) — the batch
/// overlay handler composes the profile builder (SSOT), the batch scorer (zero N+1) and
/// the deterministic <see cref="MatchGradeCalculator"/>. Honest + anonymous-tolerant: no
/// user / no stated occupation → empty map; only ads earning a positive grade appear.
/// <para>
/// <b>Why hand-rolled fakes, not NSubstitute, for the two ValueTask-returning ports:</b>
/// setting up a ValueTask return with NSubstitute (<c>.Returns(new ValueTask&lt;T&gt;(x))</c>)
/// trips analyzer CA2012 ("do not store/await a ValueTask multiple times") at the
/// call-setup site in THIS project. A tiny class returning <c>new ValueTask&lt;T&gt;(...)</c>
/// straight from the method body is the clean, warning-free form. <see cref="ICurrentUser"/>
/// is a plain <c>Guid?</c> property (no ValueTask) so NSubstitute is fine there.
/// </para>
/// </summary>
public class GetJobAdMatchBatchQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetJobAdMatchBatchQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    // ---------------------------------------------------------------
    // Hand-rolled ValueTask fakes (CA2012-safe) with call counters so we can
    // assert short-circuit behaviour (the gate must not query when it bails).
    // ---------------------------------------------------------------
    private sealed class FakeProfileBuilder(CandidateMatchProfile profile) : IMatchProfileBuilder
    {
        public int CallCount { get; private set; }

        public ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            return new ValueTask<CandidateMatchProfile>(profile);
        }
    }

    private sealed class FakeScorer(IReadOnlyDictionary<JobAdId, MatchScore> scores) : IMatchScorer
    {
        public int BatchCallCount { get; private set; }
        public IReadOnlyList<JobAdId>? LastBatchIds { get; private set; }

        public ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>> ScoreBatchAsync(
            IReadOnlyList<JobAdId> jobAdIds, CandidateMatchProfile profile,
            CancellationToken cancellationToken)
        {
            BatchCallCount++;
            LastBatchIds = jobAdIds;
            return new ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>>(scores);
        }

        // Unused by the batch handler — never called; return a completed dummy.
        public ValueTask<MatchScore> ScoreAsync(
            JobAdId jobAdId, CandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreAsync ska inte anropas av batch-handlern.");

        public ValueTask<FullMatchScore> ScoreFullAsync(
            JobAdId jobAdId, FullCandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreFullAsync ska inte anropas av batch-handlern.");
    }

    // ---------------------------------------------------------------
    // Profile/score builders.
    // ---------------------------------------------------------------
    private static CandidateMatchProfile ProfileWithOccupation() =>
        new(
            Title: string.Empty,
            SsykGroupConceptIds: ["grp_12345"],
            PreferredRegionConceptIds: ["region_AB"],
            PreferredEmploymentTypeConceptIds: []);

    private static CandidateMatchProfile EmptyProfile() =>
        new(string.Empty, [], [], []);

    private static MatchDimension Dim(MatchDimensionVerdict v) => new(v, [], []);

    private static MatchScore ScoreOf(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment) =>
        new(Dim(ssyk), Dim(MatchDimensionVerdict.NotAssessed), Dim(region), Dim(employment));

    private GetJobAdMatchBatchQueryHandler CreateHandler(
        FakeProfileBuilder builder, FakeScorer scorer, ICurrentUser? user = null) =>
        new(builder, scorer, user ?? _currentUser);

    // =================================================================
    // Anonymous → empty, builder + scorer never called (no UserId)
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnEmptyEntries_WhenUserIsAnonymous()
    {
        var ct = TestContext.Current.CancellationToken;
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var scorer = new FakeScorer(new Dictionary<JobAdId, MatchScore>());
        var sut = CreateHandler(builder, scorer, anon);

        var result = await sut.Handle(
            new GetJobAdMatchBatchQuery([Guid.NewGuid()]), ct);

        result.Entries.ShouldBeEmpty();
        builder.CallCount.ShouldBe(0);
        scorer.BatchCallCount.ShouldBe(0);
    }

    // =================================================================
    // Empty id list → empty, builder + scorer never called
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnEmptyEntries_WhenJobAdIdsIsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var scorer = new FakeScorer(new Dictionary<JobAdId, MatchScore>());
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(new GetJobAdMatchBatchQuery([]), ct);

        result.Entries.ShouldBeEmpty();
        builder.CallCount.ShouldBe(0);
        scorer.BatchCallCount.ShouldBe(0);
    }

    // =================================================================
    // Authed but no stated occupation → empty, scorer NOT called
    // (the occupation gate short-circuits before the batch query)
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnEmptyEntries_WhenProfileHasNoOccupationGroups()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(EmptyProfile());
        var scorer = new FakeScorer(new Dictionary<JobAdId, MatchScore>());
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(
            new GetJobAdMatchBatchQuery([Guid.NewGuid()]), ct);

        result.Entries.ShouldBeEmpty();
        builder.CallCount.ShouldBe(1);          // profile WAS built
        scorer.BatchCallCount.ShouldBe(0);      // but the batch query was short-circuited
    }

    // =================================================================
    // Authed with occupation prefs → map each ad through the calculator,
    // include ONLY non-null grades, carry the four verdicts + grade
    // =================================================================

    [Fact]
    public async Task Handle_ShouldIncludeOnlyGradedAds_WhenSomeScoreBelowTheGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var strongAd = new JobAdId(Guid.NewGuid());   // occ Match + both confirmed → Strong
        var goodAd = new JobAdId(Guid.NewGuid());      // occ Match + 1 confirmed → Good
        var gatedAd = new JobAdId(Guid.NewGuid());     // occ NoMatch → null → omitted

        var scores = new Dictionary<JobAdId, MatchScore>
        {
            [strongAd] = ScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.Match),
            [goodAd] = ScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.NotAssessed),
            [gatedAd] = ScoreOf(
                MatchDimensionVerdict.NoMatch, MatchDimensionVerdict.Match, MatchDimensionVerdict.Match),
        };
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var scorer = new FakeScorer(scores);
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(
            new GetJobAdMatchBatchQuery([strongAd.Value, goodAd.Value, gatedAd.Value]), ct);

        result.Entries.Count.ShouldBe(2);
        result.Entries.ShouldContainKey(strongAd.Value);
        result.Entries.ShouldContainKey(goodAd.Value);
        result.Entries.ShouldNotContainKey(gatedAd.Value);

        result.Entries[strongAd.Value].Grade.ShouldBe(MatchGrade.Strong);
        result.Entries[goodAd.Value].Grade.ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public async Task Handle_ShouldCarryAllFourDimensionVerdicts_OnAGradedEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        var ad = new JobAdId(Guid.NewGuid());
        var scores = new Dictionary<JobAdId, MatchScore>
        {
            [ad] = ScoreOf(
                MatchDimensionVerdict.Match,
                MatchDimensionVerdict.Match,
                MatchDimensionVerdict.NotAssessed),
        };
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var scorer = new FakeScorer(scores);
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(new GetJobAdMatchBatchQuery([ad.Value]), ct);

        var entry = result.Entries[ad.Value];
        entry.Grade.ShouldBe(MatchGrade.Good);
        entry.SsykOverlap.ShouldBe(MatchDimensionVerdict.Match);
        entry.RegionFit.ShouldBe(MatchDimensionVerdict.Match);
        entry.EmploymentFit.ShouldBe(MatchDimensionVerdict.NotAssessed);
        entry.TitleSimilarity.ShouldBe(MatchDimensionVerdict.NotAssessed);
    }

    [Fact]
    public async Task Handle_ShouldCallBatchScorerExactlyOnce_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var ad = new JobAdId(Guid.NewGuid());
        var scores = new Dictionary<JobAdId, MatchScore>
        {
            [ad] = ScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.NotAssessed, MatchDimensionVerdict.NotAssessed),
        };
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var scorer = new FakeScorer(scores);
        var sut = CreateHandler(builder, scorer);

        await sut.Handle(new GetJobAdMatchBatchQuery([ad.Value]), ct);

        scorer.BatchCallCount.ShouldBe(1);       // ONE round-trip (zero N+1)
        scorer.LastBatchIds.ShouldNotBeNull();
        scorer.LastBatchIds!.ShouldContain(ad);
    }

    [Fact]
    public async Task Handle_ShouldDeduplicateRequestedIds_BeforeScoring()
    {
        var ct = TestContext.Current.CancellationToken;
        var ad = new JobAdId(Guid.NewGuid());
        var scores = new Dictionary<JobAdId, MatchScore>
        {
            [ad] = ScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.NotAssessed, MatchDimensionVerdict.NotAssessed),
        };
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var scorer = new FakeScorer(scores);
        var sut = CreateHandler(builder, scorer);

        // Same id three times — the handler distincts before constructing JobAdIds.
        await sut.Handle(new GetJobAdMatchBatchQuery([ad.Value, ad.Value, ad.Value]), ct);

        scorer.LastBatchIds.ShouldNotBeNull();
        scorer.LastBatchIds!.Count.ShouldBe(1);
    }

    // =================================================================
    // Determinism — same input twice → equal Entries
    // =================================================================

    [Fact]
    public async Task Handle_ShouldProduceEqualEntries_WhenCalledTwiceWithSameInput()
    {
        var ct = TestContext.Current.CancellationToken;
        var ad1 = new JobAdId(Guid.NewGuid());
        var ad2 = new JobAdId(Guid.NewGuid());
        var scores = new Dictionary<JobAdId, MatchScore>
        {
            [ad1] = ScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.Match),
            [ad2] = ScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.NotAssessed),
        };

        var query = new GetJobAdMatchBatchQuery([ad1.Value, ad2.Value]);

        var first = await CreateHandler(
            new FakeProfileBuilder(ProfileWithOccupation()), new FakeScorer(scores)).Handle(query, ct);
        var second = await CreateHandler(
            new FakeProfileBuilder(ProfileWithOccupation()), new FakeScorer(scores)).Handle(query, ct);

        first.Entries.Count.ShouldBe(second.Entries.Count);
        foreach (var (id, entry) in first.Entries)
        {
            second.Entries.ShouldContainKey(id);
            second.Entries[id].Grade.ShouldBe(entry.Grade);
            second.Entries[id].SsykOverlap.ShouldBe(entry.SsykOverlap);
            second.Entries[id].RegionFit.ShouldBe(entry.RegionFit);
            second.Entries[id].EmploymentFit.ShouldBe(entry.EmploymentFit);
            second.Entries[id].TitleSimilarity.ShouldBe(entry.TitleSimilarity);
        }
    }
}
