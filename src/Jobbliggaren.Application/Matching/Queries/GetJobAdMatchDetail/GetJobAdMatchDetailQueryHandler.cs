using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;

/// <summary>
/// F4-16 (ADR 0076 Amendment (b) §5) — composes the single-ad match detail for the job
/// modal: builds the current user's FULL profile once
/// (<see cref="IMatchProfileBuilder.BuildFullForVerdictAsync"/>, the user's CONFIRMED skill
/// set — plaintext, DEK-free per ADR 0079 STEG 3 PR-D), scores the one ad
/// (<see cref="IMatchScorer.ScoreFullAsync"/>), grades it via the deterministic
/// <see cref="MatchGradeCalculator"/> Full overload, and maps every dimension's
/// verdict + matched/missing evidence onto the modal DTO. NO AI/LLM (ADR 0071).
/// <para>
/// <b>Honest, never short-circuits on profile state:</b> an authenticated user with no
/// stated occupation still gets the full breakdown (the dimensions report
/// <c>NotAssessed</c>, the grade is <c>null</c>) — the modal renders the rows plus the
/// "set your preferences" signpost. Only an ANONYMOUS caller gets <c>null</c> (the modal is
/// auth-gated; the guest modal shows no match section). A missing ad propagates
/// <c>NotFoundException</c> from the scorer (→ 404). The profile build is DEK-free
/// (confirmed plaintext skills, ADR 0079 PR-D) — no KMS dependency on this path.
/// </para>
/// </summary>
public sealed class GetJobAdMatchDetailQueryHandler(
    IMatchProfileBuilder profileBuilder,
    IMatchScorer scorer,
    ITaxonomyReadModel taxonomy,
    ICurrentUser currentUser)
    : IQueryHandler<GetJobAdMatchDetailQuery, JobAdMatchDetailDto?>
{
    public async ValueTask<JobAdMatchDetailDto?> Handle(
        GetJobAdMatchDetailQuery query, CancellationToken cancellationToken)
    {
        // Anonymous → no match section (the modal is auth-gated; the guest modal never
        // calls this). Defence-in-depth alongside the endpoint's RequireAuthorization.
        if (!currentUser.UserId.HasValue)
            return null;

        // Full profile from the user's CONFIRMED skill set (plaintext PreferredSkills,
        // DEK-free — ADR 0079 STEG 3 PR-D; the complete, curated set, so no truncation).
        // #300 PR-5a (ADR 0084 §A): includeRelated broadens the gate to exact ∪ related so a
        // related-occupation ad grades Related in the modal, consistent with the page overlay
        // toggle (default false = inert).
        var profile = await profileBuilder.BuildFullForVerdictAsync(
            cancellationToken, includeRelated: query.IncludeRelated);

        // Score the single ad. ScoreFullAsync throws NotFoundException for a missing ad
        // (→ 404) — propagated, not swallowed. We do NOT gate on the occupation here: the
        // modal shows the honest per-dimension breakdown even when the grade is null.
        var scored = await scorer.ScoreFullAsync(
            new JobAdId(query.JobAdId), profile, cancellationToken);

        // #300 PR-4 (ADR 0084 §F4): pass SsykIsRelated so a related-only occupation hit caps at
        // MatchGrade.Related (behaviour-inert until the PR-5 toggle populates the related set).
        var score = scored.Score;
        var grade = MatchGradeCalculator.Grade(score, scored.SsykIsRelated);

        // The three membership dimensions (SSYK / region / employment) carry RAW
        // taxonomy concept-ids in their evidence (MatchScorer.ScoreMembership). A
        // concept-id must never reach the user — it is the external system's ubiquitous
        // language (ADR 0043 ACL) and an opaque id is the opposite of explainable
        // (CLAUDE.md §5). Resolve them to human labels via the taxonomy read-model
        // (graceful fallback on drift). The skill/title dimensions already carry Display
        // labels / lexemes, so they are passed through unchanged.
        var labels = await ResolveMembershipLabelsAsync(score.Fast, cancellationToken);

        return new JobAdMatchDetailDto(
            Grade: grade,
            SsykOverlap: ToLabelledRow(score.Fast.SsykOverlap, labels),
            TitleSimilarity: ToRow(score.Fast.TitleSimilarity),
            RegionFit: ToLabelledRow(score.Fast.RegionFit, labels),
            EmploymentFit: ToLabelledRow(score.Fast.EmploymentFit, labels),
            SkillOverlap: ToRow(score.SkillOverlap),
            MustHaveCoverage: ToRow(score.MustHaveCoverage),
            NiceToHaveCoverage: ToRow(score.NiceToHaveCoverage));
    }

    private async ValueTask<IReadOnlyDictionary<string, string>> ResolveMembershipLabelsAsync(
        MatchScore fast, CancellationToken cancellationToken)
    {
        var conceptIds = new[] { fast.SsykOverlap, fast.RegionFit, fast.EmploymentFit }
            .SelectMany(d => d.Matched.Concat(d.Missing))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (conceptIds.Count == 0)
            return EmptyLabels;

        var resolved = await taxonomy.ResolveLabelsAsync(conceptIds, cancellationToken);
        return resolved.ToDictionary(l => l.ConceptId, l => l.Label, StringComparer.Ordinal);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyLabels =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static MatchDimensionDetailDto ToRow(MatchDimension dimension) =>
        new(dimension.Verdict, dimension.Matched, dimension.Missing);

    private static MatchDimensionDetailDto ToLabelledRow(
        MatchDimension dimension, IReadOnlyDictionary<string, string> labels) =>
        new(dimension.Verdict, MapLabels(dimension.Matched, labels), MapLabels(dimension.Missing, labels));

    private static IReadOnlyList<string> MapLabels(
        IReadOnlyList<string> conceptIds, IReadOnlyDictionary<string, string> labels) =>
        conceptIds.Count == 0
            ? conceptIds
            : conceptIds.Select(id => labels.TryGetValue(id, out var label) ? label : id).ToList();
}
