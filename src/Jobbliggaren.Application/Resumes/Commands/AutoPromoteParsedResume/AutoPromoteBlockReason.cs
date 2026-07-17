namespace Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

/// <summary>
/// Why an auto-promote left the parsed CV pending instead of promoting it (CV-pivot PR 5a,
/// CTO-bind 2026-07-17). Carried on <see cref="AutoPromoteOutcome.LeftPending"/> for FE
/// review-view copy and telemetry — NEVER for routing (the endpoint routes on the outcome
/// TYPE, CLAUDE.md §5: no per-endpoint code matching). Not an error taxonomy: every member
/// is an expected, honest "this needs the user" state, which is why it rides a
/// <c>Result.Success</c>. Carries no PII.
/// </summary>
public enum AutoPromoteBlockReason
{
    /// <summary>The parse (or the composed content — e.g. the account display name) carries a
    /// personnummer. Fail-closed — and consent does NOT change that: the 5b consent path
    /// (DPIA #659 Beslut 2(c)) stores the original FILE only; content promotion still requires
    /// the personnummer removed (5b security-bind B3 — original-file-only depth).</summary>
    PersonnummerPresent,

    /// <summary>The file carried unclassified text above its first heading (#844). Only the
    /// user may classify it (ADR 0109) — a silent promote would drop or adopt it unasked.</summary>
    UnclassifiedPreamble,

    /// <summary>The parser's own verdict says this parse needs manual review
    /// (<c>ParseConfidence.RequiresManualReview</c> — anything below Confident). The parser
    /// owns the definition of "clean"; auto-promote does not second-guess it.</summary>
    ParseNotConfident,

    /// <summary>The parse maps to content the canonical <c>Resume</c> rejects
    /// (<c>ValidateContent</c>/<c>CreateFromParsed</c>: an entry missing its organization or
    /// title, an over-long period string, …). The user completes it in the review flow.</summary>
    IncompleteContent,
}
