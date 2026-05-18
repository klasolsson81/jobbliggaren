using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.RecordFollowUpOutcome;

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
