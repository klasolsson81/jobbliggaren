using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.CreateApplication;

public sealed record CreateApplicationCommand(
    Guid? JobAdId,
    string? CoverLetter)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "Application.Created";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
