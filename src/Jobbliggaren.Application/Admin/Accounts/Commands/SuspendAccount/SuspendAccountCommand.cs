using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.SuspendAccount;

public sealed record SuspendAccountCommand(Guid UserId, string? ReauthGrant)
    : ICommand<Result<AccountAccessChanged>>, IAdminRequest, IReauthenticatingRequest,
        IAccountAccessMutation, IAuditableCommand<Result<AccountAccessChanged>>
{
    public Guid? TargetUserId => UserId;
    public string EventType => "Admin.AccountSuspended";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<AccountAccessChanged> response) => UserId;
    public override string ToString() => $"SuspendAccountCommand({UserId}, grant redacted)";
}
