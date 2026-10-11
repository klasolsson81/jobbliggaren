using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetTopMatchMail;

/// <summary>
/// ADR 0164 (#2107) — turns the current user's Toppmatch mail on or off. A pause inside the consents, never a consent or a withdrawal:
/// <see cref="JobSeeker.SetTopMatchMail"/> writes only its own stamp. Owner-scoped; one control, one write, so a
/// replay after a concurrency conflict re-applies only this value (ADR 0146).
/// <para>
/// <b>Required on the wire:</b> a body without <c>enabled</c> is a 400, never a bound <c>false</c>.
/// </para>
/// <para>
/// <b>Auditable (GDPR Art. 5(2)):</b> the audit row carries the requested <c>enabled</c>, so whether a
/// mail was sent or held back can be reconstructed.
/// </para>
/// </summary>
public sealed record SetTopMatchMailCommand([property: JsonRequired] bool Enabled)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IAuditPayloadCommand<Result<Guid>>, IReplayOnConcurrencyConflict
{
    // Stable, append-only event name (audit queries depend on it).
    public string EventType => "JobSeeker.TopMatchMailUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;

    public string? BuildAuditPayload(Result<Guid> response, IIdentifierPseudonymizer pseudonymizer)
        => JsonSerializer.Serialize(new Dictionary<string, bool> { ["enabled"] = Enabled });
}
