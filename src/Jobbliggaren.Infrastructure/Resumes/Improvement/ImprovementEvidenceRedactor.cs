using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement;

/// <summary>
/// Strips personnummer from CV-improvement proposed-change fields (Fas 4 STEG B-2 hardening;
/// ADR 0074 Invariant 1; CTO-bound <c>docs/reviews/2026-06-17-f4-improvement-evidence-redaction-cto.md</c>).
/// Applied as the single choke point in <see cref="CvImprovementEngine.SuggestAsync"/> over the full
/// change list BEFORE the result is assembled — so the redacted changes are the same instances carried
/// by <c>CvImprovementResult.Changes</c> (verified the ONLY projection — no double-embedding, unlike
/// the review engine's Verdicts/Categories/CriticalFails).
///
/// <para>Parity #110's <see cref="Review.EvidenceRedactor"/>: redacts the cited
/// <see cref="TextSpanEvidence"/> <c>Quote</c> + <c>Note</c> via <see cref="PersonnummerRedactor"/> /
/// <c>Personnummer.Masked</c>, and ZEROES the <see cref="TextSpan"/> offset (Fork 3B,
/// GDPR Art. 5(1)(c)) when a span's quote carried a personnummer, so no surviving pointer can re-slice
/// the raw value out of the CV's RawText.</para>
///
/// <para>ADDITIONALLY (the improvement engine carries more PII surface than a review verdict —
/// CTO D1 = Variant B): redacts <see cref="ProposedReplacement.Before"/> (= the cited Quote for
/// knowledge-bank transforms — an identical leak channel), and a STRUCTURAL
/// <see cref="ProposedReplacement.After"/> (a pure transform of the user's own text — only when the
/// provenance is <see cref="StructuralTransformProvenance"/>). LEAVES UNTOUCHED a knowledge-bank
/// <c>After</c> (<see cref="KnowledgeBankProvenance"/> — a curated KB value, never user PII; redacting
/// it would corrupt curated text and break the no-synthesis contract), <c>Rationale</c>,
/// <c>Provenance.Key</c>, and <c>Operation.Target</c> (a count/ordinal descriptor, PII-safe by
/// contract — asserted in the corpus gate, not redacted, per CTO D2). Adds NO new detection logic —
/// detection lives once in <see cref="PersonnummerRedactor"/> (DRY).</para>
/// </summary>
internal static class ImprovementEvidenceRedactor
{
    public static List<ProposedChange> Redact(IReadOnlyList<ProposedChange> changes)
    {
        var result = new List<ProposedChange>(changes.Count);
        foreach (var change in changes)
            result.Add(RedactChange(change));
        return result;
    }

    private static ProposedChange RedactChange(ProposedChange change)
    {
        var redactedEvidence = RedactEvidence(change.Evidence);
        var redactedReplacement = RedactReplacement(change.Replacement, change.Provenance);

        // Nothing carried a personnummer (the common case) — keep the original instance, no allocation.
        if (ReferenceEquals(redactedEvidence, change.Evidence)
            && ReferenceEquals(redactedReplacement, change.Replacement))
            return change;

        return ProposedChange.ForRedaction(change, redactedEvidence, redactedReplacement);
    }

    private static CitedEvidence RedactEvidence(CitedEvidence evidence)
    {
        // StructuralEvidence is the count-only, non-PII channel (e.g. B4) — left untouched.
        if (evidence is not TextSpanEvidence textSpan)
            return evidence;

        var redactedQuote = PersonnummerRedactor.Redact(textSpan.Span.Quote);
        var quoteHadPnr = !string.Equals(redactedQuote, textSpan.Span.Quote, StringComparison.Ordinal);
        var redactedNote = textSpan.Note is null ? null : PersonnummerRedactor.Redact(textSpan.Note);
        var noteHadPnr = redactedNote is not null
            && !string.Equals(redactedNote, textSpan.Note, StringComparison.Ordinal);

        if (!quoteHadPnr && !noteHadPnr)
            return evidence;

        // Fork 3B: a span that quoted a personnummer keeps no offset back into RawText.
        var span = quoteHadPnr
            ? new TextSpan(0, 0, redactedQuote)
            : textSpan.Span with { Quote = redactedQuote };

        return new TextSpanEvidence(span, redactedNote);
    }

    private static ProposedReplacement? RedactReplacement(
        ProposedReplacement? replacement, ChangeProvenance provenance)
    {
        if (replacement is null)
            return null;

        var redactedBefore = PersonnummerRedactor.Redact(replacement.Before);

        // A knowledge-bank After is a curated KB value (never user PII), pinned to that value by the
        // no-synthesis contract — leave it. A structural After is a pure transform of the user's
        // Before, so it can inherit a personnummer — redact it (CTO D1 = Variant B).
        var redactedAfter = provenance is StructuralTransformProvenance
            ? PersonnummerRedactor.Redact(replacement.After)
            : replacement.After;

        if (string.Equals(redactedBefore, replacement.Before, StringComparison.Ordinal)
            && string.Equals(redactedAfter, replacement.After, StringComparison.Ordinal))
            return replacement;

        return new ProposedReplacement(redactedBefore, redactedAfter);
    }
}
