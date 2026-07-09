using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Applications.Commands.BatchTransition;

/// <summary>
/// One requested transition in a batch: which application moves to which
/// status. Per-item targets (not one shared target) so a group-undo — where
/// every application returns to its OWN previous status — is a single atomic
/// call (#630 PR 9 CTO bind Q7).
/// </summary>
public sealed record BatchTransitionItem(Guid ApplicationId, string TargetStatus);

/// <summary>
/// Bulk status change for the Tabell view's bulk actions (#630 PR 10) and its
/// group-undo. CTO bind (2026-07-09, docs/reviews): all-or-nothing two-phase —
/// every application must exist and belong to the caller before ANY of them
/// mutates; a partial batch never persists. Each item routes through
/// <c>Application.TransitionTo</c>, so the ADR 0092 D3 invariants and the D4
/// StatusChange timeline apply per item exactly as on the single endpoint.
/// Audits one row per application via <see cref="IBatchAuditableCommand{T}"/>
/// with the SAME EventType as the single transition, so per-aggregate audit
/// queries see single and batch transitions identically (ADR 0022 parity).
/// </summary>
public sealed record BatchTransitionApplicationsCommand(
    IReadOnlyList<BatchTransitionItem> Items)
    : ICommand<Result>, IAuthenticatedRequest, IBatchAuditableCommand<Result>,
      IRequiresFieldEncryptionKey
{
    public string EventType => "Application.StatusTransitioned";
    public string AggregateType => "Application";

    // Distinct: identical duplicate items are silently deduped by the handler
    // (one real transition), so they must yield one audit row, not two.
    public IReadOnlyList<Guid> ExtractAggregateIds(Result response) =>
        Items.Select(i => i.ApplicationId).Distinct().ToList();
}
