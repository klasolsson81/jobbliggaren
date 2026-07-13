namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// Server-side pepper for <see cref="HmacIdentifierPseudonymizer"/> (ADR 0090 D5; #842).
/// Bound from the "AuditPseudonymization" section with
/// <see cref="AuditPseudonymizationOptionsValidator"/> + <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// <b>There is no default and there will not be one.</b> A committed default pepper in a PUBLIC
/// repository (ADR 0072) makes every HMAC in <c>audit_log</c> reversible by anyone who clones us —
/// which is strictly worse than storing the email in plaintext, because it would <i>look</i>
/// protected. Supply it via gitignored <c>appsettings.Local.json</c> locally, or a managed secret
/// in ops (CLAUDE.md §5), exactly as <c>FieldEncryption:LocalMasterKeyBase64</c> is supplied.
/// <para>
/// Generate one: <c>openssl rand -base64 32</c>.
/// </para>
/// </remarks>
public sealed class AuditPseudonymizationOptions
{
    public const string SectionName = "AuditPseudonymization";

    /// <summary>
    /// Base64-encoded HMAC-SHA256 pepper, at least 32 bytes. Never logged, never echoed in an
    /// error message, never defaulted.
    /// </summary>
    public string PepperBase64 { get; init; } = string.Empty;
}
