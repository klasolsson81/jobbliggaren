using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;

/// <summary>
/// <para><see cref="IRequiresFieldEncryptionKey"/> since Fas 4b PR-8 (CTO-bind Q1): the
/// handler reconciles the finding-status ledger, which reads the decrypted master
/// content (the language drives the review) — without the warmed owner DEK the
/// materialization interceptor cannot decrypt the Form B shadow.</para>
/// </summary>
public sealed record SetResumeLanguageCommand(
    Guid ResumeId,
    string Language)
    : ICommand<Result>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result>
{
    public string EventType => "Resume.LanguageChanged";
    public string AggregateType => "Resume";
    public Guid ExtractAggregateId(Result response) => ResumeId;
}
