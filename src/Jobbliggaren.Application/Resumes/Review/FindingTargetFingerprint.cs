using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review;

/// <summary>
/// Content-addressed identity for one review finding (Fas 4b PR-4, CTO-bind Q4 — the
/// D2(e) "TargetId fingerprint"): builds the canonical, one-way-hashable payload — the
/// unit-separator-joined tuple (rubric version, criterion id, normalized evidence).
/// Identifies a finding by WHAT IT IS, stable under layout shifts — never by position
/// (offsets are distrusted, <c>TextSpan.NotLocated</c>) and never by storing the text
/// itself. Always SERVER-derived from the engine's current finding (ADR 0074 Invariant 2
/// — a client-submitted fingerprint would be forgeable provenance).
///
/// <para><b>This class owns the canonicalization (the business knowledge); it does NOT hash.</b>
/// The keyed HMAC over this payload is <see cref="Abstractions.IFindingFingerprinter"/>, whose
/// per-deployment pepper lives OUTSIDE the database (#692, security-auditor Q4 — an unsalted
/// digest of short CV quotes is brute-forceable if the ledger leaks). Separation per Clean
/// Architecture: normalization is Application logic under these tests; the key is Infrastructure.</para>
/// </summary>
public static class FindingTargetFingerprint
{
    // U+001F INFORMATION SEPARATOR ONE: cannot survive normalization (control chars are
    // stripped), so ("A1","2x") vs ("A12","x") can never collide by concatenation.
    private const char UnitSeparator = (char)0x1F;

    /// <summary>
    /// Builds the canonical pre-hash payload for one (already redacted) criterion verdict.
    /// Multiple evidence items are normalized, sorted ordinal and joined, so the engine's
    /// quote ordering can never change the fingerprint. A <c>TextSpanEvidence</c> contributes
    /// its quote; a <c>StructuralEvidence</c> its (non-PII by construction) observation. The
    /// keyed HMAC over this string is <see cref="Abstractions.IFindingFingerprinter.Compute"/>.
    /// </summary>
    public static string BuildCanonicalPayload(RubricVersion rubricVersion, CvCriterionVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        var evidence = verdict.Evidence
            .Select(e => Normalize(e switch
            {
                TextSpanEvidence span => span.Span.Quote,
                StructuralEvidence structural => structural.Observation,
                _ => throw new InvalidOperationException(
                    $"Unknown evidence type: {e.GetType().Name}"),
            }))
            .Order(StringComparer.Ordinal);

        return string.Join(
            UnitSeparator,
            [rubricVersion.ToString(), verdict.CriterionId, .. evidence]);
    }

    /// <summary>
    /// ONE canonicalization for fingerprint text (CTO-bind Q4, the pnr-guard's
    /// normalization DISCIPLINE — Unicode NFC fold, strip invisible format/control
    /// characters, collapse every whitespace run incl. NBSP to a single space, trim) so
    /// cosmetic whitespace/encoding drift never re-keys a finding. Deliberately NOT
    /// <c>PersonnummerTextNormalizer.Normalize</c> itself — that is a digit-gap BRIDGER
    /// shaped for scan candidates, not a general text canonicalizer.
    /// </summary>
    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var folded = text.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(folded.Length);
        var pendingSpace = false;

        foreach (var c in folded)
        {
            if (char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
