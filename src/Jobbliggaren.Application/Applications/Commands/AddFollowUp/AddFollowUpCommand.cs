using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.AddFollowUp;

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
