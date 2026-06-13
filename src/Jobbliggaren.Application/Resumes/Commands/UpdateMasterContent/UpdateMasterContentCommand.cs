using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;

// IRequiresFieldEncryptionKey: materialiserar befintliga (krypterade)
// ResumeVersion-rader via Include OCH skriver ny Content →
// FieldEncryptionKeyPrefetchBehavior måste värma ägar-DEK före både
// decrypt-on-read och encrypt-on-write (ADR 0049 Mekanik-not 4/5/6).
public sealed record UpdateMasterContentCommand(
    Guid ResumeId,
    ResumeContentDto Content)
    : ICommand<Result>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result>
{
    public string EventType => "Resume.MasterContentUpdated";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result response) => ResumeId;
}
