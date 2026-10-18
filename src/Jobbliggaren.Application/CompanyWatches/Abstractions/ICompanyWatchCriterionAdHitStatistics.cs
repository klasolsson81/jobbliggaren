namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// ADR 0175 D10 — refreshes the planner's statistics for <c>company_watch_criterion_ad_hits</c> once per completed
/// scan run (AGENTS.md §3.6). The table is bulk-written by one periodic job and its columns reach the count's,
/// the page's and the purge's <c>WHERE</c>; whether autovacuum re-arms on it cannot be shown before it exists on
/// the box, so the scan does not assume it. Its own port and not the scan's: a statistics refresh is not a read
/// of new ads (ISP).
/// </summary>
public interface ICompanyWatchCriterionAdHitStatistics
{
    Task AnalyzeAsync(CancellationToken cancellationToken);
}
