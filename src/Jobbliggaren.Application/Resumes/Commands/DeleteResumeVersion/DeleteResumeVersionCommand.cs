using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.DeleteResumeVersion;

public sealed record DeleteResumeVersionCommand(
    Guid ResumeId,
    Guid VersionId)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    // AggregateType = "Resume" (inte "ResumeVersion") — ResumeVersion är inte aggregate root.
    // VersionId loggas inte i audit Fas 1 (skulle kräva payload-fältet, ADR 0022 deferrar det).
    public string EventType => "Resume.VersionDeleted";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result response) => ResumeId;
}
