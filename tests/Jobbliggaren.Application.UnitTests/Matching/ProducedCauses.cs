using Jobbliggaren.Application.Matching.Abstractions;

namespace Jobbliggaren.Application.UnitTests.Matching;

/// <summary>
/// The causes the production scorer attaches to a Fast score, for a test that fakes the scorer
/// and so has to supply them itself. Region and employment are <c>NotAssessed</c> in exactly two
/// situations (<c>MatchScorer.ScoreOrtUnion</c> / <c>ScoreEmploymentMembership</c>): the user
/// stated nothing (<see cref="MatchDimensionCause.PreferenceUnstated"/>), or — region only — the
/// län-only containment arm, which a test names explicitly. Since ADR 0170 the grade reads that
/// cause, so a fake that pairs a <c>NotAssessed</c> secondary with
/// <see cref="MatchDimensionCauses.None"/> would assert a grade for a state production cannot
/// produce. <c>Match</c> and <c>NoMatch</c> carry no cause the grade reads.
/// </summary>
internal static class ProducedCauses
{
    public static MatchDimensionCauses For(MatchScore fast)
    {
        ArgumentNullException.ThrowIfNull(fast);

        return new MatchDimensionCauses(
            SsykOverlap: null,
            RegionFit: UnstatedIfNotAssessed(fast.RegionFit),
            EmploymentFit: UnstatedIfNotAssessed(fast.EmploymentFit));
    }

    private static MatchDimensionCause? UnstatedIfNotAssessed(MatchDimension dimension) =>
        dimension.Verdict == MatchDimensionVerdict.NotAssessed
            ? MatchDimensionCause.PreferenceUnstated
            : null;
}
