using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.DeleteResumeVersion;

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
