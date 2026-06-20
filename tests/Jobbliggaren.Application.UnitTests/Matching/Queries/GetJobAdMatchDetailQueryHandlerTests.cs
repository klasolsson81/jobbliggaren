using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;
using Jobbliggaren.Domain.JobAds;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries;

/// <summary>
/// F4-16 (ADR 0076 Amendment (b); CTO 2026-06-20 D3/D5) — the single-ad MODAL detail
/// handler. It builds the current user's FULL CV-skill profile
/// (<see cref="IMatchProfileBuilder.BuildFullFromCvSkillsAsync"/>, the DEK-warmed,
/// fail-closed verdict-bearing path), FULL-scores ONE ad
/// (<see cref="IMatchScorer.ScoreFullAsync"/> — single-ad THROW semantics, NOT the
/// silent-omit batch), grades it via the new <see cref="MatchGradeCalculator"/> Full
/// overload (golden rung = <see cref="MatchGrade.Top"/>), and projects all SEVEN
/// dimensions to <see cref="MatchDimensionDetailDto"/> rows carrying verdict +
/// matched[] + missing[] (the modal's "what you're missing for this ad" evidence).
/// <para>
/// <b>Modal altitude differs from the batch handler in two bound ways (CTO D3):</b>
/// (1) it does NOT short-circuit on an empty occupation — it returns an honest DTO with
/// a <c>null</c> grade + per-dimension rows so the modal renders the breakdown + signpost;
/// (2) it uses the single-ad <see cref="IMatchScorer.ScoreFullAsync"/> that THROWS
/// <see cref="NotFoundException"/> for a missing ad (propagated → 404), never the batch.
/// Anonymous (no <c>UserId</c>) → <c>null</c> (no match section; scorer/builder never called).
/// </para>
/// <para>
/// <b>Why hand-rolled fakes, not NSubstitute, for the two ValueTask-returning ports:</b>
/// a ValueTask return set via NSubstitute trips CA2012 at the call-setup site in THIS
/// project; a tiny class returning <c>new ValueTask&lt;T&gt;(...)</c> straight from the body
/// is the clean, warning-free form (parity <see cref="GetJobAdMatchBatchQueryHandlerTests"/>).
/// <see cref="ICurrentUser"/> is a plain <c>Guid?</c> so NSubstitute is fine there.
/// </para>
/// RED until <c>GetJobAdMatchDetailQuery(Handler)</c> + <c>JobAdMatchDetailDto</c> /
/// <c>MatchDimensionDetailDto</c> + the <c>MatchGrade.Top</c> member + the
/// <c>Grade(FullMatchScore)</c> overload exist.
/// </summary>
public class GetJobAdMatchDetailQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetJobAdMatchDetailQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    // ---------------------------------------------------------------
    // Hand-rolled ValueTask fakes (CA2012-safe) with call counters. The builder serves
    // the FULL (CV-skill) profile; the scorer serves the SINGLE-ad full score, with a
    // throw-mode for the not-found / fail-closed paths.
    // ---------------------------------------------------------------
    private sealed class FakeProfileBuilder(FullCandidateMatchProfile fullProfile) : IMatchProfileBuilder
    {
        private readonly Exception? _throwOnCvSkills;

        public FakeProfileBuilder(Exception throwOnCvSkills)
            : this(EmptyFullProfile()) => _throwOnCvSkills = throwOnCvSkills;

        public int CvSkillsCallCount { get; private set; }

        // Unchanged F4-12 method — never called by the modal handler.
        public ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException("BuildFromPreferencesAsync ska inte anropas av modal-handlern.");

        // SORT path — not used by the modal handler.
        public ValueTask<FullCandidateMatchProfile> BuildFullFromTopSkillsAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException("BuildFullFromTopSkillsAsync ska inte anropas av modal-handlern.");

        // The modal path — full CV-skill profile (DEK-warmed, fail-closed).
        public ValueTask<FullCandidateMatchProfile> BuildFullFromCvSkillsAsync(CancellationToken cancellationToken)
        {
            CvSkillsCallCount++;
            if (_throwOnCvSkills is not null)
                throw _throwOnCvSkills; // fail-closed — DEK/KMS failure propagates, never swallowed
            return new ValueTask<FullCandidateMatchProfile>(fullProfile);
        }
    }

    private sealed class FakeScorer : IMatchScorer
    {
        private readonly FullMatchScore? _score;
        private readonly Exception? _throwOnScoreFull;

        public FakeScorer(FullMatchScore score) => _score = score;
        public FakeScorer(Exception throwOnScoreFull) => _throwOnScoreFull = throwOnScoreFull;

        public int ScoreFullCallCount { get; private set; }
        public JobAdId? LastScoredId { get; private set; }

        public ValueTask<FullMatchScore> ScoreFullAsync(
            JobAdId jobAdId, FullCandidateMatchProfile profile, CancellationToken cancellationToken)
        {
            ScoreFullCallCount++;
            LastScoredId = jobAdId;
            if (_throwOnScoreFull is not null)
                throw _throwOnScoreFull; // NotFoundException for a missing ad → propagate
            return new ValueTask<FullMatchScore>(_score!);
        }

        // The modal handler must NOT touch any of the batch / Fast methods.
        public ValueTask<MatchScore> ScoreAsync(
            JobAdId jobAdId, CandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreAsync ska inte anropas av modal-handlern.");

        public ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>> ScoreBatchAsync(
            IReadOnlyList<JobAdId> jobAdIds, CandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreBatchAsync ska inte anropas av modal-handlern.");

        public ValueTask<IReadOnlyDictionary<JobAdId, FullMatchScore>> ScoreFullBatchAsync(
            IReadOnlyList<JobAdId> jobAdIds, FullCandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "ScoreFullBatchAsync (batch) ska inte anropas av single-ad modal-handlern (CTO D3 — döda inte batch-kontraktet för en enskild fråga).");
    }

    // ---------------------------------------------------------------
    // Profile / score builders.
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

    private static MatchDimension Dim(
        MatchDimensionVerdict v,
        IReadOnlyList<string>? matched = null,
        IReadOnlyList<string>? missing = null) =>
        new(v, matched ?? [], missing ?? []);

    private GetJobAdMatchDetailQueryHandler CreateHandler(
        FakeProfileBuilder builder, FakeScorer scorer, ICurrentUser? user = null) =>
        new(builder, scorer, user ?? _currentUser);

    // =================================================================
    // Anonymous → null, builder + scorer never called (the modal is auth-gated)
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIsAnonymous()
    {
        var ct = TestContext.Current.CancellationToken;
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var builder = new FakeProfileBuilder(FullProfileWithOccupation());
        var scorer = new FakeScorer(StrongScore());
        var sut = CreateHandler(builder, scorer, anon);

        var result = await sut.Handle(new GetJobAdMatchDetailQuery(Guid.NewGuid()), ct);

        result.ShouldBeNull();
        builder.CvSkillsCallCount.ShouldBe(0);
        scorer.ScoreFullCallCount.ShouldBe(0);
    }

    // =================================================================
    // Happy path — Strong + SkillOverlap Match → grade Top; the 7 dimension rows carry
    // the right verdict + matched/missing strings (assert several flow through verbatim).
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnTopGradeWithAllSevenDimensionRows_WhenStrongAndSkillMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobAdId = Guid.NewGuid();
        var score = new FullMatchScore(
            Fast: new MatchScore(
                SsykOverlap: Dim(MatchDimensionVerdict.Match, matched: ["Systemutvecklare"]),
                TitleSimilarity: Dim(MatchDimensionVerdict.NotAssessed),
                RegionFit: Dim(MatchDimensionVerdict.Match, matched: ["Stockholm"]),
                EmploymentFit: Dim(MatchDimensionVerdict.Match, matched: ["Tillsvidare"])),
            SkillOverlap: Dim(MatchDimensionVerdict.Match, matched: ["C#", "SQL"], missing: ["Kubernetes"]),
            MustHaveCoverage: Dim(MatchDimensionVerdict.Match, matched: ["C#"], missing: []),
            NiceToHaveCoverage: Dim(MatchDimensionVerdict.NotAssessed));
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-csharp"));
        var scorer = new FakeScorer(score);
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(new GetJobAdMatchDetailQuery(jobAdId), ct);

        result.ShouldNotBeNull();
        result!.Grade.ShouldBe(MatchGrade.Top); // Strong base + SkillOverlap Match → golden

        // Four Fast rows.
        result.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        result.SsykOverlap.Matched.ShouldContain("Systemutvecklare");
        result.TitleSimilarity.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        result.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        result.RegionFit.Matched.ShouldContain("Stockholm");
        result.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);

        // Three Full rows — the modal's civic-useful "what you're missing" direction.
        result.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        result.SkillOverlap.Matched.ShouldBe(["C#", "SQL"]);
        result.SkillOverlap.Missing.ShouldBe(["Kubernetes"]);
        result.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        result.MustHaveCoverage.Matched.ShouldContain("C#");
        result.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);

        // Single-ad read used (not the batch); the ad we asked for was scored.
        scorer.ScoreFullCallCount.ShouldBe(1);
        scorer.LastScoredId.ShouldBe(new JobAdId(jobAdId));
        builder.CvSkillsCallCount.ShouldBe(1);
    }

    // =================================================================
    // Strong + SkillOverlap NoMatch → grade Strong (golden NOT awarded); the skill row's
    // missing[] is surfaced so the modal can show what's lacking.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnStrongGrade_WhenStrongButSkillNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobAdId = Guid.NewGuid();
        var score = new FullMatchScore(
            Fast: new MatchScore(
                SsykOverlap: Dim(MatchDimensionVerdict.Match),
                TitleSimilarity: Dim(MatchDimensionVerdict.NotAssessed),
                RegionFit: Dim(MatchDimensionVerdict.Match),
                EmploymentFit: Dim(MatchDimensionVerdict.Match)),
            SkillOverlap: Dim(MatchDimensionVerdict.NoMatch, matched: [], missing: ["C#", "Azure"]),
            MustHaveCoverage: Dim(MatchDimensionVerdict.NoMatch, matched: [], missing: ["C#"]),
            NiceToHaveCoverage: Dim(MatchDimensionVerdict.NotAssessed));
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-irrelevant"));
        var scorer = new FakeScorer(score);
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(new GetJobAdMatchDetailQuery(jobAdId), ct);

        result.ShouldNotBeNull();
        // Positive-only ladder — a skill NoMatch never demotes an honest Strong.
        result!.Grade.ShouldBe(MatchGrade.Strong);
        result.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        result.SkillOverlap.Missing.ShouldBe(["C#", "Azure"]); // the lacking skills surfaced
    }

    // =================================================================
    // Empty preference → SSYK NotAssessed → grade null, BUT the DTO is still returned
    // (the modal renders the honest per-dimension breakdown + signpost). The handler must
    // NOT short-circuit (unlike the batch handler).
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnDtoWithNullGrade_WhenOccupationNotAssessed_NoShortCircuit()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobAdId = Guid.NewGuid();
        var score = new FullMatchScore(
            Fast: new MatchScore(
                SsykOverlap: Dim(MatchDimensionVerdict.NotAssessed),
                TitleSimilarity: Dim(MatchDimensionVerdict.NotAssessed),
                RegionFit: Dim(MatchDimensionVerdict.NotAssessed),
                EmploymentFit: Dim(MatchDimensionVerdict.NotAssessed)),
            SkillOverlap: Dim(MatchDimensionVerdict.NotAssessed),
            MustHaveCoverage: Dim(MatchDimensionVerdict.NotAssessed),
            NiceToHaveCoverage: Dim(MatchDimensionVerdict.NotAssessed));
        // Empty profile (no stated occupation) — the modal still scores and shows the rows.
        var builder = new FakeProfileBuilder(EmptyFullProfile());
        var scorer = new FakeScorer(score);
        var sut = CreateHandler(builder, scorer);

        var result = await sut.Handle(new GetJobAdMatchDetailQuery(jobAdId), ct);

        // DTO IS returned (not null) with an honest null grade + per-dimension rows.
        result.ShouldNotBeNull();
        result!.Grade.ShouldBeNull();
        result.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        result.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);

        // Proof it did NOT short-circuit: it built the profile AND ran the single-ad scorer.
        builder.CvSkillsCallCount.ShouldBe(1);
        scorer.ScoreFullCallCount.ShouldBe(1);
    }

    // =================================================================
    // Ad not found — the single-ad scorer throws NotFoundException → handler propagates
    // (404), never the batch's silent-omit.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldPropagateNotFoundException_WhenAdDoesNotExist()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-x"));
        var scorer = new FakeScorer(new NotFoundException("JobAd hittades inte."));
        var sut = CreateHandler(builder, scorer);

        await Should.ThrowAsync<NotFoundException>(
            async () => await sut.Handle(new GetJobAdMatchDetailQuery(Guid.NewGuid()), ct));
    }

    // =================================================================
    // Fail-closed — the profile builder throws (DEK/KMS failure) → propagates, never
    // swallowed into an honest-empty (a silent empty skill set would mis-report).
    // =================================================================

    [Fact]
    public async Task Handle_ShouldPropagate_WhenProfileBuilderFailsClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(new InvalidOperationException("DEK kunde inte värmas (fail-closed)."));
        var scorer = new FakeScorer(StrongScore());
        var sut = CreateHandler(builder, scorer);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await sut.Handle(new GetJobAdMatchDetailQuery(Guid.NewGuid()), ct));

        // The scorer is never reached when the DEK warm fails (fail-closed before scoring).
        scorer.ScoreFullCallCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // Shared fixture: a Strong-Fast + SkillOverlap Match score.
    // ---------------------------------------------------------------
    private static FullMatchScore StrongScore() =>
        new(
            Fast: new MatchScore(
                SsykOverlap: Dim(MatchDimensionVerdict.Match),
                TitleSimilarity: Dim(MatchDimensionVerdict.NotAssessed),
                RegionFit: Dim(MatchDimensionVerdict.Match),
                EmploymentFit: Dim(MatchDimensionVerdict.Match)),
            SkillOverlap: Dim(MatchDimensionVerdict.Match),
            MustHaveCoverage: Dim(MatchDimensionVerdict.NotAssessed),
            NiceToHaveCoverage: Dim(MatchDimensionVerdict.NotAssessed));
}
