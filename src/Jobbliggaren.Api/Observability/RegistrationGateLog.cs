using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.Observability;

/// <summary>
/// ADR 0083 Amendment 2026-08-03 — high-performance (CA1848) source-generated startup announcement of
/// the public-registration kill-switch.
/// <para>
/// This exists because its absence was measured: <c>Auth:RequireEmailConfirmation</c> is declared
/// without an initialiser and the key lives only in <c>appsettings.Development.json</c>, so Production
/// silently ran the legacy instant-login branch and nothing said so at boot. A registration gate that
/// can only be observed by attempting to register is a gate nobody checks. One line per process, at
/// Information, carrying no PII.
/// </para>
/// </summary>
internal static partial class RegistrationGateLog
{
    [LoggerMessage(EventId = 4300, Level = LogLevel.Information,
        Message = "Registration gate: {RegistrationGateState}")]
    public static partial void Announce(ILogger logger, string registrationGateState);
}
