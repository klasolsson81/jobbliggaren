using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;
using Jobbliggaren.Domain.JobAds;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries;

/// <summary>
/// F4-13 → F4-15 (ADR 0076 Decision 5/6) — the batch overlay handler, upgraded to FULL.
/// It now builds the current user's FULL CV-skill profile
/// (<see cref="IMatchProfileBuilder.BuildFullFromCvSkillsAsync"/>, the TAG path — DEK-warmed),
/// FULL-scores all requested ads in ONE round-trip
/// (<see cref="IMatchScorer.ScoreFullBatchAsync"/>, zero N+1), and grades each via the
/// deterministic <see cref="MatchGradeCalculator"/> over the EMBEDDED Fast tuple (the grade
/// ladder is unchanged — F4-15 adds NO visible MatchGrade member, ceilinged at Strong). The
/// entry now carries the three new FULL verdicts (SkillOverlap, MustHaveCoverage,
/// NiceToHaveCoverage) ALONGSIDE the four Fast verdicts. Honest + anonymous-tolerant: no
/// user / no stated occupation (the gate reads <c>profile.Fast.SsykGroupConceptIds</c>) →
/// empty map; only ads earning a positive grade appear.
/// <para>
/// <b>Why hand-rolled fakes, not NSubstitute, for the two ValueTask-returning ports:</b>
/// a ValueTask return set via NSubstitute trips CA2012 at the call-setup site in THIS
/// project; a tiny class returning <c>new ValueTask&lt;T&gt;(...)</c> straight from the body
/// is the clean, warning-free form. <see cref="ICurrentUser"/> is a plain <c>Guid?</c>
/// (no ValueTask) so NSubstitute is fine there.
/// </para>
/// RED until the handler is upgraded to BuildFullFromCvSkillsAsync + ScoreFullBatchAsync
/// and JobAdMatchEntryDto widens to the seven verdicts.
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
    // Hand-rolled ValueTask fakes (CA2012-safe) with call counters. The builder now
    // serves the FULL (CV-skill) profile; the scorer now serves the FULL batch.
    // ---------------------------------------------------------------
    private sealed class FakeProfileBuilder(FullCandidateMatchProfile fullProfile) : IMatchProfileBuilder
    {
        public int FastCallCount { get; private set; }
        public int TopSkillsCallCount { get; private set; }
        public int CvSkillsCallCount { get; private set; }

        // Unchanged F4-12 method — never called by the batch handler.
        public ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(CancellationToken cancellationToken)
        {
            FastCallCount++;
            return new ValueTask<CandidateMatchProfile>(fullProfile.Fast);
        }

        // SORT path — not used by the TAG batch handler.
        public ValueTask<FullCandidateMatchProfile> BuildFullFromTopSkillsAsync(CancellationToken cancellationToken)
        {
            TopSkillsCallCount++;
            return new ValueTask<FullCandidateMatchProfile>(fullProfile);
        }

        // TAG path — the one the batch handler uses (DEK-warmed CV-skill profile).
        public ValueTask<FullCandidateMatchProfile> BuildFullFromCvSkillsAsync(CancellationToken cancellationToken)
        {
            CvSkillsCallCount++;
            return new ValueTask<FullCandidateMatchProfile>(fullProfile);
        }
    }

    private sealed class FakeScorer(IReadOnlyDictionary<JobAdId, FullMatchScore> scores) : IMatchScorer
    {
        public int FullBatchCallCount { get; private set; }
        public IReadOnlyList<JobAdId>? LastBatchIds { get; private set; }

        public ValueTask<IReadOnlyDictionary<JobAdId, FullMatchScore>> ScoreFullBatchAsync(
            IReadOnlyList<JobAdId> jobAdIds, FullCandidateMatchProfile profile,
            CancellationToken cancellationToken)
        {
            FullBatchCallCount++;
            LastBatchIds = jobAdIds;
            return new ValueTask<IReadOnlyDictionary<JobAdId, FullMatchScore>>(scores);
        }

        // The Fast batch must NOT be used by the upgraded handler.
        public ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>> ScoreBatchAsync(
            IReadOnlyList<JobAdId> jobAdIds, CandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreBatchAsync ska inte anropas av den FULL-uppgraderade handlern.");

        public ValueTask<MatchScore> ScoreAsync(
            JobAdId jobAdId, CandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreAsync ska inte anropas av batch-handlern.");

        public ValueTask<FullMatchScore> ScoreFullAsync(
            JobAdId jobAdId, FullCandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreFullAsync (single) ska inte anropas av batch-handlern.");
    }

    // ---------------------------------------------------------------
    // Profile/score builders.
    // ---------------------------------------------------------------
    private static FullCandidateMatchProfile FullProfileWithOccupation(params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: ["grp_12345"],
                PreferredRegionConceptIds: ["region_AB"],
                PreferredEmploymentTypeConceptIds: []),
            cvSkillConceptIds);

    private static FullCandidateMatchProfile EmptyFullProfile() =>
        new(new CandidateMatchProfile(string.Empty, [], [], []), []);

    private static MatchDimension Dim(MatchDimensionVerdict v) => new(v, [], []);
    private static MatchDimension Dim(MatchDimensionVerdict v, IReadOnlyList<string> matched) => new(v, matched, []);

    private static FullMatchScore FullScoreOf(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill = MatchDimensionVerdict.NotAssessed,
        MatchDimensionVerdict mustHave = MatchDimensionVerdict.NotAssessed,
        MatchDimensionVerdict niceToHave = MatchDimensionVerdict.NotAssessed) =>
        new(
            Fast: new MatchScore(
                Dim(ssyk), Dim(MatchDimensionVerdict.NotAssessed), Dim(region), Dim(employment)),
            SkillOverlap: Dim(skill),
            MustHaveCoverage: Dim(mustHave),
            NiceToHaveCoverage: Dim(niceToHave));

    private GetJobAdMatchBatchQueryHandler CreateHandler(
        FakeProfileBuilder builder, FakeScorer scorer, ICurrentUser? user = null) =>
        new(builder, scorer, user ?? _currentUser);

    // =================================================================
    // Anonymous → empty, builder + scorer never called (gate unchanged)
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnEmptyEntries_WhenUserIsAnonymous()
    {
        var ct = TestContext.Current.CancellationToken;
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var builder = new FakeProfileBuilder(FullProfileWithOccupation());
        var scorer = new FakeScorer(new Dictionary<JobAdId, FullMatchScore>());
        var sut = CreateHandler(builder, scorer, anon);

        var result = await sut.Handle(new GetJobAdMatchBatchQuery([Guid.NewGuid()]), ct);

        result.Entries.ShouldBeEmpty();
        builder.CvSkillsCallCount.ShouldBe(0);
        scorer.FullBatchCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyEntries_WhenJobAdIdsIsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(FullProfileWithOccupation());
        var scorer = new FakeScorer(new Dictionary<JobAdId, FullMatchScore>());
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(new GetJobAdMatchBatchQuery([]), ct);

        result.Entries.ShouldBeEmpty();
        builder.CvSkillsCallCount.ShouldBe(0);
        scorer.FullBatchCallCount.ShouldBe(0);
    }

    // =================================================================
    // Authed but no stated occupation → empty, scorer NOT called. The gate
    // now reads profile.Fast.SsykGroupConceptIds.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnEmptyEntries_WhenProfileFastHasNoOccupationGroups()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(EmptyFullProfile());
        var scorer = new FakeScorer(new Dictionary<JobAdId, FullMatchScore>());
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(new GetJobAdMatchBatchQuery([Guid.NewGuid()]), ct);

        result.Entries.ShouldBeEmpty();
        builder.CvSkillsCallCount.ShouldBe(1);      // FULL profile WAS built (TAG path)
        scorer.FullBatchCallCount.ShouldBe(0);      // but the batch query was short-circuited
    }

    // =================================================================
    // Authed with occupation → grade from the FULL score (F4-16, ADR 0076 Amendment
    // (b) §1 — the visible chip is no longer ceilinged at Strong): a Strong Fast match
    // WITH CV-skill overlap is promoted to the golden Top rung; a Strong Fast match
    // WITHOUT skill overlap stays Strong; sub-Strong grades pass through. Include only
    // graded; entry carries the four Fast verdicts AND the three FULL verdicts.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldGradeFromFullScore_PromotingStrongWithSkillOverlapToTop()
    {
        var ct = TestContext.Current.CancellationToken;
        var topAd = new JobAdId(Guid.NewGuid());          // occ+region+employment Match + skill Match → Top
        var strongAd = new JobAdId(Guid.NewGuid());       // occ+region+employment Match, skill NotAssessed → Strong
        var goodAd = new JobAdId(Guid.NewGuid());          // occ + region Match → Good
        var gatedAd = new JobAdId(Guid.NewGuid());         // occ NoMatch → null → omitted

        var scores = new Dictionary<JobAdId, FullMatchScore>
        {
            [topAd] = FullScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.Match,
                skill: MatchDimensionVerdict.Match),
            [strongAd] = FullScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.Match),
            [goodAd] = FullScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.NotAssessed),
            [gatedAd] = FullScoreOf(
                MatchDimensionVerdict.NoMatch, MatchDimensionVerdict.Match, MatchDimensionVerdict.Match),
        };
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-x"));
        var scorer = new FakeScorer(scores);
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(
            new GetJobAdMatchBatchQuery([topAd.Value, strongAd.Value, goodAd.Value, gatedAd.Value]), ct);

        result.Entries.Count.ShouldBe(3);
        result.Entries.ShouldNotContainKey(gatedAd.Value);

        // F4-16: the visible grade is the FULL grade. A Strong Fast match with CV-skill
        // overlap is the golden Top; without overlap it stays Strong; Good passes through.
        result.Entries[topAd.Value].Grade.ShouldBe(MatchGrade.Top);
        result.Entries[strongAd.Value].Grade.ShouldBe(MatchGrade.Strong);
        result.Entries[goodAd.Value].Grade.ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public async Task Handle_ShouldCarryAllSevenDimensionVerdicts_OnAGradedEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        var ad = new JobAdId(Guid.NewGuid());
        var scores = new Dictionary<JobAdId, FullMatchScore>
        {
            [ad] = FullScoreOf(
                ssyk: MatchDimensionVerdict.Match,
                region: MatchDimensionVerdict.Match,
                employment: MatchDimensionVerdict.NotAssessed,
                skill: MatchDimensionVerdict.Partial,
                mustHave: MatchDimensionVerdict.Match,
                niceToHave: MatchDimensionVerdict.NotAssessed),
        };
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-x"));
        var scorer = new FakeScorer(scores);
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(new GetJobAdMatchBatchQuery([ad.Value]), ct);

        var entry = result.Entries[ad.Value];
        entry.Grade.ShouldBe(MatchGrade.Good);
        // Four Fast verdicts.
        entry.SsykOverlap.ShouldBe(MatchDimensionVerdict.Match);
        entry.TitleSimilarity.ShouldBe(MatchDimensionVerdict.NotAssessed);
        entry.RegionFit.ShouldBe(MatchDimensionVerdict.Match);
        entry.EmploymentFit.ShouldBe(MatchDimensionVerdict.NotAssessed);
        // Three FULL verdicts (the F4-15 additions).
        entry.SkillOverlap.ShouldBe(MatchDimensionVerdict.Partial);
        entry.MustHaveCoverage.ShouldBe(MatchDimensionVerdict.Match);
        entry.NiceToHaveCoverage.ShouldBe(MatchDimensionVerdict.NotAssessed);
    }

    [Fact]
    public async Task Handle_ShouldCallFullBatchScorerExactlyOnce_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var ad = new JobAdId(Guid.NewGuid());
        var scores = new Dictionary<JobAdId, FullMatchScore>
        {
            [ad] = FullScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.NotAssessed, MatchDimensionVerdict.NotAssessed),
        };
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-x"));
        var scorer = new FakeScorer(scores);
        var sut = CreateHandler(builder, scorer);

        await sut.Handle(new GetJobAdMatchBatchQuery([ad.Value]), ct);

        scorer.FullBatchCallCount.ShouldBe(1);       // ONE round-trip (zero N+1)
        builder.CvSkillsCallCount.ShouldBe(1);       // TAG path — full CV-skill profile
        scorer.LastBatchIds.ShouldNotBeNull();
        scorer.LastBatchIds!.ShouldContain(ad);
    }

    [Fact]
    public async Task Handle_ShouldDeduplicateRequestedIds_BeforeScoring()
    {
        var ct = TestContext.Current.CancellationToken;
        var ad = new JobAdId(Guid.NewGuid());
        var scores = new Dictionary<JobAdId, FullMatchScore>
        {
            [ad] = FullScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.NotAssessed, MatchDimensionVerdict.NotAssessed),
        };
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-x"));
        var scorer = new FakeScorer(scores);
        var sut = CreateHandler(builder, scorer);

        await sut.Handle(new GetJobAdMatchBatchQuery([ad.Value, ad.Value, ad.Value]), ct);

        scorer.LastBatchIds.ShouldNotBeNull();
        scorer.LastBatchIds!.Count.ShouldBe(1);
    }

    // =================================================================
    // Determinism — same input twice → equal Entries (incl. the new verdicts)
    // =================================================================

    [Fact]
    public async Task Handle_ShouldProduceEqualEntries_WhenCalledTwiceWithSameInput()
    {
        var ct = TestContext.Current.CancellationToken;
        var ad1 = new JobAdId(Guid.NewGuid());
        var ad2 = new JobAdId(Guid.NewGuid());
        var scores = new Dictionary<JobAdId, FullMatchScore>
        {
            [ad1] = FullScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.Match,
                skill: MatchDimensionVerdict.Match),
            [ad2] = FullScoreOf(
                MatchDimensionVerdict.Match, MatchDimensionVerdict.Match, MatchDimensionVerdict.NotAssessed,
                mustHave: MatchDimensionVerdict.Partial),
        };

        var query = new GetJobAdMatchBatchQuery([ad1.Value, ad2.Value]);

        var first = await CreateHandler(
            new FakeProfileBuilder(FullProfileWithOccupation("skill-x")), new FakeScorer(scores)).Handle(query, ct);
        var second = await CreateHandler(
            new FakeProfileBuilder(FullProfileWithOccupation("skill-x")), new FakeScorer(scores)).Handle(query, ct);

        first.Entries.Count.ShouldBe(second.Entries.Count);
        foreach (var (id, entry) in first.Entries)
        {
            second.Entries.ShouldContainKey(id);
            second.Entries[id].Grade.ShouldBe(entry.Grade);
            second.Entries[id].SsykOverlap.ShouldBe(entry.SsykOverlap);
            second.Entries[id].RegionFit.ShouldBe(entry.RegionFit);
            second.Entries[id].EmploymentFit.ShouldBe(entry.EmploymentFit);
            second.Entries[id].TitleSimilarity.ShouldBe(entry.TitleSimilarity);
            second.Entries[id].SkillOverlap.ShouldBe(entry.SkillOverlap);
            second.Entries[id].MustHaveCoverage.ShouldBe(entry.MustHaveCoverage);
            second.Entries[id].NiceToHaveCoverage.ShouldBe(entry.NiceToHaveCoverage);
        }
    }
}
