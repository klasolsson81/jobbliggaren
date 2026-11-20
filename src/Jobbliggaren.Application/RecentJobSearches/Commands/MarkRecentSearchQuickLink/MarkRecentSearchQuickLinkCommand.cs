using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.MarkRecentSearchQuickLink;

/// <summary>
/// ADR 0188 (#2265) — makes one of the current user's recent searches a quick link in the /jobb hero. Takes the row's
/// id only, never a criterion, so the capture gate stays the only way a search reaches the table.
/// <para>
/// <b>Auditable, without a payload</b> (security-auditor, #2265 plan read Q5): the aggregate and the row's id, as
/// <c>DeleteRecentSearchCommand</c>; never a criterion, label, href or count. Not
/// <see cref="IReplayOnConcurrencyConflict"/>: the handler saves on its own, to answer a slot taken by a racing mark.
/// </para>
/// </summary>
public sealed record MarkRecentSearchQuickLinkCommand(Guid Id)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "RecentJobSearch.QuickLinkMarked";
    public string AggregateType => "RecentJobSearch";
    public Guid ExtractAggregateId(Result response) => Id;
}
