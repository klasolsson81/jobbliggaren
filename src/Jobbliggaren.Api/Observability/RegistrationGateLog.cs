using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.Observability;

/// <summary>
/// ADR 0083 Amendment 2026-08-03 — high-performance (CA1848) source-generated startup announcement of the
/// registration gate. A posture only observable by attempting to register is a posture nobody checks.
/// <para>
/// Level is not cosmetic: CLOSED is routine and logs at Information, while OPEN outside Development is a
/// security-posture statement that should be alertable, so it logs at Warning. Neither carries PII.
/// </para>
/// </summary>
internal static partial class RegistrationGateLog
{
    /// <summary>Announces the gate the host booted with, at the level its posture calls for.</summary>
    public static void AnnounceGate(ILogger logger, bool registrationsOpen, bool isDevelopment)
    {
        if (registrationsOpen && !isDevelopment)
        {
            AnnounceOpenOutsideDevelopment(logger, "OPEN");
        }
        else
        {
            Announce(logger, registrationsOpen ? "OPEN" : "CLOSED");
        }
    }

    [LoggerMessage(EventId = 4300, Level = LogLevel.Information,
        Message = "Registration gate: {RegistrationGateState}")]
    private static partial void Announce(ILogger logger, string registrationGateState);

    // Carries {RegistrationGateState} too, even though this branch only ever fires for OPEN: a Seq query or
    // alert keyed on that property must match BOTH lines, or the only one it finds is the harmless one.
    [LoggerMessage(EventId = 4301, Level = LogLevel.Warning,
        Message = "Registration gate: {RegistrationGateState} outside Development")]
    private static partial void AnnounceOpenOutsideDevelopment(ILogger logger, string registrationGateState);
}
