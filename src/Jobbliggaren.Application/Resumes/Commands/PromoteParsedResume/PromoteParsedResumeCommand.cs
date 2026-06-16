using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;

/// <summary>
/// Promotes a <c>PendingReview</c> <c>ParsedResume</c> staging artifact (F4-8) into a
/// canonical <c>Resume</c> with a strict-valid Master version (Fas 4 STEG A — NO AI/LLM,
/// ADR 0071/0074). The full, user-approved gap-filled content rides on
/// <see cref="Content"/> (CTO DQ1 Variant A — the approved content IS the Resume; the
/// backend never merges or synthesises from the parse, CLAUDE.md §5). Returns the new
/// Resume's id.
///
/// <para><see cref="IRequiresFieldEncryptionKey"/> is mandatory: the handler writes the
/// Master content as encrypted CV-PII (<c>resume_versions.content_enc</c>, Form B), so the
/// owner DEK must be warmed by <c>FieldEncryptionKeyPrefetchBehavior</c> before the
/// SaveChanges interceptor encrypts (ADR 0074 Invariant 3).</para>
/// </summary>
public sealed record PromoteParsedResumeCommand(
    Guid ParsedResumeId,
    string Name,
    ResumeContentDto Content)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result<Guid>>
{
    public string EventType => "Resume.PromotedFromParsed";

    public string AggregateType => "Resume";

    public Guid ExtractAggregateId(Result<Guid> response) =>
        response.IsSuccess ? response.Value : Guid.Empty;
}
