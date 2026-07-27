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

    /// <summary>The handler returned a genuine Failure (owner missing / IDOR), so no gate verdict
    /// exists at all. Distinct from NotEvaluated, which means an earlier GATE stopped control.</summary>
    NoVerdict,
}

/// <summary>One rung. <see cref="CallSite"/> is a display string, never a re-typed predicate.</summary>
public sealed record GateCell(string GateId, string CallSite, GateState State);

/// <summary>
/// Renders the five auto-promote rungs for one case.
///
/// <para><b>Five, not six, since #1060's D1.2 retired the preamble gate (2026-07-27).</b> The rung
/// is removed rather than kept as a permanently-passing column: a ladder that prints a rung the
/// handler no longer has would claim control passed a gate that does not exist, which is the
/// mis-report this type's <see cref="GateState.NotEvaluated"/> state exists to prevent. The
/// retirement itself stays visible in the report through §5's "Preamble present" column, which is
/// aggregate STATE and is now the more informative reading: it says whether the carrier the
/// promoted CV's review surface reads is there at all.</para>
///
/// <para><b>No predicate expression is written anywhere in this corpus.</b> The states below are
/// derived from what the REAL handler returned, using only the handler's own control flow as
/// stated fact: the two policy gates each return unconditionally, so a block at the second
/// proves the first passed and proves nothing at all about the rungs below. Re-encoding the predicates
/// here would fork the gate ORDER — and the order is exactly what pin P6 pins, so a corpus holding
/// its own copy of it would stay green through a reordering of the product.</para>
///
/// <para><b>The collapsed token, and how it is resolved by ELIMINATION rather than guessed.</b>
/// Three distinct predicates return the single <c>PersonnummerPresent</c> reason: the parse-level
/// check, the resolved-label scan, and the composed-DTO DQ6 guard. All three are resolvable from
/// outside the Application assembly without re-typing any of them: <c>parsed.Personnummer.Found</c>
/// is readable on the aggregate, and <c>ResumeLabelResolver</c> plus <c>PersonnummerScanner</c> are
/// both public, so the corpus runs the same two public calls the handler runs. Whatever remains
/// after eliminating those two IS the DQ6 guard — there is no fourth site. So no rung is ever
/// reported as an unresolved token.</para>
///
/// <para><b>Why there is no per-case "not exercisable" state.</b> An earlier revision keyed such a
/// state on whether a case authored a personnummer, and it was wrong in both directions: it printed
/// "no fixture can make this fire" on 15 cases while case 12 was firing that very gate, and it
/// masked the label and DQ6 rungs on every promoted row where control had provably passed them.
/// Exercisability is a property of the CORPUS, not of a case, and the corpus does exercise all five
/// rungs — the DQ6 rung via the account-display-name case, which is the only route to it that a
/// parse-level personnummer does not pre-empt.</para>
/// </summary>
internal static class GateLadder
{
    internal const string G1 = "G1 pnr(parse)";
    internal const string G2 = "G2 confidence";
    internal const string G2b = "G2b pnr(label)";
    internal const string G3a = "G3a pnr(DQ6)";
    internal const string G3b = "G3b buildability";

    /// <summary>Call sites in <c>AutoPromoteParsedResumeCommandHandler</c>. Display strings: they
    /// orient a reader and are deliberately NOT load-bearing, because a line number cannot be
    /// pinned from here and a stale one must never read as a claim about behaviour.</summary>
    private static readonly (string Id, string CallSite)[] Rungs =
    [
        (G1, "pnr on parse"), (G2, "confidence"),
        (G2b, "pnr in label"), (G3a, "pnr DQ6"), (G3b, "buildability"),
    ];

    internal static IReadOnlyList<GateCell> From(
        AutoPromoteBlockReason? block,
        bool promoted,
        bool promoteFaulted,
        bool pnrFoundOnParse,
        bool pnrInResolvedLabel)
    {
        var states = Resolve(block, promoted, promoteFaulted, pnrFoundOnParse, pnrInResolvedLabel);
        return [.. Rungs.Select((r, i) => new GateCell(r.Id, r.CallSite, states[i]))];
    }

    private static GateState[] Resolve(
        AutoPromoteBlockReason? block, bool promoted, bool promoteFaulted,
        bool pnrFoundOnParse, bool pnrInResolvedLabel)
    {
        // A genuine handler fault produced no gate verdict at all. Saying "not evaluated" would
        // imply a gate stopped control; nothing did.
        if (promoteFaulted)
            return [.. Enumerable.Repeat(GateState.NoVerdict, Rungs.Length)];

        // A promote proves every rung was reached and passed.
        if (promoted)
            return [.. Enumerable.Repeat(GateState.Passed, Rungs.Length)];

        const GateState p = GateState.Passed;
        const GateState b = GateState.Blocked;
        const GateState n = GateState.NotEvaluated;

        return block switch
        {
            AutoPromoteBlockReason.ParseNotConfident => [p, b, n, n, n],
            AutoPromoteBlockReason.IncompleteContent => [p, p, p, p, b],

            // The collapsed token, resolved by elimination. The two policy gates return
            // unconditionally, so a block reached from below the first proves both passed.
            AutoPromoteBlockReason.PersonnummerPresent when pnrFoundOnParse => [b, n, n, n, n],
            AutoPromoteBlockReason.PersonnummerPresent when pnrInResolvedLabel => [p, p, b, n, n],
            AutoPromoteBlockReason.PersonnummerPresent => [p, p, p, b, n],

            _ => [.. Enumerable.Repeat(GateState.NoVerdict, Rungs.Length)],
        };
    }

    /// <summary>An edit guard, not a measurement: every literal <see cref="Resolve"/> returns is
    /// well-formed by construction, so this cannot fail against today's code. It exists so that a
    /// future hand-edited ladder cannot claim control passed a rung it never reached.</summary>
    internal static bool IsWellFormed(IReadOnlyList<GateCell> ladder)
    {
        var stopped = false;
        foreach (var cell in ladder)
        {
            if (cell.State is GateState.NotEvaluated or GateState.NoVerdict)
                stopped = true;
            else if (stopped && cell.State == GateState.Passed)
                return false;
        }

        return true;
    }
}
