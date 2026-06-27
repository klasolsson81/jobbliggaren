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
/// The FAST ladder under test (<see cref="MatchGradeCalculator.Grade(MatchScore)"/>) —
/// UNCHANGED by the requirement-aware rebind (it has no must-have input):
/// <list type="number">
/// <item><b>Gate:</b> SsykOverlap != Match → <c>null</c> (no tag).</item>
/// <item>Occupation Match + a stated NoMatch on Region OR Employment → <c>Basic</c>
/// (a contradicted preference floors the grade).</item>
/// <item>Otherwise by count of <c>Match</c> among {Region, Employment}: 2 → <c>Strong</c>;
/// 1 → <c>Good</c>; 0 (both NotAssessed) → <c>Basic</c>.</item>
/// </list>
/// <see cref="MatchScore.TitleSimilarity"/> is NOT consulted — proven below.
/// </para>
/// <para>
/// The FULL ladder under test (<see cref="MatchGradeCalculator.Grade(FullMatchScore)"/>)
/// is REQUIREMENT-AWARE (PR-B1; senior-cto-advisor 2026-06-20 RE-BIND G1; Klas product
/// decision; ADR 0076 amendment REVERSING Amendment (b) §1's "must-have never caps").
/// must-have coverage now GATES the upper rungs — it is no longer evidence-only. The
/// total function (first matching step wins, SSYK = Match assumed):
/// <list type="number">
/// <item><b>Gate:</b> <c>Fast.SsykOverlap.Verdict != Match</c> → <c>null</c>.</item>
/// <item><b>RB1 floor (evaluated BEFORE must-have):</b>
/// <c>RegionFit == NoMatch OR EmploymentFit == NoMatch</c> → <c>Basic</c>.</item>
/// <item><c>HasSkillOrNiceSignal = SkillOverlap ∈ {Match,Partial} OR NiceToHaveCoverage ∈
/// {Match,Partial}</c>.</item>
/// <item><b>requirementBacked (ADR 0076 amendment 2026-06-27, F1(b)):</b>
/// <c>MustHaveCoverage == Match OR (MustHaveCoverage == Vacuous AND HasSkillOrNiceSignal)</c>.
/// A covered must-have is sufficient evidence on its own; a <c>Vacuous</c> must-have (ad
/// states none) is gate-open ONLY when backed by a skill/nice signal. AMENDS Reading 1 —
/// Vacuous alone no longer reaches Strong/Top (~98% of ads are Vacuous).</item>
/// <item><c>confirmed = #Match among {RegionFit, EmploymentFit}</c>.</item>
/// <item>If <c>requirementBacked</c>: <c>confirmed == 2 AND HasSkillOrNiceSignal</c> →
/// <c>Top</c>; else <c>confirmed >= 1</c> → <c>Strong</c>; else → <c>Basic</c> (evidence but
/// no secondary stated).</item>
/// <item>Else (no CV / Partial/NoMatch must-have / <b>Vacuous-without-signal</b>):
/// <c>confirmed >= 1</c> → <c>Good</c>; else → <c>Basic</c>.</item>
/// </list>
/// Key consequences: no-CV (must-have NotAssessed) caps at <c>Good</c> — never Strong/Top;
/// Partial/NoMatch must-have cap at Good/Basic; <c>Vacuous</c> reaches Strong/Top ONLY with a
/// skill/nice signal (F1(b) — else caps at Good); the RB1 floor wins over a met must-have +
/// perfect skill. The oracle below is an INDEPENDENT re-statement, not a delegation to the SUT.
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
    // PR-B1 — REQUIREMENT-AWARE Grade(FullMatchScore) overload (senior-cto-advisor
    // 2026-06-20 RE-BIND G1; Klas product decision; ADR 0076 amendment REVERSING
    // Amendment (b) §1's "must-have never caps"). This REPLACES the F4-16
    // "must-have is evidence-only / skill promotes Strong→Top" section — those
    // assertions are now WRONG. must-have coverage GATES the upper rungs.
    //
    // The exact total function (the independent oracle is ExpectedFull below):
    //   1. null-guard.
    //   2. var f = score.Fast;
    //   3. if f.SsykOverlap.Verdict != Match → null.
    //   4. RB1 floor (BEFORE must-have): region NoMatch OR employment NoMatch → Basic.
    //   5. signal = skill∈{Match,Partial} || nice∈{Match,Partial};
    //      requirementBacked = mustHave==Match || (mustHave==Vacuous && signal)  [F1(b)].
    //   6. confirmed = #Match among {region, employment}.
    //   7. if requirementBacked:
    //        confirmed==2 && signal → Top;  confirmed>=1 → Strong;  else → Basic.
    //   8. else (incl. Vacuous-without-signal): confirmed>=1 → Good;  else → Basic.
    //
    // RED until: MatchDimensionVerdict.Vacuous (5th member), the requirement-aware
    // Grade(FullMatchScore) body, and MatchGrade.Top all exist.
    // =================================================================

    private static MatchDimension FullDim(MatchDimensionVerdict verdict) => new(verdict, [], []);

    // Builds a FullMatchScore from a Fast verdict-tuple plus the three Full dimensions'
    // verdicts. Matched/Missing are irrelevant to Grade() so we leave them empty.
    private static FullMatchScore FullScore(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill = MatchDimensionVerdict.NotAssessed,
        MatchDimensionVerdict mustHave = MatchDimensionVerdict.NotAssessed,
        MatchDimensionVerdict niceToHave = MatchDimensionVerdict.NotAssessed) =>
        new(
            Fast: Score(ssyk, region, employment),
            SkillOverlap: FullDim(skill),
            MustHaveCoverage: FullDim(mustHave),
            NiceToHaveCoverage: FullDim(niceToHave));

    // INDEPENDENT oracle — the requirement-aware function restated from the spec, NOT
    // delegating to the SUT. A regression in either implementation diverges.
    private static MatchGrade? ExpectedFull(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill,
        MatchDimensionVerdict mustHave,
        MatchDimensionVerdict niceToHave)
    {
        // 3. Gate.
        if (ssyk != MatchDimensionVerdict.Match)
            return null;

        // 4. RB1 floor — evaluated BEFORE must-have; a contradicted stated preference
        //    floors to Basic even if must-have is met and skills are perfect.
        if (region == MatchDimensionVerdict.NoMatch
            || employment == MatchDimensionVerdict.NoMatch)
            return MatchGrade.Basic;

        // 5. requirementBacked (ADR 0076 amendment 2026-06-27, F1(b)): a must-have Match is
        //    sufficient evidence on its own; a Vacuous must-have (ad states none) is gate-open
        //    ONLY when backed by a positive skill/nice signal. Amends Reading 1 — Vacuous alone
        //    no longer reaches Strong/Top (~98% of ads are Vacuous, so that inflated the
        //    requirement-backed rungs with zero evidence). NotAssessed/Partial/NoMatch never pass.
        var skillOrNiceSignal =
            skill is MatchDimensionVerdict.Match or MatchDimensionVerdict.Partial
            || niceToHave is MatchDimensionVerdict.Match or MatchDimensionVerdict.Partial;
        var requirementBacked =
            mustHave == MatchDimensionVerdict.Match
            || (mustHave == MatchDimensionVerdict.Vacuous && skillOrNiceSignal);

        // 6.
        var confirmed =
            (region == MatchDimensionVerdict.Match ? 1 : 0)
            + (employment == MatchDimensionVerdict.Match ? 1 : 0);

        if (requirementBacked)
        {
            if (confirmed == 2 && skillOrNiceSignal)
                return MatchGrade.Top;
            if (confirmed >= 1)
                return MatchGrade.Strong;
            return MatchGrade.Basic; // evidence present but no secondary stated
        }

        // 8. No requirement evidence (no CV / Partial / NoMatch must-have / vacuous-without-
        //    skill) — caps below Strong.
        return confirmed >= 1 ? MatchGrade.Good : MatchGrade.Basic;
    }

    // All five verdicts, so the sweep is a genuine pure-function exhaustive table.
    private static readonly MatchDimensionVerdict[] AllVerdicts =
    [
        MatchDimensionVerdict.Match,
        MatchDimensionVerdict.Partial,
        MatchDimensionVerdict.NoMatch,
        MatchDimensionVerdict.NotAssessed,
        MatchDimensionVerdict.Vacuous,
    ];

    // The three Fast dims only reach {Match, NoMatch, NotAssessed} in practice (binary
    // set-membership never reports Partial/Vacuous on the Fast tuple), so the sweep
    // keeps the Fast axes at those three and lets skill/mustHave/niceToHave range over
    // ALL FIVE verdicts — a pure-function exhaustive cross-product over the reachable
    // FULL space (3³ × 5 × 5 × 5 = 3 375 cells). SUT ≡ oracle for every cell.
    public static TheoryData<
        MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict,
        MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, MatchGrade?>
        RequirementAwareFullVerdictSpace()
    {
        var fastVerdicts = new[]
        {
            MatchDimensionVerdict.Match,
            MatchDimensionVerdict.NoMatch,
            MatchDimensionVerdict.NotAssessed,
        };

        var data = new TheoryData<
            MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict,
            MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, MatchGrade?>();

        foreach (var ssyk in fastVerdicts)
            foreach (var region in fastVerdicts)
                foreach (var employment in fastVerdicts)
                    foreach (var skill in AllVerdicts)
                        foreach (var mustHave in AllVerdicts)
                            foreach (var niceToHave in AllVerdicts)
                                data.Add(ssyk, region, employment, skill, mustHave, niceToHave,
                                    ExpectedFull(ssyk, region, employment, skill, mustHave, niceToHave));

        return data;
    }

    [Theory]
    [MemberData(nameof(RequirementAwareFullVerdictSpace))]
    public void GradeFull_ShouldMatchTheRequirementAwareLadderTable_AcrossTheFullReachableSpace(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill,
        MatchDimensionVerdict mustHave,
        MatchDimensionVerdict niceToHave,
        MatchGrade? expected)
    {
        var score = FullScore(ssyk, region, employment, skill, mustHave, niceToHave);

        MatchGradeCalculator.Grade(score).ShouldBe(expected,
            $"ssyk={ssyk}, region={region}, employment={employment}, skill={skill}, " +
            $"mustHave={mustHave}, niceToHave={niceToHave} ska ge {expected}.");
    }

    // -----------------------------------------------------------------
    // Named cell tests — the load-bearing consequences of the rebind, each pinned
    // so an implementation diverging from the bound function fails by NAME.
    // -----------------------------------------------------------------

    // --- No-CV ceiling = Good; NEVER Strong/Top (the load-bearing reversal of
    //     ADR 0076 Amendment (b) §1). No CV ⇒ mustHave/skill/nice all NotAssessed. ---

    [Fact]
    public void GradeFull_ShouldReturnGood_WhenNoCv_AndBothSecondariesMatch()
    {
        // mustHave NotAssessed (no CV) → gate NOT met → caps at Good even with both
        // secondaries confirmed. THIS is the reversal: pre-rebind this was Strong.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.NotAssessed,
            niceToHave: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public void GradeFull_ShouldReturnGood_WhenNoCv_AndOneSecondaryMatch()
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public void GradeFull_ShouldReturnBasic_WhenNoCv_AndNoSecondaryConfirmed()
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldNeverReachStrongOrTop_WhenNoCv_RegardlessOfFastTuple(
        MatchDimensionVerdict secondary)
    {
        // No CV (mustHave NotAssessed) can NEVER reach Strong/Top for ANY Fast tuple.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: secondary,
            employment: secondary,
            skill: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.NotAssessed,
            niceToHave: MatchDimensionVerdict.NotAssessed);

        var grade = MatchGradeCalculator.Grade(score);
        grade.ShouldNotBe(MatchGrade.Strong);
        grade.ShouldNotBe(MatchGrade.Top);
    }

    // --- Partial must-have → caps at Good (Klas: "Stark kräver ALLA must-have") ---

    [Fact]
    public void GradeFull_ShouldReturnGood_WhenMustHavePartial_AndBothSecondariesMatch()
    {
        // Partial ∉ {Match, Vacuous} → gate NOT met → caps at Good despite confirmed==2
        // and a perfect skill overlap.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Partial);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public void GradeFull_ShouldReturnBasic_WhenMustHavePartial_AndNoSecondaryConfirmed()
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.Partial);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    // --- NoMatch must-have (CV present, disjoint) → caps at Good/Basic, NEVER Strong/Top ---

    [Fact]
    public void GradeFull_ShouldReturnGood_WhenMustHaveNoMatch_AndBothSecondariesMatch()
    {
        // The strongest must-have FAILURE signal (CV has skills, none cover the must-haves).
        // Must NOT read as a strong candidate match — caps at Good (secondaries confirm).
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.NoMatch);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public void GradeFull_ShouldReturnBasic_WhenMustHaveNoMatch_AndNoSecondaryConfirmed()
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.NoMatch);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    // --- Vacuous must-have (ad states no must-haves, CV present) — ADR 0076 amendment
    //     2026-06-27 (F1(b)): gate-open ONLY when backed by a skill/nice signal. Vacuous
    //     WITHOUT a signal is pure preference-fit → caps at Good (was Strong pre-amendment). ---

    [Fact]
    public void GradeFull_ShouldReturnGood_WhenMustHaveVacuous_AndBothSecondariesMatch_NoSkillSignal()
    {
        // F1(b) CHANGED CELL: a bare ad (no must-have terms) + both secondaries Match + NO
        // skill/nice signal → Good (pre-amendment this was Strong). Vacuous alone is not
        // requirement evidence; without a skill/nice overlap the ad is pure preference-fit.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Vacuous,
            mustHave: MatchDimensionVerdict.Vacuous,
            niceToHave: MatchDimensionVerdict.Vacuous);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    [Fact]
    public void GradeFull_ShouldReturnStrong_WhenMustHaveVacuous_OneSecondaryMatch_AndSkillSignal()
    {
        // F1(b): a Vacuous must-have BACKED by a skill signal IS requirement-backed (Reading
        // 1's spirit survives — a candidate genuinely sharing a skill with a no-requirement
        // ad is not punished). One confirmed secondary + a skill Partial → Strong.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NotAssessed,
            skill: MatchDimensionVerdict.Partial,
            mustHave: MatchDimensionVerdict.Vacuous);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    [Fact]
    public void GradeFull_ShouldReturnTop_WhenMustHaveVacuous_AndBothSecondariesMatch_AndSkillMatch()
    {
        // Vacuous must-have is gate-open; with confirmed==2 and a skill Match signal → Top
        // (exactly like a Match must-have would behave).
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Vacuous);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Top);
    }

    [Fact]
    public void GradeFull_ShouldReturnGood_WhenMustHaveVacuous_OneSecondaryMatch_NoSkillSignal()
    {
        // F1(b) CHANGED CELL: a Vacuous gate WITHOUT a skill/nice signal is not requirement-
        // backed → one confirmed secondary caps at Good (pre-amendment this was Strong).
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.Vacuous);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good);
    }

    // --- F1(b) load-bearing invariant: Vacuous-without-signal NEVER reaches Strong/Top
    //     (symmetric to the no-CV invariant above). ~98% of real ads are Vacuous. ---

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.NoMatch)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldNeverReachStrongOrTop_WhenMustHaveVacuous_AndNoSkillSignal_RegardlessOfFastTuple(
        MatchDimensionVerdict secondary)
    {
        // A Vacuous must-have WITHOUT a skill/nice signal is preference-fit only — it can never
        // be requirement-backed, so it caps at Good (or Basic via the RB1 floor / no secondary)
        // for ANY Fast tuple. The headline F1(b) guard, failing BY NAME on regression.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: secondary,
            employment: secondary,
            skill: MatchDimensionVerdict.Vacuous,
            mustHave: MatchDimensionVerdict.Vacuous,
            niceToHave: MatchDimensionVerdict.Vacuous);

        var grade = MatchGradeCalculator.Grade(score);
        grade.ShouldNotBe(MatchGrade.Strong);
        grade.ShouldNotBe(MatchGrade.Top);
    }

    // --- The RB1 floor wins over a met must-have + perfect skill ---

    [Fact]
    public void GradeFull_ShouldReturnBasic_WhenRegionContradicts_EvenWithMustHaveMatchAndSkillMatch()
    {
        // Region NoMatch floors to Basic FIRST — must-have Match + skill Match cannot
        // rescue a wrong-city ad (Klas: "det sämsta är fel ort"). The floor wins.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NoMatch,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    [Fact]
    public void GradeFull_ShouldReturnBasic_WhenEmploymentContradicts_EvenWithMustHaveMatchAndSkillMatch()
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NoMatch,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    // --- must-have met + 0 secondaries confirmed (both NotAssessed) → Basic
    //     (the one non-obvious cell; the bound CTO "otherwise" behaviour) ---

    [Fact]
    public void GradeFull_ShouldReturnBasic_WhenMustHaveMatch_ButBothSecondariesNotAssessed()
    {
        // must-have met but NO secondary preference stated → Basic. The gate is open but
        // there is nothing to confirm Strong (CTO "else → Basic"). Bound, non-obvious.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.NotAssessed,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    [Fact]
    public void GradeFull_ShouldReturnBasic_WhenMustHaveVacuous_ButBothSecondariesNotAssessed()
    {
        // Same "otherwise" cell with a Vacuous gate — still Basic (no secondary stated).
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.NotAssessed,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Vacuous);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    // --- Top requires confirmed==2 AND a skill|nice signal ---

    [Fact]
    public void GradeFull_ShouldReturnTop_WhenMustHaveMatch_BothSecondariesMatch_AndSkillSignal()
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Top);
    }

    [Fact]
    public void GradeFull_ShouldReturnTop_WhenMustHaveMatch_BothSecondariesMatch_AndNiceSignalOnly()
    {
        // No skill signal, but nice-to-have Match supplies the Top tie-break.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.Match,
            niceToHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Top);
    }

    [Fact]
    public void GradeFull_ShouldReturnStrong_WhenMustHaveMatch_BothSecondariesMatch_ButNoSkillOrNiceSignal()
    {
        // confirmed==2 but skill NoMatch and nice NotAssessed → no Top tie-break → Strong.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.NoMatch,
            mustHave: MatchDimensionVerdict.Match,
            niceToHave: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    [Fact]
    public void GradeFull_ShouldNotReachTop_WhenMustHaveMatch_OnlyOneSecondaryConfirmed_EvenWithSkillMatch()
    {
        // Top REQUIRES confirmed==2; with one secondary NotAssessed it caps at Strong even
        // with a perfect skill overlap (the open-secondary Strong fallback).
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NotAssessed,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    // --- Strong open-secondary fallback (confirmed==1, must-have met) ---

    [Fact]
    public void GradeFull_ShouldReturnStrong_WhenMustHaveMatch_RegionMatch_EmploymentNotAssessed()
    {
        // One secondary Match, the other NotAssessed (not contradicted) + must-have met
        // → Strong (the open-secondary fallback).
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.NotAssessed,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    [Fact]
    public void GradeFull_ShouldReturnStrong_WhenMustHaveMatch_RegionNotAssessed_EmploymentMatch()
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    // --- The occupation gate still wins above everything (null regardless of Full dims) ---

    [Theory]
    [InlineData(MatchDimensionVerdict.Match)]
    [InlineData(MatchDimensionVerdict.Vacuous)]
    [InlineData(MatchDimensionVerdict.NotAssessed)]
    public void GradeFull_ShouldReturnNull_WhenOccupationGateFails_RegardlessOfMustHave(
        MatchDimensionVerdict mustHave)
    {
        var score = FullScore(
            ssyk: MatchDimensionVerdict.NoMatch,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: mustHave);

        MatchGradeCalculator.Grade(score).ShouldBeNull();
    }

    // --- Null guard parity with the Fast overload ---

    [Fact]
    public void GradeFull_ShouldThrowArgumentNullException_WhenScoreIsNull()
    {
        Should.Throw<ArgumentNullException>(
            () => MatchGradeCalculator.Grade((FullMatchScore)null!));
    }
}
