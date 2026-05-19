using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.CreateResume;

// IRequiresFieldEncryptionKey: skapar Master-ResumeVersion med Content —
// FieldEncryptionSaveChangesInterceptor krypterar Content→content_enc (#1c)
// och kräver varm ägar-DEK i scope-cachen (ADR 0049 Mekanik-not 5/6).
public sealed record CreateResumeCommand(
    string Name,
    string FullName)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result<Guid>>
{
    public string EventType => "Resume.Created";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
