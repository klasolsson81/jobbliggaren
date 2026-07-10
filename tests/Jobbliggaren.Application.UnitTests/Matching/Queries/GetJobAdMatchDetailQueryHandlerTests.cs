using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
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
/// (<see cref="IMatchProfileBuilder.BuildFullForVerdictAsync"/>, the DEK-warmed,
/// fail-closed verdict-bearing path), FULL-scores ONE ad
/// (<see cref="IMatchScorer.ScoreFullAsync"/> — single-ad THROW semantics, NOT the
/// silent-omit batch), grades it via the REQUIREMENT-AWARE
/// <see cref="MatchGradeCalculator"/> Full overload (PR-B1 RE-BIND — must-have coverage
/// GATES Strong/Top; <see cref="MatchGrade.Top"/> needs must-have Match/Vacuous + both
/// secondaries + a skill/nice signal), and projects all SEVEN dimensions to
/// <see cref="MatchDimensionDetailDto"/> rows carrying verdict + matched[] + missing[]
/// (the modal's "what you're missing for this ad" evidence).
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

        // #300 PR-5a — captures the includeRelated arg the modal handler threads into the
        // verdict-path build. Null = never called; the threading tests assert the captured
        // value equals query.IncludeRelated.
        public bool? IncludeRelatedSeen { get; private set; }

        // Unchanged F4-12 method — never called by the modal handler.
        public ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(CancellationToken cancellationToken, bool includeRelated = false)
            => throw new NotSupportedException("BuildFromPreferencesAsync ska inte anropas av modal-handlern.");

        // SORT path — not used by the modal handler.
        public ValueTask<FullCandidateMatchProfile> BuildFullForSortAsync(CancellationToken cancellationToken, bool includeRelated = false)
            => throw new NotSupportedException("BuildFullForSortAsync ska inte anropas av modal-handlern.");

        // The modal path — full CV-skill profile (DEK-warmed, fail-closed).
        public ValueTask<FullCandidateMatchProfile> BuildFullForVerdictAsync(CancellationToken cancellationToken, bool includeRelated = false)
        {
            CvSkillsCallCount++;
            IncludeRelatedSeen = includeRelated;
            if (_throwOnCvSkills is not null)
                throw _throwOnCvSkills; // fail-closed — DEK/KMS failure propagates, never swallowed
            return new ValueTask<FullCandidateMatchProfile>(fullProfile);
        }

        // ADR 0080 Vag 4 PR-2 — the background by-id builder. Never called by the modal handler.
        public ValueTask<FullCandidateMatchProfile> BuildFullForUserIdAsync(
            Guid userId, CancellationToken cancellationToken)
            => throw new NotSupportedException("BuildFullForUserIdAsync ska inte anropas av modal-handlern.");
    }

    private sealed class FakeScorer : IMatchScorer
    {
        private readonly FullMatchScore? _score;
        private readonly bool _isRelated;
        private readonly Exception? _throwOnScoreFull;

        // PR-4 (#300, ADR 0084): ScoreFullAsync now returns the FullScoredMatch carrier
        // (score + SsykIsRelated). The optional isRelated flag lets a test set the related
        // bit the handler forwards into Grade(FullMatchScore, bool); default false keeps every
        // existing test behaviour-inert (the carrier wraps the stored score, isRelated:false).
        public FakeScorer(FullMatchScore score, bool isRelated = false)
        {
            _score = score;
            _isRelated = isRelated;
        }

        public FakeScorer(Exception throwOnScoreFull) => _throwOnScoreFull = throwOnScoreFull;

        public int ScoreFullCallCount { get; private set; }
        public JobAdId? LastScoredId { get; private set; }

        public ValueTask<FullScoredMatch> ScoreFullAsync(
            JobAdId jobAdId, FullCandidateMatchProfile profile, CancellationToken cancellationToken)
        {
            ScoreFullCallCount++;
            LastScoredId = jobAdId;
            if (_throwOnScoreFull is not null)
                throw _throwOnScoreFull; // NotFoundException for a missing ad → propagate
            return new ValueTask<FullScoredMatch>(new FullScoredMatch(_score!, _isRelated, []));
        }

        // The modal handler must NOT touch any of the batch / Fast methods.
        public ValueTask<MatchScore> ScoreAsync(
            JobAdId jobAdId, CandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreAsync ska inte anropas av modal-handlern.");

        public ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>> ScoreBatchAsync(
            IReadOnlyList<JobAdId> jobAdIds, CandidateMatchProfile profile, CancellationToken cancellationToken)
            => throw new NotSupportedException("ScoreBatchAsync ska inte anropas av modal-handlern.");

        public ValueTask<IReadOnlyDictionary<JobAdId, FullScoredMatch>> ScoreFullBatchAsync(
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
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            cvSkillConceptIds);

    private static FullCandidateMatchProfile EmptyFullProfile() =>
        new(new CandidateMatchProfile(string.Empty, [], [], [], []), []);

    private static MatchDimension Dim(
        MatchDimensionVerdict v,
        IReadOnlyList<string>? matched = null,
        IReadOnlyList<string>? missing = null) =>
        new(v, matched ?? [], missing ?? []);

    // Taxonomy read-model fake. Default = passthrough (label == concept-id) so existing
    // tests are unaffected; pass a map to assert the concept-id → label resolution the
    // modal handler applies to the SSYK / region / employment membership dimensions.
    private sealed class FakeTaxonomy(IReadOnlyDictionary<string, string>? map = null) : ITaxonomyReadModel
    {
        public ValueTask<IReadOnlyList<TaxonomyLabelDto>> ResolveLabelsAsync(
            IReadOnlyList<string> conceptIds, CancellationToken cancellationToken)
            => new(conceptIds
                .Select(id => new TaxonomyLabelDto(
                    id, map is not null && map.TryGetValue(id, out var l) ? l : id))
                .ToList());

        public ValueTask<TaxonomyTreeDto> GetTreeAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException("GetTreeAsync ska inte anropas av modal-handlern.");

        public ValueTask<IReadOnlyList<TaxonomySuggestionDto>> SuggestByPrefixAsync(
            string prefix, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException("SuggestByPrefixAsync ska inte anropas av modal-handlern.");

        // ADR 0084 — the match-detail modal handler never broadens occupation groups
        // (it resolves labels only), so this member must not be reached.
        public ValueTask<IReadOnlyList<string>> GetRelatedOccupationGroupsAsync(
            IReadOnlyList<string> ssyk4ConceptIds, CancellationToken cancellationToken)
            => throw new NotSupportedException("GetRelatedOccupationGroupsAsync ska inte anropas av modal-handlern.");
    }

    private GetJobAdMatchDetailQueryHandler CreateHandler(
        FakeProfileBuilder builder, FakeScorer scorer,
        ICurrentUser? user = null, ITaxonomyReadModel? taxonomy = null) =>
        new(builder, scorer, taxonomy ?? new FakeTaxonomy(), user ?? _currentUser);

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
    public async Task Handle_ShouldReturnTopGradeWithAllSevenDimensionRows_WhenMustHaveMatchAndStrongAndSkillMatch()
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
        // PR-B1 requirement-aware Top: must-have Match (gate OPEN) + both secondaries
        // confirmed + SkillOverlap Match (the Top tie-break signal) → Top.
        result!.Grade.ShouldBe(MatchGrade.Top);

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
    // The three membership dimensions (SSYK / region / employment) carry RAW taxonomy
    // concept-ids in their evidence; the handler MUST resolve them to human labels (ADR
    // 0043 ACL — a concept-id never reaches the user; CLAUDE.md §5 — an opaque id is the
    // opposite of explainable). The skill/title dimensions already carry Display labels /
    // lexemes and MUST pass through unchanged (never re-resolved).
    // =================================================================

    [Fact]
    public async Task Handle_ShouldResolveMembershipConceptIdsToLabels_LeavingSkillEvidenceUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var score = new FullMatchScore(
            Fast: new MatchScore(
                SsykOverlap: Dim(MatchDimensionVerdict.Match, matched: ["grp_12345"]),
                TitleSimilarity: Dim(MatchDimensionVerdict.NotAssessed),
                RegionFit: Dim(MatchDimensionVerdict.Match, matched: ["region_AB"]),
                EmploymentFit: Dim(MatchDimensionVerdict.NoMatch, missing: ["emp_999"])),
            SkillOverlap: Dim(MatchDimensionVerdict.Match, matched: ["C#"]),
            MustHaveCoverage: Dim(MatchDimensionVerdict.NotAssessed),
            NiceToHaveCoverage: Dim(MatchDimensionVerdict.NotAssessed));
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grp_12345"] = "Mjukvaru- och systemutvecklare m.fl.",
            ["region_AB"] = "Stockholms län",
            ["emp_999"] = "Tillsvidareanställning",
        };
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-csharp"));
        var scorer = new FakeScorer(score);
        var sut = CreateHandler(builder, scorer, taxonomy: new FakeTaxonomy(map));

        var result = await sut.Handle(new GetJobAdMatchDetailQuery(Guid.NewGuid()), ct);

        result.ShouldNotBeNull();
        // Membership dims: concept-ids resolved to human labels (matched AND missing).
        result!.SsykOverlap.Matched.ShouldBe(["Mjukvaru- och systemutvecklare m.fl."]);
        result.RegionFit.Matched.ShouldBe(["Stockholms län"]);
        result.EmploymentFit.Missing.ShouldBe(["Tillsvidareanställning"]);
        // Skill dim already carries a Display label → passed through, never re-resolved.
        result.SkillOverlap.Matched.ShouldBe(["C#"]);
    }

    // =================================================================
    // PR-B1 (RE-BIND G1-c): Strong-Fast ad + must-have NoMatch (CV has skills, none cover
    // the must-haves) → the requirement-aware grade CAPS at Good (NOT Strong, NOT Top).
    // The must-have gate requires Match/Vacuous; NoMatch is the strongest failure signal →
    // the modal grade now reflects must-have. The skill/must-have missing[] lists are
    // surfaced so the modal shows what is lacking. THIS test FLIPPED from Strong → Good.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldCapAtGood_WhenStrongFastButMustHaveNoMatch_WithCvSkills()
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
        // Requirement-aware: must-have NoMatch caps below Strong → Good (both secondaries
        // confirm), never Top — the modal grade now reflects the binding requirement.
        result!.Grade.ShouldBe(MatchGrade.Good);
        result.Grade.ShouldNotBe(MatchGrade.Strong);
        result.Grade.ShouldNotBe(MatchGrade.Top);
        result.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        result.SkillOverlap.Missing.ShouldBe(["C#", "Azure"]); // the lacking skills surfaced
        result.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        result.MustHaveCoverage.Missing.ShouldBe(["C#"]); // the unmet must-have surfaced
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

    // =================================================================
    // #300 PR-5a — the request's IncludeRelated flag threads into the verdict-path build
    // (BuildFullForVerdictAsync). ADR 0084 question A: off by default; the PR-5 toggle is the
    // only thing that flips it true. RED until GetJobAdMatchDetailQuery carries IncludeRelated
    // AND the handler passes includeRelated: query.IncludeRelated into BuildFullForVerdictAsync.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldThreadIncludeRelatedTrue_ToBuildFullForVerdict_WhenQueryIncludeRelatedIsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-x"));
        var scorer = new FakeScorer(StrongScore());
        var sut = CreateHandler(builder, scorer);

        await sut.Handle(new GetJobAdMatchDetailQuery(Guid.NewGuid(), IncludeRelated: true), ct);

        builder.IncludeRelatedSeen.ShouldBe(true);
    }

    [Fact]
    public async Task Handle_ShouldThreadIncludeRelatedFalse_ToBuildFullForVerdict_WhenQueryIncludeRelatedIsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-x"));
        var scorer = new FakeScorer(StrongScore());
        var sut = CreateHandler(builder, scorer);

        await sut.Handle(new GetJobAdMatchDetailQuery(Guid.NewGuid(), IncludeRelated: false), ct);

        builder.IncludeRelatedSeen.ShouldBe(false);
    }

    [Fact]
    public async Task Handle_ShouldDefaultIncludeRelatedToFalse_WhenQueryOmitsIt()
    {
        // ADR 0084 question A — off by default. The modal's only production caller until the
        // PR-5 FE toggle omits the flag → the exact-only profile must be built.
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(FullProfileWithOccupation("skill-x"));
        var scorer = new FakeScorer(StrongScore());
        var sut = CreateHandler(builder, scorer);

        await sut.Handle(new GetJobAdMatchDetailQuery(Guid.NewGuid()), ct);

        builder.IncludeRelatedSeen.ShouldBe(false);
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
