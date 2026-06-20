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
        Should.Throw<ArgumentNullException>(() => MatchGradeCalculator.Grade((MatchScore)null!));
    }

    // =================================================================
    // F4-16 (ADR 0076 Amendment (b) §1; CTO 2026-06-20 D1; Klas: golden name = Top
    // "Toppmatch") — the NEW Grade(FullMatchScore) overload. It delegates to the
    // frozen Grade(MatchScore) ladder for the base, then promotes EXACTLY ONE rung
    // (MatchGrade.Top) iff base == Strong AND SkillOverlap.Verdict ∈ {Match, Partial}.
    // Sub-Strong/null bases pass through unchanged regardless of any Full verdict
    // (skills never RESCUE below Strong and never DEMOTE — positive-only ladder).
    // MustHaveCoverage/NiceToHaveCoverage do NOT affect the VISIBLE grade v1.
    // The oracle below is an INDEPENDENT re-statement of the rule (it does not call
    // the SUT), so a regression in either implementation diverges. RED until the
    // overload + the MatchGrade.Top member exist.
    // =================================================================

    private static MatchDimension FullDim(MatchDimensionVerdict verdict) => new(verdict, [], []);

    // Builds a FullMatchScore from a Fast verdict-tuple plus the three Full dimensions'
    // verdicts. Matched/Missing are irrelevant to Grade() so we leave them empty.
    private static FullMatchScore FullScore(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill,
        MatchDimensionVerdict mustHave = MatchDimensionVerdict.NotAssessed,
        MatchDimensionVerdict niceToHave = MatchDimensionVerdict.NotAssessed) =>
        new(
            Fast: Score(ssyk, region, employment),
            SkillOverlap: FullDim(skill),
            MustHaveCoverage: FullDim(mustHave),
            NiceToHaveCoverage: FullDim(niceToHave));

    // Independent oracle — the F4-16 golden rule restated from the spec, NOT delegating
    // to the SUT. base = the frozen Fast ladder (re-used Expected above).
    private static MatchGrade? ExpectedFull(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill)
    {
        var baseGrade = Expected(ssyk, region, employment);
        if (baseGrade != MatchGrade.Strong)
            return baseGrade; // null / Basic / Good / (a non-Strong) pass through unchanged

        return skill is MatchDimensionVerdict.Match or MatchDimensionVerdict.Partial
            ? MatchGrade.Top
            : MatchGrade.Strong; // NoMatch / NotAssessed → no promotion, no demotion
    }

    // Full reachable space — (SsykOverlap × RegionFit × EmploymentFit) ∈
    // {Match, NoMatch, NotAssessed}³  ×  SkillOverlap ∈ all four verdicts.
    public static TheoryData<
        MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, MatchGrade?>
        FullVerdictSpaceWithSkill()
    {
        var fastVerdicts = new[]
        {
            MatchDimensionVerdict.Match,
            MatchDimensionVerdict.NoMatch,
            MatchDimensionVerdict.NotAssessed,
        };
        var skillVerdicts = new[]
        {
            MatchDimensionVerdict.Match,
            MatchDimensionVerdict.Partial,
            MatchDimensionVerdict.NoMatch,
            MatchDimensionVerdict.NotAssessed,
        };

        var data = new TheoryData<
            MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, MatchGrade?>();

        foreach (var ssyk in fastVerdicts)
            foreach (var region in fastVerdicts)
                foreach (var employment in fastVerdicts)
                    foreach (var skill in skillVerdicts)
                        data.Add(ssyk, region, employment, skill,
                            ExpectedFull(ssyk, region, employment, skill));

        return data;
    }

    [Theory]
    [MemberData(nameof(FullVerdictSpaceWithSkill))]
    public void GradeFull_ShouldMatchTheGoldenLadderTable_AcrossTheFullReachableSpace(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill,
        MatchGrade? expected)
    {
        var score = FullScore(ssyk, region, employment, skill);

        MatchGradeCalculator.Grade(score).ShouldBe(expected,
            $"ssyk={ssyk}, region={region}, employment={employment}, skill={skill} ska ge {expected}.");
    }

    // --- The four golden-promotion cells, pinned by name (the heart of D1) ---

    [Fact]
    public void GradeFull_ShouldReturnTop_WhenBaseIsStrongAndSkillIsMatch()
    {
        // Occupation + region + employment Match → Strong base; SkillOverlap Match → Top.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Top);
    }

    [Fact]
    public void GradeFull_ShouldReturnTop_WhenBaseIsStrongAndSkillIsPartial()
    {
        // SkillOverlap never reports Partial in practice (binary set-membership), but the
        // function must promote on Partial too (defensive — CTO D1).
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Partial);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Top);
    }

    [Fact]
    public void GradeFull_ShouldReturnStrong_WhenBaseIsStrongAndSkillIsNoMatch()
    {
        // A skill mismatch (CV skills present, disjoint from the ad) NEVER demotes an
        // honest Strong — positive-only ladder. The mismatch is surfaced as modal
        // evidence, not a chip demotion.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.NoMatch);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    [Fact]
    public void GradeFull_ShouldReturnStrong_WhenBaseIsStrongAndSkillIsNotAssessed()
    {
        // No resolved CV skills / ad has no skill terms → SkillOverlap NotAssessed →
        // no promotion → stays Strong ("we could not assess skills, everything else tops").
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    // --- Skills never RESCUE a sub-Strong base (the floor invariant) ---

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Partial)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldStayGood_RegardlessOfSkill_WhenBaseIsGood(
        MatchDimensionVerdict skill)
    {
        // Occupation + region Match, employment NotAssessed → Good base. A perfect skill
        // overlap must NOT lift a Good ad to Top — promotion applies only ATOP Strong.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NotAssessed,
            skill: skill);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Partial)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldStayBasic_RegardlessOfSkill_WhenBaseIsBasic(
        MatchDimensionVerdict skill)
    {
        // Occupation Match but region contradicts → Basic base (Klas RB1 floor: "fel ort").
        // Skills can never lift a wrong-city ad to Top.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NoMatch,
            employment: MatchDimensionVerdict.Match,
            skill: skill);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Partial)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldStayNull_RegardlessOfSkill_WhenOccupationGateFails(
        MatchDimensionVerdict skill)
    {
        // Occupation NoMatch → null base (no tag). Skills never produce a tag below the gate.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.NoMatch,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: skill);

        MatchGradeCalculator.Grade(score).ShouldBeNull();
    }

    // --- MustHave / NiceToHave do NOT change the VISIBLE grade v1 ---

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Partial)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldAlwaysReturnTop_OnStrongPlusSkillMatch_RegardlessOfMustHave(
        MatchDimensionVerdict mustHave)
    {
        // Strong + SkillOverlap Match → Top, no matter the must-have coverage verdict
        // (equal-weight, evidence-only — must-have rides the modal DTO, not the grade).
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: mustHave);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Top);
    }

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Partial)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldAlwaysReturnTop_OnStrongPlusSkillMatch_RegardlessOfNiceToHave(
        MatchDimensionVerdict niceToHave)
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            niceToHave: niceToHave);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Top);
    }

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Partial)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldAlwaysReturnStrong_OnStrongPlusSkillNoMatch_RegardlessOfMustHave(
        MatchDimensionVerdict mustHave)
    {
        // Strong + SkillOverlap NoMatch stays Strong; a covered must-have cannot promote
        // (must-have is not a visible promoter v1) and a missing one cannot demote.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.NoMatch,
            mustHave: mustHave);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Partial)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldAlwaysReturnStrong_OnStrongPlusSkillNoMatch_RegardlessOfNiceToHave(
        MatchDimensionVerdict niceToHave)
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.NoMatch,
            niceToHave: niceToHave);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    // --- Null guard parity with the Fast overload ---

    [Fact]
    public void GradeFull_ShouldThrowArgumentNullException_WhenScoreIsNull()
    {
        Should.Throw<ArgumentNullException>(
            () => MatchGradeCalculator.Grade((FullMatchScore)null!));
    }
}
