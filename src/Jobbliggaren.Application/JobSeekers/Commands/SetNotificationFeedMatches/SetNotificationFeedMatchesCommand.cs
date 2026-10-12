using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetNotificationFeedMatches;

/// <summary>
/// ADR 0162 (#2130) — turns "new matches reach the notification bell" on or off for the current user. A delivery
/// choice, never a consent or a withdrawal: <see cref="JobSeeker.SetNotificationFeedMatches"/> writes only its own
/// stamp (security V3), and the command carries no consent field, so a replay after a concurrency conflict re-applies
/// only this value (ADR 0146). Owner-scoped; one control, one write.
/// <para>
/// <b>Required on the wire:</b> a body without <c>enabled</c> is a 400, never a bound <c>false</c>.
/// </para>
/// <para>
/// <b>Auditable (GDPR Art. 5(2)):</b> the audit row carries the requested <c>enabled</c> and nothing else (no stamp,
/// no count of rows marked), so what reached the bell can be reconstructed.
/// </para>
/// <para>
/// Switching ON first marks what arrived while it was off as read. That mark is NOT in this command or its handler: it
/// is a bulk write, which a replayed command may not make, and the endpoint sends it before this one.
/// </para>
/// </summary>
public sealed record SetNotificationFeedMatchesCommand([property: JsonRequired] bool Enabled)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IAuditPayloadCommand<Result<Guid>>, IReplayOnConcurrencyConflict
{
    // Stable, append-only event name (audit queries depend on it).
    public string EventType => "JobSeeker.NotificationFeedMatchesUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;

    public string? BuildAuditPayload(Result<Guid> response, IIdentifierPseudonymizer pseudonymizer)
        => JsonSerializer.Serialize(new Dictionary<string, bool> { ["enabled"] = Enabled });
}
