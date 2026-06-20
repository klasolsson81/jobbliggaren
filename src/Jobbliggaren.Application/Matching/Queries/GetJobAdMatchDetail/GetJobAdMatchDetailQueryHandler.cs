using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;

/// <summary>
/// F4-16 (ADR 0076 Amendment (b) §5) — composes the single-ad match detail for the job
/// modal: builds the current user's FULL profile once
/// (<see cref="IMatchProfileBuilder.BuildFullFromCvSkillsAsync"/>, the complete CV skills via
/// the DEK pipeline, fail-closed — parity the F4-15 batch tag path), scores the one ad
/// (<see cref="IMatchScorer.ScoreFullAsync"/>), grades it via the deterministic
/// <see cref="MatchGradeCalculator"/> Full overload, and maps every dimension's
/// verdict + matched/missing evidence onto the modal DTO. NO AI/LLM (ADR 0071).
/// <para>
/// <b>Honest, never short-circuits on profile state:</b> an authenticated user with no
/// stated occupation still gets the full breakdown (the dimensions report
/// <c>NotAssessed</c>, the grade is <c>null</c>) — the modal renders the rows plus the
/// "set your preferences" signpost. Only an ANONYMOUS caller gets <c>null</c> (the modal is
/// auth-gated; the guest modal shows no match section). A missing ad propagates
/// <c>NotFoundException</c> from the scorer (→ 404); a DEK/KMS failure propagates from the
/// builder (fail-closed — never a dishonest empty skill set).
/// </para>
/// </summary>
public sealed class GetJobAdMatchDetailQueryHandler(
    IMatchProfileBuilder profileBuilder,
    IMatchScorer scorer,
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

        // Full profile from the primary CV's COMPLETE skills (DEK-warmed, fail-closed —
        // a KMS/DEK failure PROPAGATES; it never degrades to a dishonest empty set).
        var profile = await profileBuilder.BuildFullFromCvSkillsAsync(cancellationToken);

        // Score the single ad. ScoreFullAsync throws NotFoundException for a missing ad
        // (→ 404) — propagated, not swallowed. We do NOT gate on the occupation here: the
        // modal shows the honest per-dimension breakdown even when the grade is null.
        var score = await scorer.ScoreFullAsync(
            new JobAdId(query.JobAdId), profile, cancellationToken);

        var grade = MatchGradeCalculator.Grade(score);

        return new JobAdMatchDetailDto(
            Grade: grade,
            SsykOverlap: ToRow(score.Fast.SsykOverlap),
            TitleSimilarity: ToRow(score.Fast.TitleSimilarity),
            RegionFit: ToRow(score.Fast.RegionFit),
            EmploymentFit: ToRow(score.Fast.EmploymentFit),
            SkillOverlap: ToRow(score.SkillOverlap),
            MustHaveCoverage: ToRow(score.MustHaveCoverage),
            NiceToHaveCoverage: ToRow(score.NiceToHaveCoverage));
    }

    private static MatchDimensionDetailDto ToRow(MatchDimension dimension) =>
        new(dimension.Verdict, dimension.Matched, dimension.Missing);
}
