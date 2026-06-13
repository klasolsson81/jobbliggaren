using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Commands.RedactRecruiterPii;

/// <summary>
/// GDPR Art. 17 — Right to erasure för rekryterar-PII i <c>job_ads.raw_payload</c>.
/// Per ADR 0032 §8 amendment 2026-05-13 + senior-cto-advisor-decision 2026-05-13.
///
/// <para>
/// <b>Mekanik (CTO Q2 = total null-out):</b> matchande rader får <c>raw_payload = NULL</c>
/// via <c>ExecuteUpdateAsync(SetProperty(j =&gt; j.RawPayload, _ =&gt; null))</c>.
/// GDPR Art. 5(1)(c) data-minimisation > debug-värde. Sanitizer + 30d-retention
/// minimerar redan PII-fönstret (ADR 0032 §8 amendment 2026-05-12).
/// </para>
///
/// <para>
/// <b>Audit-granularitet (CTO Q3 = aggregerad rad per request):</b> EN audit-rad
/// per command-anrop med payload <c>{ identifier, type, rowsAffected }</c>. Speglar
/// ADR 0024 D4-precedens ("användaren begärde en handling, inte 100").
/// </para>
///
/// <para>
/// <b>AggregateId:</b> per-request-Guid (genererad i constructor) — system-events
/// har ingen aggregate-root, AggregateId bevarar Guid.Empty-invarianten via unik
/// per-anrop-Guid. <c>AggregateType = "System.RecruiterPiiRedaction"</c>.
/// </para>
///
/// <para>
/// <b>Authorization:</b> <see cref="IAdminRequest"/> + endpoint
/// <c>RequireAuthorization(AuthorizationPolicies.Admin)</c>.
/// Defense-in-depth via <c>AdminAuthorizationBehavior</c>.
/// </para>
/// </summary>
public sealed record RedactRecruiterPiiCommand(
    string Identifier,
    RecruiterIdentifierType Type)
    : ICommand<Result<int>>, IAdminRequest, IAuditableCommand<Result<int>>
{
    /// <summary>
    /// Per-request-Guid för audit-rad. Initieras vid command-konstruktion så att
    /// både request-receipt och audit-rad delar samma identifier.
    /// </summary>
    public Guid RequestId { get; } = Guid.NewGuid();

    public string EventType => "Admin.RecruiterPiiRedacted";
    public string AggregateType => "System.RecruiterPiiRedaction";

    public Guid ExtractAggregateId(Result<int> response) => RequestId;
}
