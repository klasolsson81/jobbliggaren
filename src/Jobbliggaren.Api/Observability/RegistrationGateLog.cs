using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.Observability;

/// <summary>
/// ADR 0083 Amendment 2026-08-03 — high-performance (CA1848) source-generated startup announcement of
/// the two auth-flow flags that together decide what a public registration actually does.
/// <para>
/// BOTH are announced deliberately. Either one alone is misleading: an OPEN gate with email
/// confirmation OFF is legacy instant-login — an account minted with no proof the registrant owns the
/// address — which is exactly the posture issue #734 exists to prevent. Announcing only the gate would
/// reproduce the very defect this class was added for, one flag over.
/// </para>
/// <para>
/// The measured motivation: <c>Auth:RequireEmailConfirmation</c> is declared without an initialiser
/// and the <c>Auth</c> section exists only in <c>appsettings.Development.json</c>, so in the Production
/// configuration the handler WOULD take the legacy instant-login branch — and nothing in the boot
/// sequence said so. (No Production host has booted yet; this is a property of the configuration, not
/// a history.) A posture only observable by attempting to register is a posture nobody checks.
/// </para>
/// <para>
/// Level is not cosmetic: CLOSED is routine and logs at Information, while OPEN outside Development is
/// a security-posture statement that should be alertable, so it logs at Warning. Neither carries PII.
/// </para>
/// </summary>
internal static partial class RegistrationGateLog
{
    [LoggerMessage(EventId = 4300, Level = LogLevel.Information,
        Message = "Registration gate: {RegistrationGateState}; email confirmation: {EmailConfirmationState}")]
    public static partial void Announce(
        ILogger logger, string registrationGateState, string emailConfirmationState);

    [LoggerMessage(EventId = 4301, Level = LogLevel.Warning,
        Message = "Registration gate: OPEN outside Development; email confirmation: {EmailConfirmationState}")]
    public static partial void AnnounceOpenOutsideDevelopment(
        ILogger logger, string emailConfirmationState);
}
