using System.Text.Json;
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
    // (SsykOverlap × RegionFit × EmploymentFit) ∈ {Match, NoMatch, NotAssessed}³
    // CROSS-PRODUCTED with isRelated ∈ {true, false} (#300 PR-2, ADR 0084 §F2/§F4;
    // senior-cto-advisor + dotnet-architect bound contract). The expected value is
    // computed by an INDEPENDENT re-statement of the ladder (not by calling the SUT),
    // so a regression in either implementation diverges.
    //   • isRelated == false → the EXISTING table, bit-for-bit (the cap is purely
    //     additive — the regression guard).
    //   • isRelated == true  → the flat Related-cap: gate passes → Related; gate fails →
    //     null. The cap sits AFTER the gate and BEFORE the RB1 floor — so even a wrong-city
    //     (region NoMatch) related ad is Related, not Basic.
    // =================================================================

    public static TheoryData<MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, bool, MatchGrade?>
        FullVerdictSpace()
    {
        var verdicts = new[]
        {
            MatchDimensionVerdict.Match,
            MatchDimensionVerdict.NoMatch,
            MatchDimensionVerdict.NotAssessed,
        };

        var data = new TheoryData<
            MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, bool, MatchGrade?>();

        foreach (var ssyk in verdicts)
            foreach (var region in verdicts)
                foreach (var employment in verdicts)
                    foreach (var isRelated in new[] { false, true })
                        data.Add(ssyk, region, employment, isRelated,
                            Expected(ssyk, region, employment, isRelated));

        return data;
    }

    // Independent oracle — the ladder restated from the spec, NOT delegating to the SUT.
    // #300 PR-2: the Related-cap is threaded in EXACTLY where the bound contract places it —
    // gate first (gate WINS over the cap), then the flat cap, then (isRelated == false only)
    // the existing ladder, untouched.
    private static MatchGrade? Expected(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        bool isRelated = false)
    {
        // Gate first (unchanged) — beats the cap: a non-matching occupation earns no tag
        // even when isRelated is true.
        if (ssyk != MatchDimensionVerdict.Match)
            return null;

        // Related-cap (NEW, step 2): gate passed AND isRelated → flat Related, regardless of
        // secondaries (BEFORE the RB1 contradiction floor below).
        if (isRelated)
            return MatchGrade.Related;

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
        bool isRelated,
        MatchGrade? expected)
    {
        var score = Score(ssyk, region, employment);

        MatchGradeCalculator.Grade(score, isRelated).ShouldBe(expected,
            $"ssyk={ssyk}, region={region}, employment={employment}, " +
            $"isRelated={isRelated} ska ge {expected}.");
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
    // #300 PR-2 (ADR 0084 §F2/§F4): isRelated is threaded in BETWEEN the gate and the RB1
    // floor — gate WINS over the cap, the cap WINS over BOTH the RB1 floor and the F1(b)
    // requirement gate. isRelated == false reproduces the requirement-aware ladder unchanged.
    private static MatchGrade? ExpectedFull(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill,
        MatchDimensionVerdict mustHave,
        MatchDimensionVerdict niceToHave,
        bool isRelated = false)
    {
        // 3. Gate (unchanged) — beats the cap: a non-matching occupation earns no tag even
        //    when isRelated is true.
        if (ssyk != MatchDimensionVerdict.Match)
            return null;

        // 3b. Related-cap (NEW, step 2): gate passed AND isRelated → flat Related, BEFORE the
        //     RB1 floor AND the F1(b) requirement gate — regardless of must-have, skill,
        //     secondaries, or a contradicting region/employment.
        if (isRelated)
            return MatchGrade.Related;

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
    // ALL FIVE verdicts — CROSS-PRODUCTED with isRelated ∈ {true, false} (#300 PR-2). A
    // pure-function exhaustive cross-product over the reachable FULL space
    // (3³ × 5 × 5 × 5 × 2 = 6 750 cells). SUT ≡ oracle for every cell.
    //   • isRelated == false half  → the EXISTING requirement-aware table, bit-for-bit
    //     (the regression guard — the cap is purely additive).
    //   • isRelated == true half   → Related whenever the gate passes; null when it fails.
    public static TheoryData<
        MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict,
        MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, bool, MatchGrade?>
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
            MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict, bool, MatchGrade?>();

        foreach (var ssyk in fastVerdicts)
            foreach (var region in fastVerdicts)
                foreach (var employment in fastVerdicts)
                    foreach (var skill in AllVerdicts)
                        foreach (var mustHave in AllVerdicts)
                            foreach (var niceToHave in AllVerdicts)
                                foreach (var isRelated in new[] { false, true })
                                    data.Add(ssyk, region, employment, skill, mustHave, niceToHave,
                                        isRelated,
                                        ExpectedFull(ssyk, region, employment, skill, mustHave,
                                            niceToHave, isRelated));

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
        bool isRelated,
        MatchGrade? expected)
    {
        var score = FullScore(ssyk, region, employment, skill, mustHave, niceToHave);

        MatchGradeCalculator.Grade(score, isRelated).ShouldBe(expected,
            $"ssyk={ssyk}, region={region}, employment={employment}, skill={skill}, " +
            $"mustHave={mustHave}, niceToHave={niceToHave}, isRelated={isRelated} ska ge {expected}.");
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

    // =================================================================
    // #300 PR-2 — Related-cap (ADR 0084 §F2/§F4; ADR 0076 F1(b); senior-cto-advisor +
    // dotnet-architect bound contract). BOTH Grade overloads gain `bool isRelated = false`.
    // The cap sits AFTER the SSYK gate and BEFORE everything else:
    //   • Fast:  after the gate, BEFORE the RB1 contradiction floor.
    //   • Full:  after the gate, BEFORE both the RB1 floor AND the F1(b) requirement gate.
    // Bound evaluation order (per overload):
    //   1. Gate (unchanged): SsykOverlap.Verdict != Match → null. The gate WINS over the cap.
    //   2. Related-cap (NEW): gate passed AND isRelated == true → MatchGrade.Related. STOP.
    //      A FLAT cap — regardless of secondaries, must-have, skill, or a contradicting
    //      region/employment.
    //   3. Else (isRelated == false): the existing ladder, bit-for-bit (regression).
    //
    // MatchGrade.Related is the new rung BETWEEN Basic and Good
    // (Basic=0, Related=1, Good=2, Strong=3, Top=4). RED until the enum member + the
    // isRelated parameter on both overloads ship.
    // =================================================================

    // --- Fast overload: the load-bearing Related cells (each fails BY NAME on regression) ---

    [Fact]
    public void Grade_ShouldReturnRelated_WhenIsRelatedTrue_AndBothSecondariesMatch()
    {
        // SsykOverlap Match + region Match + employment Match would be Strong for an EXACT
        // hit — the flat Related-cap forces Related instead (the headline exact-vs-related split).
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score, isRelated: true).ShouldBe(MatchGrade.Related,
            "En related-yrkes-träff med båda sekundärerna bekräftade ska kapas till Related, " +
            "inte Strong (flat-cap, ADR 0084 §F2).");
    }

    [Fact]
    public void Grade_ShouldReturnRelated_WhenIsRelatedTrue_AndRegionContradicts()
    {
        // region NoMatch would floor an EXACT hit to Basic via the RB1 floor — but the cap
        // sits BEFORE the floor, so a wrong-city related ad is Related, NOT Basic.
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NoMatch,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score, isRelated: true).ShouldBe(MatchGrade.Related,
            "Related-cap sitter FÖRE RB1-golvet — fel ort kapar inte ner till Basic (ADR 0084 §F4).");
    }

    [Fact]
    public void Grade_ShouldReturnNull_WhenIsRelatedTrue_ButSsykOverlapIsNoMatch()
    {
        // The gate WINS over the cap: a non-matching occupation earns no tag even when related.
        var score = Score(
            ssyk: MatchDimensionVerdict.NoMatch,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score, isRelated: true).ShouldBeNull(
            "Grinden slår capen: ingen SSYK-träff → null, även om isRelated är true (ADR 0084 §F4).");
    }

    [Fact]
    public void Grade_ShouldReturnRelated_WhenIsRelatedTrue_AndBothSecondariesNotAssessed()
    {
        // Both secondaries NotAssessed would be Basic for an EXACT hit — the cap still yields
        // Related (the flat cap is indifferent to the secondaries).
        var score = Score(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NotAssessed,
            employment: MatchDimensionVerdict.NotAssessed);

        MatchGradeCalculator.Grade(score, isRelated: true).ShouldBe(MatchGrade.Related);
    }

    // --- Full overload: the load-bearing Related cells ---

    [Fact]
    public void GradeFull_ShouldReturnRelated_WhenIsRelatedTrue_AndEverythingMatches()
    {
        // mustHave Match + skill Match + both secondaries Match would be Top for an EXACT hit
        // — the cap sits BEFORE the requirement gate, so it is Related, NOT Top.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match,
            niceToHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score, isRelated: true).ShouldBe(MatchGrade.Related,
            "Related-cap sitter FÖRE F1(b)-kravgrinden — perfekt full-träff kapas till Related, " +
            "inte Top (ADR 0084 §F2).");
    }

    [Fact]
    public void GradeFull_ShouldReturnRelated_WhenIsRelatedTrue_AndRegionContradicts_WithMustHaveAndSkillMatch()
    {
        // region NoMatch + mustHave Match + skill Match would floor to Basic via RB1 for an
        // EXACT hit — the cap sits BEFORE RB1, so it is Related, NOT Basic.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.Match,
            region: MatchDimensionVerdict.NoMatch,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score, isRelated: true).ShouldBe(MatchGrade.Related,
            "Related-cap sitter FÖRE RB1-golvet på Full-overloaden — fel ort kapar inte ner " +
            "till Basic (ADR 0084 §F4).");
    }

    [Fact]
    public void GradeFull_ShouldReturnNull_WhenIsRelatedTrue_ButSsykOverlapIsNoMatch()
    {
        // The gate WINS over the cap on the Full overload too.
        var score = FullScore(
            ssyk: MatchDimensionVerdict.NoMatch,
            region: MatchDimensionVerdict.Match,
            employment: MatchDimensionVerdict.Match,
            skill: MatchDimensionVerdict.Match,
            mustHave: MatchDimensionVerdict.Match);

        MatchGradeCalculator.Grade(score, isRelated: true).ShouldBeNull(
            "Grinden slår capen även på Full-overloaden: ingen SSYK-träff → null (ADR 0084 §F4).");
    }

    // --- Flat-cap invariant headline: for isRelated == true, a gate-passing score is ALWAYS
    //     exactly Related across a representative spread of Fast + Full tuples. Never
    //     Basic/Good/Strong/Top/null. The single guard the whole exact-vs-related split rests on. ---

    public static TheoryData<MatchDimensionVerdict, MatchDimensionVerdict,
        MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict>
        RelatedFlatCapSpread()
    {
        // A representative spread where SSYK == Match (the gate passes): every secondary /
        // must-have / skill / nice combination below must collapse to exactly Related.
        var verdicts = new[]
        {
            MatchDimensionVerdict.Match,
            MatchDimensionVerdict.NoMatch,
            MatchDimensionVerdict.NotAssessed,
            MatchDimensionVerdict.Vacuous,
        };

        var data = new TheoryData<MatchDimensionVerdict, MatchDimensionVerdict,
            MatchDimensionVerdict, MatchDimensionVerdict, MatchDimensionVerdict>();

        // Fast axes kept at {Match, NoMatch, NotAssessed} (binary membership never reaches
        // Vacuous); skill/mustHave/niceToHave range over all four representative verdicts.
        var fast = new[]
        {
            MatchDimensionVerdict.Match,
            MatchDimensionVerdict.NoMatch,
            MatchDimensionVerdict.NotAssessed,
        };

        foreach (var region in fast)
            foreach (var employment in fast)
                foreach (var skill in verdicts)
                    foreach (var mustHave in verdicts)
                        // niceToHave pinned at NotAssessed to keep the spread representative,
                        // not exhaustive (the full exhaustive sweep already lives above).
                        data.Add(region, employment, skill, mustHave, MatchDimensionVerdict.NotAssessed);

        return data;
    }

    [Theory]
    [MemberData(nameof(RelatedFlatCapSpread))]
    public void Grade_ShouldAlwaysReturnExactlyRelated_WhenIsRelatedTrue_AndSsykMatches(
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill,
        MatchDimensionVerdict mustHave,
        MatchDimensionVerdict niceToHave)
    {
        // Fast: only the secondaries matter; the flat cap collapses them all to Related.
        var fastScore = Score(MatchDimensionVerdict.Match, region, employment);
        MatchGradeCalculator.Grade(fastScore, isRelated: true).ShouldBe(MatchGrade.Related,
            $"Fast: SSYK Match + isRelated true ska ALLTID ge exakt Related " +
            $"(region={region}, employment={employment}).");

        // Full: secondaries + must-have + skill + nice — still exactly Related.
        var fullScore = FullScore(
            MatchDimensionVerdict.Match, region, employment, skill, mustHave, niceToHave);
        MatchGradeCalculator.Grade(fullScore, isRelated: true).ShouldBe(MatchGrade.Related,
            $"Full: SSYK Match + isRelated true ska ALLTID ge exakt Related " +
            $"(region={region}, employment={employment}, skill={skill}, mustHave={mustHave}).");
    }

    // --- Regression guard: isRelated == false over the full reachable space equals the
    //     pre-existing expected table for BOTH overloads (so the cap is purely additive). ---

    [Theory]
    [MemberData(nameof(FullVerdictSpace))]
    public void Grade_ShouldReproduceTheExistingTable_WhenIsRelatedFalse_Fast(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        bool isRelated,
        MatchGrade? expected)
    {
        // Only the isRelated == false rows; assert against the PRE-existing 3-arg oracle
        // (no isRelated thread) to prove the cap added nothing to the false path.
        if (isRelated)
            return;

        var preExisting = Expected(ssyk, region, employment); // default isRelated:false
        preExisting.ShouldBe(expected, "Sanity: cross-product-oraklet matchar pre-cap-oraklet.");

        var score = Score(ssyk, region, employment);
        MatchGradeCalculator.Grade(score, isRelated: false).ShouldBe(preExisting,
            $"isRelated:false ska reproducera den befintliga Fast-tabellen bit-för-bit " +
            $"(ssyk={ssyk}, region={region}, employment={employment}).");
    }

    [Theory]
    [MemberData(nameof(RequirementAwareFullVerdictSpace))]
    public void Grade_ShouldReproduceTheExistingTable_WhenIsRelatedFalse_Full(
        MatchDimensionVerdict ssyk,
        MatchDimensionVerdict region,
        MatchDimensionVerdict employment,
        MatchDimensionVerdict skill,
        MatchDimensionVerdict mustHave,
        MatchDimensionVerdict niceToHave,
        bool isRelated,
        MatchGrade? expected)
    {
        if (isRelated)
            return;

        var preExisting = ExpectedFull(ssyk, region, employment, skill, mustHave, niceToHave);
        preExisting.ShouldBe(expected, "Sanity: cross-product-oraklet matchar pre-cap-oraklet.");

        var score = FullScore(ssyk, region, employment, skill, mustHave, niceToHave);
        MatchGradeCalculator.Grade(score, isRelated: false).ShouldBe(preExisting,
            $"isRelated:false ska reproducera den befintliga requirement-aware-tabellen bit-för-bit " +
            $"(ssyk={ssyk}, region={region}, employment={employment}, skill={skill}, mustHave={mustHave}).");
    }

    // =================================================================
    // #300 PR-2 — MatchGrade.Related serialization (ADR 0084 §F2). The enum carries
    // [JsonConverter(typeof(JsonStringEnumConverter))], so the wire value is the NAME
    // "Related", never the ordinal 1. Mirrors how the other rungs go on the wire.
    // =================================================================

    [Theory]
    [InlineData(MatchGrade.Basic, "\"Basic\"")]
    [InlineData(MatchGrade.Related, "\"Related\"")]
    [InlineData(MatchGrade.Good, "\"Good\"")]
    [InlineData(MatchGrade.Strong, "\"Strong\"")]
    [InlineData(MatchGrade.Top, "\"Top\"")]
    public void MatchGrade_ShouldSerializeByName_WhenWrittenToJson(
        MatchGrade grade, string expectedJson)
    {
        // The Related rung is serialized by NAME via the enum's JsonStringEnumConverter —
        // parity the four existing rungs (named verdicts on the wire are reorder-safe,
        // so the new ordinal 1 between Basic and Good never leaks as a number).
        JsonSerializer.Serialize(grade).ShouldBe(expectedJson,
            $"{grade} ska serialiseras som namnet {expectedJson} (ADR 0084 §F2).");
    }

    [Fact]
    public void MatchGrade_Related_ShouldSitBetweenBasicAndGood_ByOrdinal()
    {
        // The ordinals are bound: Basic=0, Related=1, Good=2, Strong=3, Top=4. Pin the new
        // member's position so a re-order (which the name-serialization tolerates on the wire)
        // does not silently move it relative to the ladder used in-process.
        ((int)MatchGrade.Basic).ShouldBe(0);
        ((int)MatchGrade.Related).ShouldBe(1);
        ((int)MatchGrade.Good).ShouldBe(2);
        ((int)MatchGrade.Strong).ShouldBe(3);
        ((int)MatchGrade.Top).ShouldBe(4);
    }

    // =================================================================
    // #277 — grade-inert pin: PreferredSkills/scorer stay FLAT and set-based, so adding the
    // SECOND C# twin concept-id to the confirmed set (which is grouped only at the read/offer
    // surface) cannot change the grade or the FullMatchScore. The confirmed set is the scorer's
    // CvSkillConceptIds; the scorer's concept-coverage is a SET intersection against the ad's
    // cited concept-ids, so a non-cited extra twin id is inert. We RE-STATE the production
    // ScoreConceptCoverage rule independently (parity the grade oracle above — not a delegation
    // to the SUT) and derive the twin ids LIVE from the committed asset (§5 — never hardcode).
    // =================================================================

    [Fact]
    public void Grade_WithBothCSharpTwinIds_EqualsSingleTwinBaseline_ProvingPreferredSkillsStayFlat()
    {
        var twins = CSharpTwinConceptIds();
        twins.Count.ShouldBeGreaterThanOrEqualTo(2,
            "Förutsättning: 'C#' bärs av minst två concepts (ESCO + AF) — härled, gissa aldrig.");
        var cited = twins[0]; // the ad cites exactly ONE of the twins.
        var otherTwin = twins[1];

        // Baseline: the confirmed set holds ONLY the cited twin id.
        var baseline = ScoreFullForAd(cited, cvSkills: [cited]);
        // Twin-pair: the confirmed set holds BOTH twin ids (the flat PreferredSkills the read
        // surface would group into one chip). The ad still cites only `cited`.
        var both = ScoreFullForAd(cited, cvSkills: [cited, otherTwin]);

        // The non-cited extra twin id is inert: identical dimensions (compared by VALUE — record
        // equality over IReadOnlyList members is reference-based, so assert content) and grade.
        AssertScoreEquivalent(both, baseline);
        MatchGradeCalculator.Grade(both).ShouldBe(MatchGradeCalculator.Grade(baseline),
            "PreferredSkills hålls platt + scorad som mängd → graden är tvilling-cardinalitet-inert (#277).");

        // Sanity: the baseline actually exercises a positive skill match (else the pin is vacuous).
        baseline.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        MatchGradeCalculator.Grade(baseline).ShouldNotBeNull();
    }

    // Value-equality of two FullMatchScores by dimension verdict + matched/missing CONTENT (record
    // equality treats the IReadOnlyList members by reference, so equal-content scores from distinct
    // calls are not record-equal — we compare semantics, which is what grade-inertness means).
    private static void AssertScoreEquivalent(FullMatchScore actual, FullMatchScore expected)
    {
        AssertFastEquivalent(actual.Fast, expected.Fast);
        AssertDimEquivalent(actual.SkillOverlap, expected.SkillOverlap);
        AssertDimEquivalent(actual.MustHaveCoverage, expected.MustHaveCoverage);
        AssertDimEquivalent(actual.NiceToHaveCoverage, expected.NiceToHaveCoverage);
    }

    private static void AssertFastEquivalent(MatchScore actual, MatchScore expected)
    {
        AssertDimEquivalent(actual.SsykOverlap, expected.SsykOverlap);
        AssertDimEquivalent(actual.TitleSimilarity, expected.TitleSimilarity);
        AssertDimEquivalent(actual.RegionFit, expected.RegionFit);
        AssertDimEquivalent(actual.EmploymentFit, expected.EmploymentFit);
    }

    private static void AssertDimEquivalent(MatchDimension actual, MatchDimension expected)
    {
        actual.Verdict.ShouldBe(expected.Verdict);
        actual.Matched.ShouldBe(expected.Matched);
        actual.Missing.ShouldBe(expected.Missing);
    }

    // An ad that cites <paramref name="adSkillConceptId"/> as its sole Skill term and states one
    // must-have == that same id, scored against <paramref name="cvSkills"/>. Models the production
    // scorer: Fast dims fixed at SSYK=Match + both secondaries Match; the concept-coverage dims via
    // the independently-restated SET-intersection rule (ScoreConceptCoverage parity).
    private static FullMatchScore ScoreFullForAd(string adSkillConceptId, IReadOnlyList<string> cvSkills)
    {
        var cv = cvSkills.ToHashSet(StringComparer.Ordinal);
        var fast = new MatchScore(
            SsykOverlap: Dim(MatchDimensionVerdict.Match),
            TitleSimilarity: Dim(MatchDimensionVerdict.NotAssessed),
            RegionFit: Dim(MatchDimensionVerdict.Match),
            EmploymentFit: Dim(MatchDimensionVerdict.Match));

        return new FullMatchScore(
            Fast: fast,
            SkillOverlap: Coverage([adSkillConceptId], cv),
            MustHaveCoverage: Coverage([adSkillConceptId], cv),
            NiceToHaveCoverage: Coverage([], cv));
    }

    // Independent re-statement of MatchScorer.ScoreConceptCoverage (SET-emptiness verdict, no
    // threshold): CV empty → NotAssessed; ad partition empty → Vacuous; else matched/missing by
    // set membership → Match / Partial / NoMatch. Matched/missing carry the concept-ids
    // (Ordinal-sorted) — the test does not need Display labels.
    private static MatchDimension Coverage(IReadOnlyList<string> adConceptIds, HashSet<string> cvSkills)
    {
        if (cvSkills.Count == 0)
            return Dim(MatchDimensionVerdict.NotAssessed);
        if (adConceptIds.Count == 0)
            return Dim(MatchDimensionVerdict.Vacuous);

        var matched = adConceptIds.Where(cvSkills.Contains).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var missing = adConceptIds.Where(x => !cvSkills.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var verdict = matched.Count == 0
            ? MatchDimensionVerdict.NoMatch
            : missing.Count == 0 ? MatchDimensionVerdict.Match : MatchDimensionVerdict.Partial;
        return new MatchDimension(verdict, matched, missing);
    }

    // Live provenance: the concept-ids carrying the literal "C#" (preferred label OR synonym),
    // read from the committed JobTech skill-taxonomy asset (Ordinal-sorted, deterministic). Never
    // hardcoded (§5). Mirrors SkillResolverIntegrationTests / SkillSurfaceGroupingTests.
    private static List<string> CSharpTwinConceptIds()
    {
        const string resource = "Jobbliggaren.Infrastructure.Taxonomy.jobad-skill-taxonomy.v30.json";
        var asm = typeof(Jobbliggaren.Infrastructure.TextAnalysis.LocalTextAnalyzer).Assembly;
        using var stream = asm.GetManifestResourceStream(resource);
        stream.ShouldNotBeNull($"Skill-taxonomi-resursen '{resource}' ska vara en <EmbeddedResource>.");

        using var doc = JsonDocument.Parse(stream!);
        var ids = new List<string>();
        foreach (var el in doc.RootElement.GetProperty("skills").EnumerateArray())
        {
            var carries = string.Equals(
                el.GetProperty("preferredLabel").GetString()?.Trim(), "C#", StringComparison.OrdinalIgnoreCase);
            if (!carries && el.TryGetProperty("synonyms", out var syns) && syns.ValueKind == JsonValueKind.Array)
                foreach (var s in syns.EnumerateArray())
                    if (string.Equals(s.GetString()?.Trim(), "C#", StringComparison.OrdinalIgnoreCase))
                    {
                        carries = true;
                        break;
                    }

            if (carries)
                ids.Add(el.GetProperty("conceptId").GetString()!);
        }

        ids.Sort(StringComparer.Ordinal);
        return ids;
    }
}
