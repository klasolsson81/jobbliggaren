using System.Text.Json;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;

/// <summary>
/// #1975 (ADR 0153) — an administrator starts a change of another account's address. The account does not change
/// here: the new address gets a code that works only after <see cref="AccountEmailChangePolicy.Delay"/>, the account's
/// current address is told at once, and the account's owner completes the change on the public page.
/// <para>
/// Every request costs the administrator a re-authentication of their own account
/// (<see cref="IReauthenticatingRequest"/>), and the admin gate runs before it (<see cref="IAdminRequest"/>), so a
/// caller without the role never spends a grant. One <c>Admin.AccountEmailChangeRequested</c> row per request that
/// reached both inboxes and passed final original-authority admission, with the administrator as its user and the
/// account as its aggregate. Its payload carries only the exact server request identity.
/// </para>
/// </summary>
public sealed record RequestAccountEmailChangeCommand(Guid UserId, string? NewEmail, string? ReauthGrant)
    : ICommand<Result<AccountEmailChangePending>>, IAdminRequest, IReauthenticatingRequest,
        IAuditableCommand<Result<AccountEmailChangePending>>, IOwnsAccountTransaction,
        IAuditPayloadCommand<Result<AccountEmailChangePending>>
{
    public const string RequestedEventType = "Admin.AccountEmailChangeRequested";
    public Guid RequestId { get; } = Guid.NewGuid();
    public string EventType => RequestedEventType;
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<AccountEmailChangePending> response) => UserId;
    public string BuildAuditPayload(Result<AccountEmailChangePending> response, IIdentifierPseudonymizer pseudonymizer) =>
        JsonSerializer.Serialize(new { requestId = RequestId });

    public override string ToString() => $"RequestAccountEmailChangeCommand({UserId}, address and grant redacted)";
}

/// <summary>A started change's two instants: the earliest it can complete, and when its code stops working.</summary>
public sealed record AccountEmailChangePending(DateTimeOffset CompletableFrom, DateTimeOffset ExpiresAt);
