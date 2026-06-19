using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Grading;

/// <summary>
/// F4-13 (ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 graded-ladder re-bind) —
/// the deterministic preference→<see cref="MatchGrade"/> rule is the ORACLE for the
/// page-scoped match tag. A pure function: no I/O, no clock, no randomness, so the full
/// reachable verdict space is pinned here cell-by-cell (RED-first table = the spec).
/// <para>
/// The ladder under test (<see cref="MatchGradeCalculator.Grade"/>):
/// <list type="number">
/// <item><b>Gate:</b> SsykOverlap != Match → <c>null</c> (no tag).</item>
/// <item>Occupation Match + a stated NoMatch on Region OR Employment → <c>Basic</c>
/// (a contradicted preference floors the grade).</item>
/// <item>Otherwise by count of <c>Match</c> among {Region, Employment}: 2 → <c>Strong</c>;
/// 1 → <c>Good</c>; 0 (both NotAssessed) → <c>Basic</c>.</item>
/// </list>
/// <see cref="MatchScore.TitleSimilarity"/> is NOT consulted — proven below.
/// </para>
/// </summary>
public class MatchGradeCalculatorTests
{
    // ---------------------------------------------------------------
    // Small MatchScore factory: only the three verdicts the ladder reads
    // (SsykOverlap, RegionFit, EmploymentFit) plus a controllable Title verdict.
    // Matched/Missing are irrelevant to Grade() so we leave them empty — the
    // calculator reads Verdict only.
    // ---------------------------------------------------------------
    private static MatchScore Score(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict title = MatchDimensionVerdict.NotAssessed) =>
        new(
            SsykOverlap: Dim(ssyk),
            TitleSimilarity: Dim(title),
            RegionFit: Dim(region),
            EmploymentFit: Dim(employment));

    private static MatchDimension Dim(MatchDimensionVerdict verdict) =>
        new(verdict, [], []);

    // =================================================================
    // Gate — occupation/SSYK must Match to earn ANY tag (else null)
    // =================================================================

