using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.RecordFollowUpOutcome;

public sealed record RecordFollowUpOutcomeCommand(
    Guid ApplicationId,
    Guid FollowUpId,
    string Outcome)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>,
      IRequiresFieldEncryptionKey
{
    public string EventType => "Application.FollowUpOutcomeRecorded";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result response) => ApplicationId;
}
