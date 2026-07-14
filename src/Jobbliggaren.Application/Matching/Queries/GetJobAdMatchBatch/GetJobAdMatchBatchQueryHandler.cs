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
/// Only ads that earn a positive grade appear in <c>Entries</c>; ads that do not qualify or
/// do not exist are simply absent (the scorer omits missing ads, the calculator returns null
/// below the gate).
/// </para>
/// <para>
/// <b>Lifecycle (#864):</b> an ARCHIVED ad IS absent - <see cref="IMatchScorer.ScoreFullBatchAsync"/>
/// gates on <c>Status == Active</c>, so "missing" means the row does not exist OR the ad is not
/// Active, and both are omitted identically. This handler's id list is CLIENT-SUPPLIED, which is
/// what made it the reachable surface for the gap; a caller can no longer obtain a grade for an ad
/// the product may no longer present by asking for its id. Pinned by
/// <c>MatchTagBatchEndpointsTests.POST_match_tags_omits_both_non_existent_ids_and_archived_ads</c>.
/// The DETAIL path deliberately still grades an archived ad (#805-3) - it runs the SINGLE method.
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

        // ADR 0079 STEG 3 PR-D: the page-scoped TAG path runs FULL — it reads the user's
        // CONFIRMED skill set (plaintext PreferredSkills, DEK-free) so the F4-16 modal's
        // matched/missing is honest. Empty confirmed set → the three Full dimensions
        // degrade to NotAssessed (never NoMatch).
        // #300 PR-5a (ADR 0084 §A): includeRelated broadens the gate to exact ∪ related so a
        // related-occupation ad earns the Related tag in this page overlay (default false = inert).
        var profile = await profileBuilder.BuildFullForVerdictAsync(
            cancellationToken, includeRelated: query.IncludeRelated);

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
        foreach (var (jobAdId, scored) in scores)
        {
            // F4-16 (ADR 0076 Amendment (b) §1) — the VISIBLE grade is now the FULL grade:
            // a Strong Fast match with CV-skill overlap is promoted to Top ("Toppmatch").
            // The golden rung that was sort-key-only in F4-15 (b-ii) now paints the chip.
            // #300 PR-4 (ADR 0084 §F4): pass SsykIsRelated so a related-only hit caps at
            // MatchGrade.Related (lit by the live ?relaterade=on toggle, off by default; with it
            // off the related set is empty so SsykIsRelated is false).
            var score = scored.Score;
            var grade = MatchGradeCalculator.Grade(score, scored.SsykIsRelated);
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
