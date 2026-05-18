using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.TransitionTo;

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
