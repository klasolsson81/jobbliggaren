using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.AttachResumeVersion;

/// <summary>
/// F4-11 (BUILD §5.3): link an application to the exact CV version used when
/// applying. The version must belong to the caller's own Resume — the handler
/// rejects cross-user references (IDOR guard). <see cref="IRequiresFieldEncryptionKey"/>
/// because the handler materialises the tracked Application aggregate (its
/// encrypted CoverLetter is decrypted on materialisation, parity with TransitionTo).
/// </summary>
public sealed record AttachResumeVersionCommand(
    Guid ApplicationId,
    Guid ResumeVersionId)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>,
      IRequiresFieldEncryptionKey
{
    public string EventType => "Application.ResumeVersionAttached";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result response) => ApplicationId;
}
