using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.AddNote;

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
