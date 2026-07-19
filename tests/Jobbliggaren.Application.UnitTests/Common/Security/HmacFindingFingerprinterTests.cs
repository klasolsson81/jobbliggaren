using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// HmacFindingFingerprinter (#692, ADR 0093 §D2(e)) — the keyed at-rest identity for
/// <c>resume_finding_statuses.target_fingerprint</c>. security-auditor (Fas 4b PR-4 Q4) bound that a
/// plain SHA-256 is only the INTERIM floor: the evidence is short user-written CV quotes, so an
/// UNKEYED digest is dictionary/brute-forceable if the ledger leaks in isolation. This suite pins the
/// property that closes #692 — the fingerprint is KEYED under a per-deployment pepper, not a plain
/// digest — plus that the canonicalization still drives identity.
/// </summary>
/// <remarks>
/// Mirrors <see cref="HmacProtectedIdentityTokenizerTests"/>. The canonicalization properties
/// (normalization, evidence order-independence, version/criterion keying) live in
/// <c>FindingTargetFingerprintTests</c> against <c>BuildCanonicalPayload</c>; HERE we pin the KEY.
/// </remarks>
public class HmacFindingFingerprinterTests
{
    private static readonly RubricVersion V110 = RubricVersion.Parse("1.1.0");

    private static readonly byte[] PepperA = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] PepperB = RandomNumberGenerator.GetBytes(32);

    private static HmacFindingFingerprinter Fingerprinter(byte[] pepper) =>
        new(Options.Create(new CvReviewFingerprintPseudonymizationOptions
        {
            PepperBase64 = Convert.ToBase64String(pepper),
        }));

    private static CvCriterionVerdict Fail(string criterionId, params CitedEvidence[] evidence) =>
        CvCriterionVerdict.Assessed(criterionId, RubricCategory.Content, CriterionVerdict.Fail, evidence);

    private static TextSpanEvidence Span(string quote) => new(new TextSpan(0, quote.Length, quote), null);

    private sealed record UnknownEvidence : CitedEvidence;

    // ================================================================================
    // Deterministic — the reconcile re-derives the fingerprint and equality-matches "same finding?".
    // ================================================================================

    [Fact]
    public void The_same_verdict_yields_the_same_fingerprint_every_time()
    {
        var sut = Fingerprinter(PepperA);
        var verdict = Fail("A1", Span("Ledde teamet"));

        sut.Compute(V110, verdict).ShouldBe(sut.Compute(V110, verdict));
    }

    [Fact]
    public void Two_instances_with_the_same_pepper_agree()
    {
        var verdict = Fail("A1", Span("Ledde teamet"));

        Fingerprinter(PepperA).Compute(V110, verdict)
            .ShouldBe(Fingerprinter(PepperA).Compute(V110, verdict));
    }

    // ================================================================================
    // KEYED under a SEPARATE pepper — the whole point of #692, not a plain digest.
    // ================================================================================

    [Fact]
    public void A_different_pepper_yields_a_different_fingerprint_for_the_same_finding()
    {
        var verdict = Fail("A1", Span("Ledde teamet"));

        Fingerprinter(PepperA).Compute(V110, verdict).ShouldNotBe(Fingerprinter(PepperB).Compute(V110, verdict),
            "the pepper is what makes the fingerprint non-recomputable from a leaked ledger. It must be load-bearing.");
    }

    [Fact]
    public void The_fingerprint_is_KEYED_and_is_not_a_plain_SHA256_of_the_payload()
    {
        var verdict = Fail("A1", new StructuralEvidence("kontakt"));
        var payload = FindingTargetFingerprint.BuildCanonicalPayload(V110, verdict);
        var unkeyed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        Fingerprinter(PepperA).Compute(V110, verdict).ShouldNotBe(unkeyed,
            "swap HMAC-SHA256(pepper) for a plain SHA-256 and this goes red — an unkeyed digest of a short "
            + "CV quote is the exact brute-forceable posture #692 removes (security-auditor Q4).");
    }

    [Fact]
    public void The_fingerprint_is_a_full_lowercase_hex_digest()
    {
        var fp = Fingerprinter(PepperA).Compute(V110, Fail("A1", Span("Ledde teamet")));

        fp.ShouldMatch("^[0-9a-f]{64}$");
        fp.Length.ShouldBe(64, "HMAC-SHA256 is 32 bytes = 64 lowercase hex chars — the persisted column contract.");
    }

    // ================================================================================
    // Identity flows through BuildCanonicalPayload (the payload builder is the SSOT for "same finding").
    // ================================================================================

    [Fact]
    public void Structural_and_span_evidence_with_the_same_text_yield_the_same_fingerprint()
    {
        var sut = Fingerprinter(PepperA);

        sut.Compute(V110, Fail("A1", new StructuralEvidence("kontaktsektion saknas")))
            .ShouldBe(sut.Compute(V110, Fail("A1", Span("kontaktsektion saknas"))));
    }

    [Fact]
    public void Different_criteria_yield_different_fingerprints()
    {
        var sut = Fingerprinter(PepperA);

        sut.Compute(V110, Fail("A1", Span("driven")))
            .ShouldNotBe(sut.Compute(V110, Fail("A7", Span("driven"))));
    }

    [Fact]
    public void A_null_verdict_is_refused_by_the_payload_builder()
    {
        Should.Throw<ArgumentNullException>(() => Fingerprinter(PepperA).Compute(V110, null!));
    }

    [Fact]
    public void An_unknown_evidence_type_fails_loud_rather_than_hashing_an_empty_contribution()
    {
        Should.Throw<InvalidOperationException>(() =>
            Fingerprinter(PepperA).Compute(V110, Fail("A1", new UnknownEvidence())));
    }

    // ================================================================================
    // Exact-output golden pin — an independent oracle, not a self-referential goalpost.
    // ================================================================================

    /// <summary>
    /// Under a FIXED pepper (bytes 0..31) and a FIXED finding, the fingerprint is this exact
    /// lowercase-hex string (cross-checked with python3 hmac). Any change to the normalization, the
    /// encoding (UTF-8), the algorithm (HMAC-SHA256), or the hex casing fails loudly HERE rather than
    /// silently re-keying every persisted finding-status row.
    /// </summary>
    [Fact]
    public void A_fixed_pepper_and_finding_produce_this_exact_lowercase_hex_fingerprint()
    {
        var sut = Fingerprinter([.. Enumerable.Range(0, 32).Select(i => (byte)i)]);
        var verdict = Fail("A1", new StructuralEvidence("kontakt"));

        sut.Compute(V110, verdict).ShouldBe(
            "a218002bdc913120bc94956aaf56d7b2364a9f52caef128451698d178fc77cf4",
            "golden HMAC-SHA256(key=0x00..0x1f, msg=UTF8(\"1.1.0\\u001fA1\\u001fkontakt\")) — cross-checked "
            + "with python3 hmac. A future normalisation/encoding/algorithm change must fail HERE, not "
            + "silently in resume_finding_statuses.");
    }
}
