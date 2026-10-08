using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.ScheduleAccountDeletion;

public sealed record ScheduleAccountDeletionCommand(Guid UserId, string? ReauthGrant)
    : ICommand<Result<AccountDeletionScheduled>>, IAdminRequest, IReauthenticatingRequest,
      IAccountAccessMutation, IAuditableCommand<Result<AccountDeletionScheduled>>
{
    public Guid? TargetUserId => UserId;
    public string EventType => "Admin.AccountDeletionScheduled";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<AccountDeletionScheduled> response) => UserId;
    public override string ToString() => $"ScheduleAccountDeletionCommand({UserId}, grant redacted)";
}
