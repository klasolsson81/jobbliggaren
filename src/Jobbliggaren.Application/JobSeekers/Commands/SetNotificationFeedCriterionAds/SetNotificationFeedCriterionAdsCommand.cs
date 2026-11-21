using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetNotificationFeedCriterionAds;

/// <summary>
/// ADR 0175 D8 (#1663) — turns "new ads from a Branschbevakning reach the notification bell" on or off for the current
/// user, with <c>SetNotificationFeedMatchesCommand</c>'s rules: a delivery choice, never a consent
/// (<see cref="JobSeeker.SetNotificationFeedCriterionAds"/> writes only its own stamp), required on the wire, audited with
/// <c>{"enabled": bool}</c> and nothing else, replayed on a concurrency conflict. It stops the bell only: the scan, the
/// hits, Översikt's count and the page of new ads go on. Switching ON first marks what arrived while it was off as read,
/// by the endpoint before this command.
/// </summary>
public sealed record SetNotificationFeedCriterionAdsCommand([property: JsonRequired] bool Enabled)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IAuditPayloadCommand<Result<Guid>>, IReplayOnConcurrencyConflict
{
    // Stable, append-only event name (audit queries depend on it).
    public string EventType => "JobSeeker.NotificationFeedCriterionAdsUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;

    public string? BuildAuditPayload(Result<Guid> response, IIdentifierPseudonymizer pseudonymizer)
        => JsonSerializer.Serialize(new Dictionary<string, bool> { ["enabled"] = Enabled });
}
