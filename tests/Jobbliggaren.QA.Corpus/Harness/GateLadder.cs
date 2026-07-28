using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Common;

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

    /// <summary>THE INSTRUMENT has no arm for the reason the handler returned — the ladder does not
    /// know, and says so.
    ///
    /// <para>It exists because collapsing it into <see cref="NoVerdict"/> is what let a real
    /// mis-report ship. #1060 PR C added <c>PersonnummerInAccountName</c>; this type's switch never
    /// learned it, so the sole case exercising the DQ6 rung fell to the catch-all and rendered five
    /// `no verdict` cells — which §5's own prose defines as "the handler returned a genuine FAULT".
    /// An honest block was published as a product fault, and nothing was red, because
    /// <see cref="IsWellFormed"/> passed an all-<see cref="NoVerdict"/> row.</para>
    ///
    /// <para>So this state is deliberately an INTEGRITY FAILURE (<see cref="IsWellFormed"/> rejects
    /// it) rather than a quieter third colour: a gap in the instrument must redden the instrument,
    /// not decorate a row. A gate token added without an arm here now lands in §0's "gate ladder
    /// malformed" list instead of being narrated as something the product did.</para></summary>
    Unresolved,
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
/// retirement itself stays visible in the report through §5's two preamble columns.</para>
///
/// <para><b>One of those columns was added because this docblock was wrong.</b> It claimed the
/// existing "Preamble present" column said "whether the carrier the promoted CV's review surface
/// reads is there at all". It does not: it reads <c>ParsedResumeContent.Preamble</c>, i.e. the
/// STAGING side. So the corpus could not tell "promoted carrying the preamble" from "promoted
/// having dropped it", and the byte-identical baseline that this PR cited as evidence the carrier
/// was safe was measuring nothing of the sort — a number true of its evidence and misleading
/// about its subject. §5 now prints the promoted side beside the parsed one.</para>
///
/// <para><b>No predicate expression is written anywhere in this corpus.</b> The states below are
/// derived from what the REAL handler returned, using only the handler's own control flow as
/// stated fact: the two policy gates each return unconditionally, so a block at the second
/// proves the first passed and proves nothing at all about the rungs below. Re-encoding the predicates
/// here would fork the gate ORDER — and the order is exactly what pin P6 pins, so a corpus holding
/// its own copy of it would stay green through a reordering of the product.</para>
///
/// <para><b>The collapsed token, and how it is resolved by ELIMINATION rather than guessed —
/// rewritten 2026-07-28, because the previous wording became false and was still being printed.</b>
/// It said "three distinct predicates return the single <c>PersonnummerPresent</c> reason … whatever
/// remains after eliminating those two IS the DQ6 guard — there is no fourth site." #1060 PR C split
/// the token: the composed-DTO DQ6 guard now returns its own <c>PersonnummerInAccountName</c>
/// (<c>AutoPromoteGate.cs:143-144</c>). <b>TWO</b> predicates collapse onto <c>PersonnummerPresent</c>
/// today — the parse-level check and the resolved-label scan — and both are resolvable from outside
/// the Application assembly without re-typing either: <c>parsed.Personnummer.Found</c> is readable on
/// the aggregate, and <c>ResumeLabelResolver</c> plus <c>PersonnummerScanner</c> are both public, so
/// the corpus runs the same two public calls the handler runs. The DQ6 rung is no longer reached by
/// elimination at all; it is reached by its own token.</para>
///
/// <para>Because those two guards are now exhaustive over <c>PersonnummerPresent</c>, a fall-through
/// on that token is an INSTRUMENT gap, not a product state — see <see cref="GateState.Unresolved"/>,
/// which is what the elimination claim above is worth without a state that can say "I do not know".</para>
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

            // The still-collapsed token, resolved by elimination over the TWO remaining sites that
            // return it. The two policy gates return unconditionally, so a block reached from below
            // the first proves both passed.
            AutoPromoteBlockReason.PersonnummerPresent when pnrFoundOnParse => [b, n, n, n, n],
            AutoPromoteBlockReason.PersonnummerPresent when pnrInResolvedLabel => [p, p, b, n, n],

            // DQ6 over the composed content (#1060 PR C). The SAME rung and the SAME literal as the
            // arm this replaces — only the pattern moved, because the reason reaching this rung is
            // now named rather than inferred. It is deliberately no longer a guard-less
            // `PersonnummerPresent` arm: once a fourth token existed, that arm was a guess wearing
            // elimination's clothes, and it would have answered "DQ6 blocked" for any future token.
            AutoPromoteBlockReason.PersonnummerInAccountName => [p, p, p, b, n],

            // The instrument has no arm for this token. NEVER NoVerdict: that narrates a gap in THIS
            // FILE as a fault in the handler, which is the exact mis-report this rewrite removes.
            _ => [.. Enumerable.Repeat(GateState.Unresolved, Rungs.Length)],
        };
    }

    /// <summary>An instrument guard. It rejects two different breakages, and the second is new.
    ///
    /// <para>(1) <b>An impossible ORDER</b> — a rung reported as passed after one that was never
    /// evaluated. Every literal <see cref="Resolve"/> returns is well-formed this way by
    /// construction, so this half only catches a future hand-edited ladder.</para>
    ///
    /// <para>(2) <b>An UNRESOLVED token</b> — <see cref="GateState.Unresolved"/> anywhere. This half
    /// is the one that had to exist: before it, an all-<see cref="GateState.NoVerdict"/> row passed
    /// (nothing is "passed after a stop"), which is precisely why PR C's new gate token went
    /// unnoticed here for a whole PR while the artifact published its case as a handler fault.
    /// Rejecting it wires the gap into §0's "gate ladder malformed" list and into
    /// <c>LayoutCorpusReportTests</c>'s existing instrument assert, so the NEXT token added without
    /// an arm is red rather than narrated.</para>
    ///
    /// <para>Both halves take the corpus's own derivation as their subject, never a product
    /// outcome — the assert rule at <c>LayoutCorpusReportTests</c> category (b).</para></summary>
    internal static bool IsWellFormed(IReadOnlyList<GateCell> ladder)
    {
        var stopped = false;
        foreach (var cell in ladder)
        {
            if (cell.State == GateState.Unresolved)
                return false;

            if (cell.State is GateState.NotEvaluated or GateState.NoVerdict)
                stopped = true;
            else if (stopped && cell.State == GateState.Passed)
                return false;
        }

        return true;
    }
}
