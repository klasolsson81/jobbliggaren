using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.DeleteApplication;

/// <summary>
/// #782 (ADR 0104) — user-initiated per-application HARD delete. Deliberately NOT
/// <c>IRequiresFieldEncryptionKey</c>: the row is deleted, never materialized for
/// display, so the scope-differentiated decryption interceptor leaves the ciphertext
/// untouched (the same key-free mechanism <c>AccountHardDeleter</c> relies on).
/// </summary>
public sealed record DeleteApplicationCommand(Guid Id)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "Application.Deleted";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result response) => Id;
}
