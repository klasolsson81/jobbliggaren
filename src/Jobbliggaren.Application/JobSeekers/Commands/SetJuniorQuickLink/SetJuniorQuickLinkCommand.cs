using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetJuniorQuickLink;

/// <summary>
/// ADR 0188 (#2265) — shows or hides the "Juniora IT-jobb" quick link in the /jobb hero for the current user. A display
/// choice: <see cref="JobSeeker.SetJuniorQuickLink"/> writes only its own stamp, so a replay after a concurrency
/// conflict re-applies only this value (ADR 0146). Owner-scoped; one control, one write.
/// <para>
/// <b>Required on the wire:</b> a body without <c>enabled</c> is a 400, never a bound <c>false</c>.
/// </para>
/// <para>
/// <b>Auditable (GDPR Art. 5(2)):</b> the audit row carries the requested <c>enabled</c> and nothing else
/// (security-auditor, #2265 plan read Q5).
/// </para>
/// </summary>
public sealed record SetJuniorQuickLinkCommand([property: JsonRequired] bool Enabled)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IAuditPayloadCommand<Result<Guid>>, IReplayOnConcurrencyConflict
{
    // Stable, append-only event name (audit queries depend on it).
    public string EventType => "JobSeeker.JuniorQuickLinkUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;

    public string? BuildAuditPayload(Result<Guid> response, IIdentifierPseudonymizer pseudonymizer)
        => JsonSerializer.Serialize(new Dictionary<string, bool> { ["enabled"] = Enabled });
}
