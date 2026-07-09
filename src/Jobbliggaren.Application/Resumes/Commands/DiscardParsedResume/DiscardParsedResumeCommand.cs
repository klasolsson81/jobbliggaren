using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.DiscardParsedResume;

/// <summary>
/// Discards a <c>PendingReview</c> <c>ParsedResume</c> staging artifact (Fas 4b PR-8,
/// CTO-bind Q6 — the hub action card's "Ta bort utkastet"). A soft-delete state
/// transition through <c>ParsedResume.Discard</c> (Status=Discarded + DeletedAt; the
/// retention sweep hard-deletes later), exposed as <c>POST .../discard</c> — an action,
/// parity with <c>/promote</c>, never a DELETE that would imply hard removal. Audited:
/// destroying a PII staging artifact is an accountable user action (parity with
/// promote's audit row).
///
/// <para><see cref="IRequiresFieldEncryptionKey"/> is required because the handler
/// materialises the aggregate (the decryption interceptor runs on the CV-PII shadows),
/// even though the handler itself never reads the decrypted content.</para>
/// </summary>
public sealed record DiscardParsedResumeCommand(Guid ParsedResumeId)
    : ICommand<Result>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result>
{
    public string EventType => "ParsedResume.Discarded";

    public string AggregateType => "ParsedResume";

    public Guid ExtractAggregateId(Result response) => ParsedResumeId;
}
