using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.AddNote;

public sealed record AddNoteCommand(
    Guid ApplicationId,
    string? Content)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IRequiresFieldEncryptionKey
{
    public string EventType => "Application.NoteAdded";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result<Guid> response) => ApplicationId;
}
