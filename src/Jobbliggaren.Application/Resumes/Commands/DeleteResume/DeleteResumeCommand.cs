using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.DeleteResume;

public sealed record DeleteResumeCommand(Guid ResumeId)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "Resume.Deleted";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result response) => ResumeId;
}
