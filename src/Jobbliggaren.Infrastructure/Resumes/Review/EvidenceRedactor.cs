using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;

namespace Jobbliggaren.Infrastructure.Resumes.Review;

/// <summary>
/// Strips personnummer from CV-review verdict evidence (Fas 4 hardening; ADR 0074 Invariant 1;
/// security-auditor binding obligation). Applied as the single choke point in
/// <see cref="CvReviewEngine.ReviewAsync"/> over the full verdict list BEFORE the result is
/// assembled — so the redacted verdicts are the same instances carried by
/// <c>CvReviewResult.Verdicts</c>, <c>Categories[*].Verdicts</c> and <c>CriticalFails</c>.
///
/// <para>CTO-bound (docs/reviews/2026-06-16-f4-hardening-pnr-evidence-cto.md):
/// redacts <see cref="TextSpanEvidence"/> <c>Quote</c> + <c>Note</c> (Fork 2a) via
/// <see cref="PersonnummerRedactor"/> / <c>Personnummer.Masked</c> (Fork 2b); and when a
/// span's quote contained a personnummer, ZEROES its <see cref="TextSpan"/> offset (Fork 3B,
/// GDPR Art. 5(1)(c)) so no surviving pointer can re-slice the raw value out of the CV's RawText.</para>
///
/// <para>#268 C2: <see cref="StructuralEvidence"/> is ALSO run through
/// <see cref="PersonnummerRedactor"/>. It was previously left untouched as a "count-only,
/// non-PII" channel, but B8FileNameRule interpolates the raw, user-controlled CV filename
/// (which can carry a personnummer) into its observation. A genuine count-only observation
/// (e.g. B4 "1 personnummer hittat") carries no Luhn-valid number and is returned unchanged,
/// so the structural channel is now PII-safe by construction for every present and future rule.</para>
/// </summary>
internal static class EvidenceRedactor
{
    public static List<CvCriterionVerdict> Redact(IReadOnlyList<CvCriterionVerdict> verdicts)
    {
        var result = new List<CvCriterionVerdict>(verdicts.Count);
        foreach (var verdict in verdicts)
            result.Add(RedactVerdict(verdict));
        return result;
    }

    private static CvCriterionVerdict RedactVerdict(CvCriterionVerdict verdict)
    {
        // NotAssessed carries no cited evidence (only a reason) — nothing to redact.
        if (verdict.Verdict == CriterionVerdict.NotAssessed)
            return verdict;

        var redacted = new List<CitedEvidence>(verdict.Evidence.Count);
        foreach (var evidence in verdict.Evidence)
            redacted.Add(RedactEvidence(evidence));

        // Assessed verdicts always carry ≥1 evidence (Inv. 2) — the factory guard holds.
        return CvCriterionVerdict.Assessed(verdict.CriterionId, verdict.Category, verdict.Verdict, redacted);
    }

    private static CitedEvidence RedactEvidence(CitedEvidence evidence)
    {
        switch (evidence)
        {
            case TextSpanEvidence textSpan:
                {
                    var redactedQuote = PersonnummerRedactor.Redact(textSpan.Span.Quote);
                    var quoteHadPnr = !string.Equals(redactedQuote, textSpan.Span.Quote, StringComparison.Ordinal);
                    var redactedNote = textSpan.Note is null ? null : PersonnummerRedactor.Redact(textSpan.Note);

                    // Fork 3B: a span that quoted a personnummer keeps no offset into RawText.
                    var span = quoteHadPnr
                        ? new TextSpan(0, 0, redactedQuote)
                        : textSpan.Span with { Quote = redactedQuote };

                    return new TextSpanEvidence(span, redactedNote);
                }

            // #268 C2 (ADR 0074 Invariant 1): StructuralEvidence was assumed count-only/non-PII,
            // but B8FileNameRule interpolates the raw, user-controlled CV filename into its
            // Observation — and a filename can carry a personnummer (e.g. "CV_811218-9876.pdf").
            // Run the SAME PersonnummerRedactor over the observation so the structural channel is
            // PII-safe BY CONSTRUCTION for B8 and any future rule, not on the now-false premise
            // that this channel never carries PII. A genuine count-only observation (e.g. B4
            // "1 personnummer hittat") holds no Luhn-valid number, so Redact returns it unchanged
            // (same reference) and the original instance is reused — no allocation, B4 untouched.
            case StructuralEvidence structural:
                {
                    var redacted = PersonnummerRedactor.Redact(structural.Observation);
                    return ReferenceEquals(redacted, structural.Observation)
                        ? evidence
                        : new StructuralEvidence(redacted);
                }

            default:
                return evidence;
        }
    }
}
