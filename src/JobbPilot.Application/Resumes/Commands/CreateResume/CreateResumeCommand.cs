using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.CreateResume;

public sealed record CreateResumeCommand(
    string Name,
    string FullName)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "Resume.Created";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
