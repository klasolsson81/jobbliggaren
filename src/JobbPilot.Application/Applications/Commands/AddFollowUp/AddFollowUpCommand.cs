using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.AddFollowUp;

public sealed record AddFollowUpCommand(
    Guid ApplicationId,
    string Channel,
    DateTimeOffset ScheduledAt,
    string? Note)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IRequiresFieldEncryptionKey
{
    public string EventType => "Application.FollowUpAdded";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result<Guid> response) => ApplicationId;
}
