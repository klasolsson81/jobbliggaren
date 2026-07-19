using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256(fingerprint pepper) over the canonical finding payload (#692, ADR 0093 §D2(e)). The
/// keyed at-rest identity for <c>resume_finding_statuses.target_fingerprint</c>. Shares the
/// keyed-HMAC-SHA256 <i>mechanism</i> with <see cref="HmacProtectedIdentityTokenizer"/> and
/// <see cref="HmacIdentifierPseudonymizer"/> but is a distinct port with its OWN pepper and its own
/// input — see <see cref="IFindingFingerprinter"/> for why they must not be one.
/// </summary>
/// <remarks>
/// The canonicalization (NFC fold, evidence sort, unit-separator join) stays in the Application layer
/// (<see cref="FindingTargetFingerprint.BuildCanonicalPayload"/>) under its existing tests — this
/// adapter adds only the key. Singleton: the pepper is read once at construction and the instance is
/// stateless thereafter. The pepper is never logged and never surfaced in an exception message
/// (CLAUDE.md §5).
/// </remarks>
internal sealed class HmacFindingFingerprinter : IFindingFingerprinter
{
    private readonly byte[] _pepper;

    public HmacFindingFingerprinter(IOptions<CvReviewFingerprintPseudonymizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Validated at startup by CvReviewFingerprintPseudonymizationOptionsValidator
        // (ValidateOnStart), so a malformed or missing pepper never reaches this constructor.
        _pepper = Convert.FromBase64String(options.Value.PepperBase64);
    }

    public string Compute(RubricVersion rubricVersion, CvCriterionVerdict verdict)
    {
        // BuildCanonicalPayload owns the ArgumentNullException.ThrowIfNull(verdict) + the
        // unknown-evidence fail-loud — the Application layer is the SSOT for what a finding IS.
        var payload = FindingTargetFingerprint.BuildCanonicalPayload(rubricVersion, verdict);
        var hash = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
