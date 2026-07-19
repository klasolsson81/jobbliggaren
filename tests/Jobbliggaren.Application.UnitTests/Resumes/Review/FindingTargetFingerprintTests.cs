using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-4 (CTO-bind Q4) — content-addressed identity for one review finding: the canonical
/// pre-hash PAYLOAD (rubric version, criterion id, normalized evidence), unit-separator-joined.
/// Identifies a finding by WHAT IT IS (stable under layout/whitespace/encoding drift), never by
/// position and never by storing text. These tests pin the normalization discipline and the
/// anti-collision guards; the payload is always SERVER-derived, so its stability is load-bearing.
/// The KEYED HMAC over this payload (#692) is <c>IFindingFingerprinter</c> — tested separately in
/// <see cref="Common.Security.HmacFindingFingerprinterTests"/>; equal payloads ⟺ equal fingerprints,
/// so every property here transfers to the fingerprint.
/// </summary>
public class FindingTargetFingerprintTests
{
    private static readonly RubricVersion V110 = RubricVersion.Parse("1.1.0");

    // U+001F INFORMATION SEPARATOR ONE — the payload's field delimiter.
    private const char Us = '\u001F';

    private static CvCriterionVerdict Fail(string criterionId, params CitedEvidence[] evidence) =>
        CvCriterionVerdict.Assessed(criterionId, RubricCategory.Content, CriterionVerdict.Fail, evidence);

    // Start/Length are ignored by BuildCanonicalPayload (it fingerprints WHAT the finding is, never
    // WHERE) — only the verbatim Quote contributes, so the offsets here are arbitrary.
    private static TextSpanEvidence Span(string quote) => new(new TextSpan(0, quote.Length, quote), null);

    // A CitedEvidence subtype the two-channel switch does not know — the abstract base is open, so an
    // unmapped kind must fail loud rather than contribute an empty string.
    private sealed record UnknownEvidence : CitedEvidence;

    [Fact]
    public void BuildCanonicalPayload_JoinsVersionCriterionAndNormalizedEvidence()
    {
        var payload = FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("Ledde teamet")));

        // (version, criterion id, normalized evidence) joined by the unit separator, in that order.
        payload.ShouldBe($"1.1.0{Us}A1{Us}Ledde teamet");
    }

    [Fact]
    public void BuildCanonicalPayload_IsDeterministic_ForTheSameInput()
    {
        var verdict = Fail("A1", Span("Ledde teamet"));

        FindingTargetFingerprint.BuildCanonicalPayload(V110, verdict)
            .ShouldBe(FindingTargetFingerprint.BuildCanonicalPayload(V110, verdict));
    }

    [Fact]
    public void BuildCanonicalPayload_IsEvidenceOrderIndependent()
    {
        var forward = Fail("A1", Span("alpha"), Span("beta"));
        var reversed = Fail("A1", Span("beta"), Span("alpha"));

        // Evidence is sorted ordinal before hashing, so the engine's quote ordering never re-keys.
        FindingTargetFingerprint.BuildCanonicalPayload(V110, forward)
            .ShouldBe(FindingTargetFingerprint.BuildCanonicalPayload(V110, reversed));
    }

    [Fact]
    public void BuildCanonicalPayload_NormalizesCosmeticWhitespaceAndInvisibleCharacters()
    {
        var canonical = FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("Ledde teamet om 8")));

        // Runs of whitespace collapse to a single space; NBSP (U+00A0) is whitespace; zero-width
        // format chars (U+200B) are stripped; leading/trailing whitespace is trimmed — none of it
        // re-keys the finding. Invisible chars are \u-escaped per the source-hygiene rule.
        FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("Ledde  teamet  om  8")))
            .ShouldBe(canonical);
        FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("Ledde\u00A0teamet\u00A0om\u00A08")))
            .ShouldBe(canonical);
        FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("Ledde teamet om 8\u200B")))
            .ShouldBe(canonical);
        FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("  Ledde teamet om 8  ")))
            .ShouldBe(canonical);
    }

    [Fact]
    public void BuildCanonicalPayload_FoldsNfc_ComposedAndDecomposedAreEqual()
    {
        // "cafe" + acute: precomposed U+00E9 ("\u00e9") vs "e" + combining acute U+0301 — NFC folds
        // them to one form, so cosmetic encoding drift never re-keys.
        var composed = Fail("A1", Span("caf\u00e9"));
        var decomposed = Fail("A1", Span("cafe\u0301"));

        FindingTargetFingerprint.BuildCanonicalPayload(V110, composed)
            .ShouldBe(FindingTargetFingerprint.BuildCanonicalPayload(V110, decomposed));
    }

    [Fact]
    public void BuildCanonicalPayload_DiffersByCriterion()
    {
        FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("driven")))
            .ShouldNotBe(FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A7", Span("driven"))));
    }

    [Fact]
    public void BuildCanonicalPayload_DiffersByRubricVersion()
    {
        var verdict = Fail("A1", Span("driven"));

        FindingTargetFingerprint.BuildCanonicalPayload(RubricVersion.Parse("1.0.0"), verdict)
            .ShouldNotBe(FindingTargetFingerprint.BuildCanonicalPayload(RubricVersion.Parse("1.1.0"), verdict));
    }

    [Fact]
    public void BuildCanonicalPayload_GuardsAgainstConcatenationAmbiguity()
    {
        // The unit-separator join makes ["ab","c"] and ["a","bc"] distinct payloads — they can never
        // collide by naive concatenation.
        FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("ab"), Span("c")))
            .ShouldNotBe(FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", Span("a"), Span("bc"))));
    }

    [Fact]
    public void BuildCanonicalPayload_StructuralEvidence_ContributesItsObservation()
    {
        // A StructuralEvidence contributes its Observation string exactly as a TextSpanEvidence
        // contributes its Quote — same text, same fingerprint.
        var structural = Fail("A1", new StructuralEvidence("kontaktsektion saknas"));
        var span = Fail("A1", Span("kontaktsektion saknas"));

        FindingTargetFingerprint.BuildCanonicalPayload(V110, structural)
            .ShouldBe(FindingTargetFingerprint.BuildCanonicalPayload(V110, span));

        // And a different observation yields a different fingerprint — the observation is load-bearing.
        FindingTargetFingerprint.BuildCanonicalPayload(V110, Fail("A1", new StructuralEvidence("annat")))
            .ShouldNotBe(FindingTargetFingerprint.BuildCanonicalPayload(V110, structural));
    }

    [Fact]
    public void BuildCanonicalPayload_ThrowsOnUnknownEvidenceType()
    {
        var verdict = Fail("A1", new UnknownEvidence());

        Should.Throw<InvalidOperationException>(() => FindingTargetFingerprint.BuildCanonicalPayload(V110, verdict));
    }
}