    [Fact]
    public void Grade_ShouldReturnNull_WhenSsykOverlapIsNoMatch()
    {
        // Even with both secondaries confirmed, a non-matching occupation → no tag.
        var score = Score(
            ssyk: MatchDimensionVerdict.NoMatch,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBeNull();
    }

    [Fact]
    public void Grade_ShouldReturnNull_WhenSsykOverlapIsNotAssessed()
    {
        // No stated occupation (empty SSYK list → NotAssessed) → no tag. The Översikt
        // setup nudge owns this case, not a discouraging per-card tag.
        var score = Score(
            ssyk: MatchDimensionVerdict.NotAssessed,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBeNull();
    }

    [Fact]
    public void Grade_ShouldReturnNull_WhenSsykOverlapIsPartial()
    {
        // Partial is unreachable on the binary membership SSYK dimension, but the gate
        // is `!= Match` so a (hypothetical) Partial occupation also yields no tag.
        var score = Score(
            ssyk: MatchDimensionVerdict.Partial,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBeNull();
    }

    // =================================================================
    // Contradiction floor — a stated NoMatch on region OR employment → Basic
    // =================================================================

    [Fact]
    public void Grade_ShouldReturnBasic_WhenRegionContradicts()
    {
        // Klas 2026-06-19: a deselected region must not read as a strong match —
        // even with employment confirmed it floors to Basic ("det sämsta är fel ort").
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NoMatch,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    [Fact]
    public void Grade_ShouldReturnBasic_WhenEmploymentContradicts()
    {
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NoMatch);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    [Fact]
    public void Grade_ShouldReturnBasic_WhenBothSecondariesContradict()
    {
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NoMatch,
            employment: MatchDimensionVerdict.NoMatch);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    // =================================================================
    // Count branch — confirmed secondaries determine the grade
    // =================================================================

    [Fact]
    public void Grade_ShouldReturnStrong_WhenBothSecondariesConfirmed()
    {
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    [Fact]
    public void Grade_ShouldReturnGood_WhenOnlyRegionConfirmed()
    {
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public void Grade_ShouldReturnGood_WhenOnlyEmploymentConfirmed()
    {
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public void Grade_ShouldReturnBasic_WhenNeitherSecondaryAssessed()
    {
        // Occupation Match, both secondaries NotAssessed ("open"/not stated) → Basic.
        // A NotAssessed secondary never penalises and never confirms.
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    // =================================================================
    // Partial on a secondary — NOT produced by ScoreMembership, but pinned
    // against the ACTUAL switch: Partial is neither Match (does not confirm)
    // nor NoMatch (does not floor) → falls through to the count branch as 0
    // confirmed → Basic. We assert ONLY what the code does, not an invented rule.
    // =================================================================

    [Fact]
    public void Grade_ShouldReturnBasic_WhenRegionPartialAndEmploymentNotAssessed()
    {
        // Partial neither contradicts (not NoMatch → no floor trigger) nor confirms
        // (not Match → confirmedSecondaries stays 0) → count branch _ => Basic.
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Partial,
            employment: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    [Fact]
    public void Grade_ShouldReturnGood_WhenRegionPartialAndEmploymentConfirmed()
    {
        // Partial region does not confirm; only the confirmed employment counts → 1 → Good.
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Partial,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    // =================================================================
    // TitleSimilarity is NOT consulted — the grade is invariant to the title verdict
    // =================================================================

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Partial)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void Grade_ShouldIgnoreTitleSimilarity_WhenComputingGrade(
        MatchDimensionVerdict titleVerdict)
    {
        // Same occupation + secondaries, varying only the title verdict → same grade.
        // (On the preference path Title is always NotAssessed; this proves a title
        // signal could never alter the ladder, so the contract is honest.)
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NotAssessed,
            title: titleVerdict);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    // =================================================================
    // Full reachable space — exhaustive [Theory] over
    // (SsykOverlap × RegionFit × EmploymentFit) ∈ {Match, NoMatch, NotAssessed}³.
    // The expected value is computed by an INDEPENDENT re-statement of the ladder
    // (not by calling the SUT), so a regression in either implementation diverges.
    // =================================================================

    public static TheoryData<MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, MatchGrade?>
        FullVerdictSpace()
    {
        var verdicts = new[]
        {
            MatchDimensionVerdict.Match,
            MatchDimensionVerdict.NoMatch,
            MatchDimensionVerdict.NotAssessed,
        };

        var data = new TheoryData<
            MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, MatchGrade?>();

        foreach (var ssyk in verdicts)
            foreach (var region in verdicts)
                foreach (var employment in verdicts)
                    data.Add(ssyk, region, employment, Expected(ssyk, region, employment));

        return data;
    }

    // Independent oracle — the ladder restated from the spec, NOT delegating to the SUT.
    private static MatchGrade? Expected(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment)
    {
        if (ssyk != MatchDimensionVerdict.Match)
            return null;

        if (region == MatchDimensionVerdict.NoMatch
            || employment == MatchDimensionVerdict.NoMatch)
            return MatchGrade.Basic;

        var confirmed =
            (region == MatchDimensionVerdict.Match ? 1 : 0)
            + (employment == MatchDimensionVerdict.Match ? 1 : 0);

        return confirmed switch
        {
            2 => MatchGrade.Strong,
            1 => MatchGrade.Good,
            _ => MatchGrade.Basic,
        };
    }

    [Theory]
    [MemberData(nameof(FullVerdictSpace))]
    public void Grade_ShouldMatchTheLadderTable_AcrossTheFullReachableVerdictSpace(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchGrade? expected)
    {
        var score = Score(ssyk, region, employment);

        MatchGradeCalculator.Grade(score).ShouldBe(expected,
            $"ssyk={ssyk}, region={region}, employment={employment} ska ge {expected}.");
    }

    // =================================================================
    // Null-guard (the production code calls ArgumentNullException.ThrowIfNull)
    // =================================================================

    [Fact]
    public void Grade_ShouldThrowArgumentNullException_WhenScoreIsNull()
    {
        Should.Throw<ArgumentNullException>(() => MatchGradeCalculator.Grade(null!));
    }
}
