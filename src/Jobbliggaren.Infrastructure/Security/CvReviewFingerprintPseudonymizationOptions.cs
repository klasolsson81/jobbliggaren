namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// Server-side pepper for <see cref="HmacFindingFingerprinter"/> (#692, ADR 0093 §D2(e)) — the
/// at-rest key for <c>resume_finding_statuses.target_fingerprint</c>, a keyed HMAC-SHA256 over the
/// (rubric version + criterion id + normalized evidence) canonical payload. Bound from the
/// "CvReviewFingerprintPseudonymization" section with
/// <see cref="CvReviewFingerprintPseudonymizationOptionsValidator"/> + <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// <b>SEPARATE from the audit pepper (<see cref="AuditPseudonymizationOptions"/>) and the
/// company-watch pepper (<see cref="CompanyWatchPseudonymizationOptions"/>) — one key, one purpose
/// (security-auditor B1).</b> The evidence hashed here is short user-written CV quotes (already
/// personnummer-redacted pre-hash); if the ledger table leaks in isolation, an UNKEYED digest of
/// such short quotes is dictionary/brute-forceable. Coupling this key to another purpose is exactly
/// the accident the separation prevents; a leak/rotation of one must never implicate the other.
/// <para>
/// <b>There is no default and there will not be one.</b> A committed default pepper in a PUBLIC
/// repository (ADR 0072) makes every fingerprint recomputable by anyone who clones us — strictly
/// worse than plain SHA-256, because it would <i>look</i> keyed. Supply it via gitignored
/// <c>appsettings.Local.json</c> locally, or a managed secret in ops (CLAUDE.md §5), exactly as
/// <c>AuditPseudonymization:PepperBase64</c>, <c>CompanyWatchPseudonymization:PepperBase64</c> and
/// <c>FieldEncryption:LocalMasterKeyBase64</c> are. Generate one: <c>openssl rand -base64 32</c>.
/// </para>
/// <para>
/// <b>Api-host-only (dotnet-architect D4).</b> The CV-review path
/// (<c>IResumeReviewReconciler</c>/the review + improve handlers) lives in <c>AddCvReview</c>, which
/// the Api calls and the Worker does not — no Worker job computes a finding fingerprint. So only the
/// Api process (and the Api integration test host) boots this section; the Worker never needs this
/// pepper. This is the mirror-opposite of the company-watch pepper, whose <c>CompanyWatchScanJob</c>
/// IS Worker-resident. (The Worker <i>integration test</i> fixture does construct the graph via
/// <c>AddCvReview</c>, so that fixture — not the real Worker — supplies a test pepper.)
/// </para>
/// </remarks>
public sealed class CvReviewFingerprintPseudonymizationOptions
{
    public const string SectionName = "CvReviewFingerprintPseudonymization";

    /// <summary>
    /// Base64-encoded HMAC-SHA256 pepper, at least 32 bytes. Never logged, never echoed in an
    /// error message, never defaulted.
    /// </summary>
    public string PepperBase64 { get; init; } = string.Empty;
}
