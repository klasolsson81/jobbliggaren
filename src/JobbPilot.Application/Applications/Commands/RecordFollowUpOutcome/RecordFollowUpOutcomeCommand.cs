using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.RecordFollowUpOutcome;

public sealed record RecordFollowUpOutcomeCommand(
    Guid ApplicationId,
    Guid FollowUpId,
    string Outcome)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "Application.FollowUpOutcomeRecorded";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result response) => ApplicationId;
}
