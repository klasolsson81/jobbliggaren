using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// The C# grade SSOT for the Fast tier, read off the real scorer. Since ADR 0170 the grade also
/// reads the causes the scorer attaches to a <c>NotAssessed</c> region or employment verdict
/// (an unstated preference confirms), and the Fast methods (<see cref="IMatchScorer.ScoreAsync"/>,
/// <see cref="IMatchScorer.ScoreBatchAsync"/>) return the bare <see cref="MatchScore"/> without
/// them. <see cref="FullMatchScore.Fast"/> is the same score by contract (<see cref="IMatchScorer"/>),
/// so asking the Full scorer for the same ads yields the Fast tuple PLUS the real causes — nothing
/// here is hand-built.
/// </summary>
internal static class ScorerGrading
{
    /// <summary>
    /// The Fast grade of one ad: <c>Grade(Score.Fast, Causes, SsykIsRelated)</c> over what
    /// <see cref="IMatchScorer.ScoreFullAsync"/> returns for it.
    /// </summary>
    public static async Task<MatchGrade?> GradeAsync(
        IMatchScorer scorer, JobAdId jobAdId, CandidateMatchProfile fast, CancellationToken ct)
    {
        var scored = await scorer.ScoreFullAsync(jobAdId, new FullCandidateMatchProfile(fast, []), ct);
        return MatchGradeCalculator.Grade(scored.Score.Fast, scored.Causes, scored.SsykIsRelated);
    }

    /// <summary>
    /// The Fast grade of every ad <see cref="IMatchScorer.ScoreFullBatchAsync"/> scores. An ad the
    /// batch gate drops is absent from the result, exactly as it is from the scorer's own.
    /// </summary>
    public static async Task<IReadOnlyDictionary<JobAdId, MatchGrade?>> GradeBatchAsync(
        IMatchScorer scorer,
        IReadOnlyList<JobAdId> jobAdIds,
        FullCandidateMatchProfile profile,
        CancellationToken ct)
    {
        var scored = await scorer.ScoreFullBatchAsync(jobAdIds, profile, ct);
        return scored.ToDictionary(
            kv => kv.Key,
            kv => MatchGradeCalculator.Grade(kv.Value.Score.Fast, kv.Value.Causes, kv.Value.SsykIsRelated));
    }
}
