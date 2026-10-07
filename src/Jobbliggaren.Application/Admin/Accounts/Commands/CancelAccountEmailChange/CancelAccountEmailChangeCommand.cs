using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.CancelAccountEmailChange;

/// <summary>
/// #1975 (ADR 0153) — an administrator cancels the account's pending address change, for instance on an objection from
/// its current address. No re-authentication: cancelling only removes exposure. A cancel that finds nothing to remove
/// is a failure, so it writes no <c>Admin.AccountEmailChangeCancelled</c> row: a write that changes nothing claims no
/// change.
/// </summary>
public sealed record CancelAccountEmailChangeCommand(Guid UserId)
    : ICommand<Result>, IAdminRequest, IAuditableCommand<Result>
{
    public string EventType => "Admin.AccountEmailChangeCancelled";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result response) => UserId;
}
