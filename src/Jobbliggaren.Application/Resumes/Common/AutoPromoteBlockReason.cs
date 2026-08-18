namespace Jobbliggaren.Application.Resumes.Common;

/// <summary>
/// Why an auto-promote left the parsed CV pending instead of promoting it (CV-pivot PR 5a,
/// CTO-bind 2026-07-17). Carried on <see cref="AutoPromoteOutcome.LeftPending"/> for FE
/// review-view copy and telemetry — NEVER for routing (the endpoint routes on the outcome
/// TYPE, CLAUDE.md §5: no per-endpoint code matching). Not an error taxonomy: every member
/// is an expected, honest "this needs the user" state, which is why it rides a
/// <c>Result.Success</c>. Carries no PII.
///
/// <para><b>Retired 2026-07-27 (#1060, CTO-bind D1.2): <c>UnclassifiedPreamble</c>.</b> That gate
/// blocked promote whenever the file carried text above its first heading — the most common
/// Swedish CV layout — and it enforced nothing ADR 0109 forbids. ADR 0109 §1 forbids MINTING
/// section identity, not promoting; the gate existed only because <c>ParsedResume.Promote</c>
/// soft-deletes the artifact, so promote WAS the drop. ADR 0109 §2's carrier is now read past
/// that soft-delete on the promoted CV's review surface, so nothing is dropped and nothing is
/// minted, and the gate has no subject left. Its bound exit was removed under it in any case:
/// ADR 0109's Amendment 2026-07-18 FAS-DEFERRED the classify step and ADR 0112 made the review
/// read-only, turning a gate-with-an-exit into a gate with none.</para>
/// </summary>
public enum AutoPromoteBlockReason
{
    /// <summary>The FILE carries a personnummer — the parse's own scan flagged it, or the CV
    /// LABEL the user typed does. Fail-closed, and consent does NOT change that: the 5b consent
    /// path (DPIA #659 Beslut 2(c)) stores the original FILE only; content promotion still
    /// requires the personnummer removed (5b security-bind B3 — original-file-only depth).
    ///
    /// <para><b>This member NARROWED on 2026-07-28 (#1060 PR C, CTO-bind D2).</b> It used to
    /// cover the account-display-name case too, which made it a token that could not say where
    /// the number was — and the copy it drove told the user to fix her file when the file was
    /// clean. See <see cref="PersonnummerInAccountName"/>. The split is bound rather than
    /// branched in the FE, because branching on <c>Personnummer.Found</c> to infer WHERE the
    /// number sat would rest on an unwritten ordering invariant nothing pins (D4-REBIND
    /// alternative (6), the same reasoning that refused a DEK-free prefix evaluator).</para></summary>
    PersonnummerPresent,

    /// <summary>The composed content carries a personnummer that the file does not: the account
    /// holder's <c>JobSeeker.DisplayName</c> does (#1060 PR C, CTO-bind D2). DQ6 on the composed
    /// content is the only control that can catch this — the display name is the one text the
    /// promote composition adds over the raw superset the import scan already covered — so the
    /// parse's own scan reports clean and no file-side surface shows anything.
    ///
    /// <para>It is a SEPARATE member because the two need different user actions and different
    /// copy: this one is fixed under Inställningar, not by editing and re-uploading the CV.
    /// Reporting it as <see cref="PersonnummerPresent"/> sent the user to look in a clean file,
    /// which is a mis-reported verdict (CLAUDE.md §5) and a loop she cannot exit.</para>
    ///
    /// <para><b>Not a new PII surface:</b> like every other member this is a gate identity, not
    /// evidence. It says which control fired, never what it saw.</para>
    ///
    /// <para><b>Reachable only from a legacy row since #1117.</b> The display-name channel is now
    /// closed at the source: <c>JobSeeker.Register</c>/<c>UpdateDisplayName</c> refuse a
    /// personnummer, so no current write path can produce this state. The invariant is
    /// forward-only (EF materializes existing rows past the factory methods), so this member
    /// still fires on a row written before it landed, and the user action it names is
    /// unchanged.</para></summary>
    PersonnummerInAccountName,

    /// <summary>Extraction produced nothing usable — <c>ParseConfidence.Overall</c> is
    /// <c>Failed</c>. Promoting that would build a <c>Resume</c> out of the account display name
    /// and nothing else: a canonical CV that says LESS than the file did, which is the same
    /// dishonesty class as dropping (ADR 0109 §3).
    ///
    /// <para><b>The token means <c>Failed</c> only — this NARROWED on 2026-07-25 (#1060 CTO-bind
    /// D1.3) and the narrowing reverses the 5a bind's R3.</b> R3 had tightened the gate from
    /// "not <c>Failed</c>" to <c>RequiresManualReview</c> (<c>Overall != Confident</c>) on two
    /// grounds, and both fail. (1) It called "not Failed" a second normaliser of the same concept;
    /// it is not — <c>RequiresManualReview</c> answers the REVIEW question ("does a human need to
    /// look at this parse?"), and whether anything is worth saving is the PROMOTE question. Using
    /// one predicate for two questions is itself the DRY violation. (2) It feared auto-promoting
    /// low-confidence PII; PII is not confidence's job — <c>Personnummer.Found</c> and
    /// <c>ResumeContentPersonnummerGuard</c> are the PII controls and are unconditional.
    /// <c>Degraded</c> means the parser found SOMETHING and is honest that the document was messy;
    /// under ADR 0112 the reviewer IS the product, so that is precisely the CV the reviewer has
    /// most to say about. Do not re-tighten this from the 5a bind alone.</para></summary>
    ParseNotConfident,

    /// <summary>The parse maps to content the canonical <c>Resume</c> rejects
    /// (<c>ValidateContent</c>/<c>CreateFromParsed</c>: an entry missing its organization or
    /// title, an over-long period string, …). The user completes it in the review flow.</summary>
    IncompleteContent,
}
