namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// Server-side pepper for <see cref="HmacProtectedIdentityTokenizer"/> (#544, ADR 0090 D5) — the
/// at-rest token key for a personnummer-shaped (enskild-firma) <c>company_watches.organization_number</c>.
/// Bound from the "CompanyWatchPseudonymization" section with
/// <see cref="CompanyWatchPseudonymizationOptionsValidator"/> + <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// <b>SEPARATE from the <see cref="AuditPseudonymizationOptions"/> pepper — one key, one purpose
/// (security-auditor B1).</b> That pepper pseudonymises an erasure-audit <i>log entry</i> and
/// <i>tolerates</i> rotation (soft degradation). This one keys a <i>live at-rest identifier</i> and
/// is <b>permanent, non-rotatable</b> (R1, Klas Art. 32 accept-extension 2026-07-18): the
/// destroy-in-place backfill discards the plaintext, so no rotation can re-key existing tokens.
/// Coupling a rotation-tolerant key to a rotation-fatal use is exactly the accident this separation
/// prevents; a leak/rotation of one must never implicate the other.
/// <para>
/// <b>There is no default and there will not be one.</b> A committed default pepper in a PUBLIC
/// repository (ADR 0072) makes every token reversible by anyone who clones us — strictly worse than
/// plaintext, because it would <i>look</i> protected. Supply it via gitignored
/// <c>appsettings.Local.json</c> locally, or a managed secret in ops (CLAUDE.md §5), exactly as
/// <c>AuditPseudonymization:PepperBase64</c> and <c>FieldEncryption:LocalMasterKeyBase64</c> are.
/// Generate one: <c>openssl rand -base64 32</c>.
/// </para>
/// </remarks>
public sealed class CompanyWatchPseudonymizationOptions
{
    public const string SectionName = "CompanyWatchPseudonymization";

    /// <summary>
    /// Base64-encoded HMAC-SHA256 pepper, at least 32 bytes. Never logged, never echoed in an
    /// error message, never defaulted.
    /// </summary>
    public string PepperBase64 { get; init; } = string.Empty;
}
