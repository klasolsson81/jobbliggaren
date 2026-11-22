using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.UnmarkRecentSearchQuickLink;

/// <summary>
/// ADR 0188 (#2265) — the user's recent search stops being a quick link and is evictable again; the search itself stays.
/// Takes the row's id only. Auditable without a payload, as <c>DeleteRecentSearchCommand</c>. Replayed after a
/// concurrency conflict (ADR 0146): a row deleted meanwhile re-reads as a 404.
/// </summary>
public sealed record UnmarkRecentSearchQuickLinkCommand(Guid Id)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>, IReplayOnConcurrencyConflict
{
    public string EventType => "RecentJobSearch.QuickLinkUnmarked";
    public string AggregateType => "RecentJobSearch";
    public Guid ExtractAggregateId(Result response) => Id;
}
