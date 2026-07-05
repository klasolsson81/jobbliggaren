using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.LogFollowUp;

// ADR 0092 D4/D5: the "Logga uppföljning" quick action — log a completed contact
// today (note optional). Unlike AddFollowUp (which schedules a future reminder with
// a channel + date), this needs only a note; the aggregate defaults channel = Other,
// scheduled = now, outcome = Logged, and bumps LastFollowUpAt so the wait resets.
// Note is encrypted (IRequiresFieldEncryptionKey warms the owner DEK), audited
// (IAuditableCommand), owner-scoped in the handler.
public sealed record LogFollowUpCommand(
    Guid ApplicationId,
    string? Note)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IRequiresFieldEncryptionKey
{
    public string EventType => "Application.FollowUpLogged";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result<Guid> response) => ApplicationId;
}
