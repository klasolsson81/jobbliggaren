using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetPrimaryResume;

/// <summary>
/// Markerar en Resume som JobSeekerns primary (Standard-CV). Per ADR 0058 +
/// senior-cto-advisor 2026-05-20 Alt A2: primary-state ägs av JobSeeker-
/// aggregatet, inte Resume. Atomic swap via JobSeeker.SetPrimaryResume.
/// </summary>
public sealed record SetPrimaryResumeCommand(Guid ResumeId)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "JobSeeker.PrimaryResumeSet";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
