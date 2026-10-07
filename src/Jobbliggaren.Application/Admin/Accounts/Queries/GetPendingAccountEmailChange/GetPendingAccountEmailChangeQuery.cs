using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;

/// <summary>
/// #1975 (ADR 0153) — the account's pending address change as the panel shows it, or null when it has none. Its own
/// read, so a fault on the volatile instance costs this one fact and never the account's details (ADR 0150 D2).
/// </summary>
public sealed record GetPendingAccountEmailChangeQuery(Guid UserId)
    : IQuery<PendingAccountEmailChangeDto?>, IAdminRequest;

/// <summary>Whether the code still works, and the two instants. No address: the panel needs none to name the state.</summary>
public sealed record PendingAccountEmailChangeDto(
    PendingAccountEmailChangeState State,
    DateTimeOffset CompletableFrom,
    DateTimeOffset ExpiresAt);
