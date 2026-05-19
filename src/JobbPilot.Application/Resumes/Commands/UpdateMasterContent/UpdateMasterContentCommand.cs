using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Security;
using JobbPilot.Application.Resumes.Queries;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.UpdateMasterContent;

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
