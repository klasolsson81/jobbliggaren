using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;

/// <summary>
/// F4-13 (ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 A1/B2/C2a) — composes the
/// match overlay for one /jobb page: builds the current user's preference profile once
/// (<see cref="IMatchProfileBuilder"/>, the shared SSOT — no handler-invokes-handler),
/// Fast-scores all requested ads in ONE round-trip
/// (<see cref="IMatchScorer.ScoreBatchAsync"/>, zero N+1), and grades each via the
/// deterministic <see cref="MatchGradeCalculator"/>. NO AI/LLM; reads no CV/DEK/PII.
/// <para>
/// <b>Honest, anonymous-tolerant:</b> no authenticated user OR no stated occupation
/// (the gate of the grade ladder) → empty result, no faked tags (ADR 0076 Decision 7).
/// Only ads that earn a positive grade appear in <c>Entries</c>; ads that do not qualify,
/// do not exist, or are soft-deleted are simply absent (the scorer omits missing ads,
/// the calculator returns null below the gate).
/// </para>
/// </summary>
public sealed class GetJobAdMatchBatchQueryHandler(
    IMatchProfileBuilder profileBuilder,
    IMatchScorer scorer,
    ICurrentUser currentUser)
    : IQueryHandler<GetJobAdMatchBatchQuery, JobAdMatchBatchDto>
{
    private static readonly JobAdMatchBatchDto Empty = new(new Dictionary<Guid, JobAdMatchEntryDto>());

    public async ValueTask<JobAdMatchBatchDto> Handle(
        GetJobAdMatchBatchQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue || query.JobAdIds.Count == 0)
            return Empty;

        // F4-15 (ADR 0076 Decision 6 + R5-REBIND Option H): the page-scoped TAG path runs
        // FULL — it reads the primary CV's COMPLETE skills (DEK-warmed, fail-closed) so the
        // F4-16 modal's matched/missing is honest. No primary CV / no resolved skills →
        // the three Full dimensions degrade to NotAssessed (never NoMatch).
        var profile = await profileBuilder.BuildFullFromCvSkillsAsync(cancellationToken);

        // Occupation/SSYK is the gate of the grade ladder (MatchGradeCalculator): without
        // a stated occupation no ad can earn a tag. Short-circuit before the batch query —
        // honest empty (the Översikt setup nudge owns the "complete your profile" case).
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            return Empty;

        var ids = query.JobAdIds
            .Distinct()
            .Select(id => new JobAdId(id))
            .ToList();

        var scores = await scorer.ScoreFullBatchAsync(ids, profile, cancellationToken);

        var entries = new Dictionary<Guid, JobAdMatchEntryDto>(scores.Count);
        foreach (var (jobAdId, score) in scores)
        {
            // F4-16 (ADR 0076 Amendment (b) §1) — the VISIBLE grade is now the FULL grade:
            // a Strong Fast match with CV-skill overlap is promoted to Top ("Toppmatch").
            // The golden rung that was sort-key-only in F4-15 (b-ii) now paints the chip.
            var grade = MatchGradeCalculator.Grade(score);
            if (grade is null)
                continue;

            entries[jobAdId.Value] = new JobAdMatchEntryDto(
                Grade: grade.Value,
                SsykOverlap: score.Fast.SsykOverlap.Verdict,
                TitleSimilarity: score.Fast.TitleSimilarity.Verdict,
                RegionFit: score.Fast.RegionFit.Verdict,
                EmploymentFit: score.Fast.EmploymentFit.Verdict,
                SkillOverlap: score.SkillOverlap.Verdict,
                MustHaveCoverage: score.MustHaveCoverage.Verdict,
                NiceToHaveCoverage: score.NiceToHaveCoverage.Verdict);
        }

        return new JobAdMatchBatchDto(entries);
    }
}
