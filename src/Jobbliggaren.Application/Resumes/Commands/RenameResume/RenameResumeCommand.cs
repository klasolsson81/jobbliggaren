using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.RenameResume;

public sealed record RenameResumeCommand(
    Guid ResumeId,
    string Name)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "Resume.Renamed";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result response) => ResumeId;
}
