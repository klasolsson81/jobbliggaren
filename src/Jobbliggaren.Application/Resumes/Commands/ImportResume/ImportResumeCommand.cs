using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.ImportResume;

/// <summary>
/// Imports an uploaded CV (PDF/DOCX) and parses it deterministically into a
/// <c>ParsedResume</c> staging artifact (F4-8, ADR 0074 — NO AI/LLM).
///
/// <para><see cref="IRequiresFieldEncryptionKey"/> is mandatory: the handler persists
/// the parsed content + raw text as encrypted CV-PII (Invariant 3), so the owner DEK
/// must be warmed by <c>FieldEncryptionKeyPrefetchBehavior</c> before the SaveChanges
/// interceptor encrypts the shadows.</para>
/// </summary>
public sealed record ImportResumeCommand(
    string FileName,
    string ContentType,
    ReadOnlyMemory<byte> FileBytes)
    : ICommand<Result<ImportResumeResponse>>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result<ImportResumeResponse>>
{
    public string EventType => "Resume.Imported";

    public string AggregateType => "ParsedResume";

    public Guid ExtractAggregateId(Result<ImportResumeResponse> response) =>
        response.IsSuccess ? response.Value.ParsedResumeId : Guid.Empty;
}
