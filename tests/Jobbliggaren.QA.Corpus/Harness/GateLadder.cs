using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

namespace Jobbliggaren.QA.Corpus.Harness;

/// <summary>Where a gate stood on one case's run.</summary>
public enum GateState
{
    /// <summary>Control provably reached and passed this gate.</summary>
    Passed,

    /// <summary>This gate is the one that returned LeftPending.</summary>
    Blocked,

    /// <summary>An earlier gate blocked, so control never reached this one. Reporting it as
    /// "passed" would be a mis-report (CLAUDE.md §5).</summary>
    NotEvaluated,

    /// <summary>No fixture in the corpus can make this gate fire. A verdict for a control nothing
    /// exercises is reduced precision and must be marked as such, never printed as a pass.</summary>
    NotExercisable,

    /// <summary>Reached, but its outcome is not observable from outside the Application assembly.
    /// Named rather than guessed.</summary>
    AmbiguousToken,
}

/// <summary>One rung. <see cref="CallSite"/> is a STRING, never a re-typed predicate.</summary>
public sealed record GateCell(string GateId, string CallSite, string Predicate, GateState State);

/// <summary>
/// Renders the six auto-promote rungs for one case.
///
/// <para><b>No predicate expression is written anywhere in this corpus.</b> The states below are
/// derived from what the REAL handler returned, using only the handler's own control flow as
/// stated fact: the three policy gates at <c>:101</c>, <c>:104</c> and <c>:107</c> each return
/// unconditionally, so a block at <c>:104</c> proves <c>:101</c> passed and proves nothing at all
/// about <c>:107</c>. Re-encoding the predicates here would fork the gate ORDER — and the order is
/// exactly what pin P6 pins, so a corpus holding its own copy of it would stay green through a
/// reordering of the product.</para>
///
/// <para><b>The one genuine ambiguity, named rather than papered over.</b> Three distinct
/// predicates collapse onto the single <c>PersonnummerPresent</c> token: the parse-level check at
/// <c>:101</c>, the resolved-label scan at <c>:134</c>, and the composed-DTO DQ6 guard at
/// <c>:145</c>. The first two are resolvable from outside — <c>parsed.Personnummer.Found</c> is
/// readable on the aggregate, and <c>ResumeLabelResolver</c> plus <c>PersonnummerScanner</c> are
/// both public, so the corpus runs the same two public calls the handler runs. Only the DQ6 guard
/// is <c>internal</c> to Application, so only it can be <see cref="GateState.AmbiguousToken"/> —
/// and only when the label scan passed.</para>
/// </summary>
internal static class GateLadder
{
    internal const string G1 = "G1 pnr(parse)";
    internal const string G2 = "G2 preamble";
    internal const string G3 = "G3 confidence";
    internal const string G3b = "G3b pnr(label)";
    internal const string G4a = "G4a pnr(DQ6)";
    internal const string G4b = "G4b buildability";

    internal static IReadOnlyList<GateCell> From(
        AutoPromoteBlockReason? block,
        bool promoted,
        bool pnrFoundOnParse,
        bool pnrInResolvedLabel,
        bool fixtureCanCarryPersonnummer)
    {
        var states = Resolve(block, promoted, pnrFoundOnParse, pnrInResolvedLabel,
            fixtureCanCarryPersonnummer);

        return
        [
            new(G1, ":101", "parsed.Personnummer.Found", states[0]),
            new(G2, ":104", "!IsNullOrWhiteSpace(parsed.Content.Preamble)", states[1]),
            new(G3, ":107", "parsed.Confidence.RequiresManualReview", states[2]),
            new(G3b, ":134", "PersonnummerScanner.Scan(label).Count > 0", states[3]),
            new(G4a, ":145", "ResumeContentPersonnummerGuard.Check(dto).IsFailure", states[4]),
            new(G4b, ":152", "Resume.CreateFromParsed(...).IsFailure", states[5]),
        ];
    }

    private static GateState[] Resolve(
        AutoPromoteBlockReason? block, bool promoted, bool pnrFoundOnParse, bool pnrInResolvedLabel,
        bool fixtureCanCarryPersonnummer)
    {
        // A promote proves every rung was reached and passed.
        if (promoted)
        {
            return
            [
                GateState.Passed, GateState.Passed, GateState.Passed,
                PnrRung(fixtureCanCarryPersonnummer, GateState.Passed),
                PnrRung(fixtureCanCarryPersonnummer, GateState.Passed),
                GateState.Passed,
            ];
        }

        var notExercisable = PnrRung(fixtureCanCarryPersonnummer, GateState.NotEvaluated);

        return block switch
        {
            AutoPromoteBlockReason.UnclassifiedPreamble =>
            [
                GateState.Passed, GateState.Blocked, GateState.NotEvaluated,
                GateState.NotEvaluated, GateState.NotEvaluated, GateState.NotEvaluated,
            ],
            AutoPromoteBlockReason.ParseNotConfident =>
            [
                GateState.Passed, GateState.Passed, GateState.Blocked,
                GateState.NotEvaluated, GateState.NotEvaluated, GateState.NotEvaluated,
            ],
            AutoPromoteBlockReason.IncompleteContent =>
            [
                GateState.Passed, GateState.Passed, GateState.Passed,
                PnrRung(fixtureCanCarryPersonnummer, GateState.Passed),
                PnrRung(fixtureCanCarryPersonnummer, GateState.Passed),
                GateState.Blocked,
            ],

            // The collapsed token. :104-108 return unconditionally, so a block reached from
            // anywhere below :101 proves G1, G2 and G3 all passed.
            AutoPromoteBlockReason.PersonnummerPresent when pnrFoundOnParse =>
            [
                GateState.Blocked, GateState.NotEvaluated, GateState.NotEvaluated,
                GateState.NotEvaluated, GateState.NotEvaluated, GateState.NotEvaluated,
            ],
            AutoPromoteBlockReason.PersonnummerPresent when pnrInResolvedLabel =>
            [
                GateState.Passed, GateState.Passed, GateState.Passed,
                GateState.Blocked, GateState.NotEvaluated, GateState.NotEvaluated,
            ],
            AutoPromoteBlockReason.PersonnummerPresent =>
            [
                GateState.Passed, GateState.Passed, GateState.Passed,
                GateState.Passed, GateState.Blocked, GateState.NotEvaluated,
            ],

            // No block and no promote means the handler returned a Failure — an owner/IDOR fault,
            // not a gate verdict. Nothing may be claimed about any rung.
            _ => [notExercisable, notExercisable, notExercisable, notExercisable, notExercisable, notExercisable],
        };
    }

    private static GateState PnrRung(bool fixtureCanCarryPersonnummer, GateState ifExercisable) =>
        fixtureCanCarryPersonnummer ? ifExercisable : GateState.NotExercisable;

    /// <summary>The one instrument assert on the ladder: control cannot pass a rung it never
    /// reached, so <c>Passed</c> may never follow <c>NotEvaluated</c>.</summary>
    internal static bool IsWellFormed(IReadOnlyList<GateCell> ladder)
    {
        var seenNotEvaluated = false;
        foreach (var cell in ladder)
        {
            if (cell.State == GateState.NotEvaluated)
                seenNotEvaluated = true;
            else if (seenNotEvaluated && cell.State == GateState.Passed)
                return false;
        }

        return true;
    }
}
