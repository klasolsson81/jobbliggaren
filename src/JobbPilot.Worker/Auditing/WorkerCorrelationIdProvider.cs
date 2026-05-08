using JobbPilot.Application.Common.Auditing;

namespace JobbPilot.Worker.Auditing;

/// <summary>
/// Stub-implementation av <see cref="ICorrelationIdProvider"/> för Worker-context.
/// Per ADR 0022 + ADR 0023 / STEG 9: scope-cachad Guid (en per Hangfire job-execution).
///
/// Hangfire skapar en <c>IServiceScope</c> per jobb-invokation via <c>JobActivator</c>.
/// Scoped DI-livstid garanterar att samma instans (och därmed samma <c>_id</c>) läses
/// genom hela jobbets DI-scope — alla audit-skrivningar från ett enskilt jobb får
/// samma correlation-ID.
///
/// HTTP-versionens fallback (<c>Guid.NewGuid()</c> per anrop) är medvetet INTE
/// återanvänd här — den hade gett unik ID per anrop till <c>Current</c>, vilket
/// skulle bryta correlation-linkning över multipla audit-skrivningar i samma scope.
/// </summary>
public sealed class WorkerCorrelationIdProvider : ICorrelationIdProvider
{
    private readonly Guid _id = Guid.NewGuid();
    public Guid Current => _id;
}
