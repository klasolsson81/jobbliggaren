using Jobbliggaren.Application.Resumes.Common;

namespace Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

/// <summary>
/// The two non-error outcomes of an auto-promote (CV-pivot PR 5a, CTO-bind 2026-07-17 R1):
/// the parse promoted to a canonical CV, or it honestly was not clean enough and stays
/// pending for the user's review. <b>Both are <c>Result.Success</c></b> — "not clean" is an
/// expected product state, not a caller mistake, and modelling it as <c>Result.Failure</c>
/// would send it through the central <c>ToProblemResult</c> mapper as a 400 (CLAUDE.md §3:
/// the kind IS the status; §5: no per-endpoint code matching). <c>Result.Failure</c> on this
/// command therefore always means a genuine fault (unknown/foreign artifact, infrastructure).
///
/// <para>A CLOSED discriminated union (private constructor + nested cases — nothing outside
/// this file can add a case), so the endpoint's TYPE pattern-match stays exhaustive:
/// <see cref="Promoted"/> → 201 + the new resume id (FE routes to the canonical review);
/// <see cref="LeftPending"/> → 200 + the already-known parsed id (FE routes to the staging
/// review). The <see cref="AutoPromoteBlockReason"/> is copy/telemetry, never routing.
/// Strengthens the <c>CitedEvidence</c> DU precedent (open abstract record) to a sealed
/// hierarchy because routing correctness hangs on exhaustiveness here.</para>
/// </summary>
public abstract record AutoPromoteOutcome
{
    private AutoPromoteOutcome() { }

    /// <summary>The parse was clean and now IS a canonical <c>Resume</c> (verbatim, no
    /// synthesis — ADR 0071); the staging artifact is <c>Promoted</c> + soft-deleted.</summary>
    public sealed record Promoted(Guid ResumeId) : AutoPromoteOutcome;

    /// <summary>The parse stays <c>PendingReview</c>, untouched — nothing was mutated, no
    /// audit row was written (a pending created no Resume; recording a promote for it would
    /// be misreporting, CLAUDE.md §5). The user finishes in the review flow.
    ///
    /// <para><b>It carries the gate TOKEN and nothing else. A Domain constraint code must never
    /// be added here</b> (#1060 D3(β) PR 2). This type crosses the assembly boundary:
    /// <c>ResumesEndpoints</c> puts <c>pending.Reason.ToString()</c> straight onto the wire, so a
    /// code added here reaches a zod schema and an FE with no copy for it — a mis-reported verdict
    /// shown to a user plus a hardcoded UI string (CLAUDE.md §5, both halves). The diagnostic code
    /// lives on the <c>internal</c> <c>AutoPromoteGateVerdict.Blocked</c> instead, where the
    /// missing <c>InternalsVisibleTo</c> to <c>Jobbliggaren.Api</c> makes the leak a build error.
    /// Nothing on THIS path is compiler-enforced, which is why the rule is written here rather
    /// than only where it was decided.</para></summary>
    public sealed record LeftPending(AutoPromoteBlockReason Reason) : AutoPromoteOutcome;
}
