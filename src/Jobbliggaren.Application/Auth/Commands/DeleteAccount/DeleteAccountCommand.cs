using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.DeleteAccount;

/// <summary>Owner-initiated scheduling; the HTTP contract remains no content after a known commit.</summary>
public sealed record DeleteAccountCommand(string? ReauthGrant)
    : ICommand<Result<AccountDeletionScheduled>>, IAuthenticatedRequest, IReauthenticatingRequest,
      IAuditableCommand<Result<AccountDeletionScheduled>>, IAccountAccessMutation
{
    public Guid? TargetUserId => null;
    public string EventType => "Account.Deleted";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<AccountDeletionScheduled> response) => response.Value.ProfileId;
    public override string ToString() => "DeleteAccountCommand(grant redacted)";
}
