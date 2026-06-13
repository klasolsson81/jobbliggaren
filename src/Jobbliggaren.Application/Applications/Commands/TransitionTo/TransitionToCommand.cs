using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.TransitionTo;

public sealed record TransitionToCommand(
    Guid ApplicationId,
    string TargetStatus)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>,
      IRequiresFieldEncryptionKey
{
    public string EventType => "Application.StatusTransitioned";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result response) => ApplicationId;
}
