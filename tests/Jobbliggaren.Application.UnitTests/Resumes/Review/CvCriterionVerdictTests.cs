using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4 STEG 9 (F4-9, ADR 0071/0074) — the HARD Invariant 2 guard on
/// <see cref="CvCriterionVerdict"/>. The verdict type has a PRIVATE ctor + two static
/// factories (<c>Assessed</c> / <c>NotAssessed</c>) so the determinism CANNOT mint an
/// assessed verdict without cited evidence, and cannot mislabel a NotAssessed criterion
/// as assessed (CLAUDE.md §5: "a CV verdict without cited textual evidence" is forbidden;
/// "reduced-precision criteria are marked 'not assessed v1', never mis-reported").
///
/// SPEC-DRIVEN against the bound contract (architect-ratified) — these tests are the
/// executable spec for the factory pre-conditions, written BEFORE the type exists.
///
/// RED until CvCriterionVerdict + CriterionVerdict + the CitedEvidence hierarchy ship in
/// Jobbliggaren.Application.Resumes.Review.Abstractions.
/// </summary>
public class CvCriterionVerdictTests
{
    private static IReadOnlyList<CitedEvidence> OneSpan() =>
        [new TextSpanEvidence(new TextSpan(0, 4, "Ledde"), "starkt handlingsverb")];

    // ===============================================================
    // 1. The verdict enum is the LOCKED four-member set (parity with the
    //    MatchDimensionVerdict pin in MatchScorerLayerTests).
    // ===============================================================

    [Fact]
    public void CriterionVerdict_ShouldBeTheLockedFourMemberSet_WhenInspected()
    {
        Enum.GetNames<CriterionVerdict>().ShouldBe(
            ["Pass", "Warn", "Fail", "NotAssessed"], ignoreOrder: true,
            "CriterionVerdict ska vara exakt { Pass, Warn, Fail, NotAssessed } " +
            "(låst 4-medlems-set, F4-9 honesty-kontrakt).");
    }

    // ===============================================================
    // 2. Assessed(...) — happy path: a Pass with one TextSpan evidence
    // ===============================================================

    [Fact]
    public void Assessed_ShouldCarryEvidenceAndVerdict_WhenPassWithSpan()
    {
        var verdict = CvCriterionVerdict.Assessed(
            "A1", RubricCategory.Content, CriterionVerdict.Pass, OneSpan());

        verdict.CriterionId.ShouldBe("A1");
        verdict.Category.ShouldBe(RubricCategory.Content);
        verdict.Verdict.ShouldBe(CriterionVerdict.Pass);
        verdict.Evidence.Count.ShouldBe(1);
        verdict.Evidence[0].ShouldBeOfType<TextSpanEvidence>();
        // An assessed verdict has no not-assessed reason.
        verdict.NotAssessedReason.ShouldBeNull();
    }

    [Theory]
    [InlineData(CriterionVerdict.Pass)]
    [InlineData(CriterionVerdict.Warn)]
    [InlineData(CriterionVerdict.Fail)]
    public void Assessed_ShouldAcceptEveryNonNotAssessedVerdict_WhenEvidenceProvided(
        CriterionVerdict verdict)
    {
        var act = () => CvCriterionVerdict.Assessed(
            "A2", RubricCategory.Content, verdict, OneSpan());

        act.ShouldNotThrow();
    }

    // ===============================================================
    // 3. Assessed(...) — Inv.2 guard: NO empty/null evidence
    // ===============================================================

    [Fact]
    public void Assessed_ShouldThrowArgumentException_WhenEvidenceIsEmpty()
    {
        // The load-bearing guard: an assessed PASS/WARN/FAIL without cited evidence is
        // forbidden (CLAUDE.md §5). The factory must reject an empty evidence list.
        var act = () => CvCriterionVerdict.Assessed(
            "A1", RubricCategory.Content, CriterionVerdict.Fail, []);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Assessed_ShouldThrowArgumentException_WhenEvidenceIsNull()
    {
        var act = () => CvCriterionVerdict.Assessed(
            "A1", RubricCategory.Content, CriterionVerdict.Fail, null!);

        act.ShouldThrow<ArgumentException>();
    }

    // ===============================================================
    // 4. Assessed(...) — Inv.2 guard: NotAssessed cannot be minted as "Assessed"
    // ===============================================================

    [Fact]
    public void Assessed_ShouldThrowArgumentException_WhenVerdictIsNotAssessed()
    {
        // A NotAssessed verdict must go through the NotAssessed factory (with a reason),
        // never the Assessed factory — otherwise a "not assessed v1" criterion could be
        // smuggled in carrying fabricated evidence.
        var act = () => CvCriterionVerdict.Assessed(
            "A5", RubricCategory.Content, CriterionVerdict.NotAssessed, OneSpan());

        act.ShouldThrow<ArgumentException>();
    }

    // ===============================================================
    // 5. NotAssessed(...) — empty evidence + the reason, verdict pinned
    // ===============================================================

    [Fact]
    public void NotAssessed_ShouldHaveEmptyEvidenceAndTheReason_WhenCalled()
    {
        const string reason = "Kräver annonskontext (A3 är annonsberoende, ej v1).";

        var verdict = CvCriterionVerdict.NotAssessed("A3", RubricCategory.Content, reason);

        verdict.CriterionId.ShouldBe("A3");
        verdict.Category.ShouldBe(RubricCategory.Content);
        verdict.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        verdict.Evidence.ShouldBeEmpty();
        verdict.NotAssessedReason.ShouldBe(reason);
    }
}
