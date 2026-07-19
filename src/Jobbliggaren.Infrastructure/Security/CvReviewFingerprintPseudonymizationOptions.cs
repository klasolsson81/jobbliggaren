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
/// <b>Dual-host (both the Api AND the Worker boot this section).</b> <c>AddCvReview</c> is reached by
/// <c>AddJobSources</c>, the one module BOTH hosts pass (Api via <c>AddInfrastructure</c>, Worker
/// directly in <c>Worker/Program.cs</c>) — so both processes register this options section and its
/// <c>ValidateOnStart</c>, and <b>both must provision the pepper in prod</b> (parity the company-watch
/// pepper #544, which is likewise dual-host). Only the Api actually COMPUTES a finding fingerprint,
/// but <c>AddCvReview</c> also registers the dual-host <see cref="Jobbliggaren.Application.Resumes.Review.Abstractions.IResumeReviewReconciler"/>,
/// which depends on <see cref="Jobbliggaren.Application.Resumes.Review.Abstractions.IFindingFingerprinter"/> —
/// so the hasher (and thus this pepper) is a Worker boot requirement too, even though no Worker job
/// invokes it. A missing pepper fail-closes BOTH hosts at startup.
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
