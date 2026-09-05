using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.BrowseCriterionAds;

/// <summary>
/// #1559 — loads the user's criterion (owner-scoped, ADR 0031 probe via
/// <see cref="CriterionOwnerScopedLoader"/>), asks the port which ACTIVE ads its companies posted,
/// then loads and projects those ads through <see cref="IAppDbContext"/>.
///
/// <para>
/// <b>Two round-trips, and the split is the firewall.</b> The port answers with ad IDS because the
/// JOIN is the only half that needs <c>company_register</c> — which is not a <c>DbSet</c> on
/// <see cref="IAppDbContext"/> (DPIA C-D4 / M-C5) and therefore unreachable from here. Projecting the
/// ad columns inside the register's Infrastructure file instead would give the job-ad read shape a
/// second home beside the Application-side projection every other ad surface uses.
/// </para>
///
/// <para>
/// <b>The page is re-sequenced by the port's ORDINAL, never by a re-derived key.</b>
/// <c>WHERE id = ANY(...)</c> does not preserve the array's order, so the loaded rows must be put
/// back in the order the port published — and the only faithful way is to follow
/// <c>page.Items</c> itself. Every attempt to re-derive it produces a DIFFERENT order: it cannot be
/// expressed in the query at all (<c>ThenBy(j =&gt; j.Id)</c> throws in-process, <c>j.Id.Value</c>
/// 500s against Postgres, <c>EF.Property&lt;Guid&gt;</c> binds the column on one provider and the CLR
/// property on the other), and sorting in memory silently introduces a THIRD one — Postgres compares
/// <c>uuid</c> bytewise while <c>Guid.CompareTo</c> reads the first field as a signed <c>Int32</c>,
/// so the two disagree on about half of all pairs.
/// </para>
///
/// <para>
/// <b>#1656 (b) — the "bara matchande" arm.</b> When
/// <see cref="BrowseCriterionAdsQuery.OnlyMatching"/> is set, the page comes from
/// <see cref="CriterionMatchingAdSet"/> instead: the WHOLE matching set is resolved first and the
/// PAGE is cut from it. Filtering the loaded page would be a different (and false) surface — at
/// pageSize 20 it would describe the page rather than the watch, which ADR 0120 forbids and which
/// would make "N matchar dig" disagree with what the user lands on.
/// </para>
///
/// <para>
/// <b>The filter is INERT rather than empty in the two unanswerable arms</b> (parity the follow
/// rail's RF-5 under-fork (i)): a user who has stated no occupation, and a criterion too broad to
/// grade, both get the UNFILTERED list. Returning an empty page there would say "nothing matches
/// you", which is not what either arm means. The composed response's <c>Matching</c> member is what
/// tells the surface which arm it is in, so it can say so instead of implying a zero.
/// </para>
///
/// <para>
/// <b>No org.nr crosses this handler.</b> The port returns ad ids and <see cref="JobAdDto"/> carries
/// no organisation number, so the personnummer guard (ADR 0087 D8(c)) has nothing to mask here — the
/// raw org.nr of a matched sole trader stays server-side inside the join, exactly as it does for the
/// counts. The ads themselves are public Platsbanken data.
/// </para>
/// </summary>
public sealed class BrowseCriterionAdsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    ICompanyWatchBrowseQuery browse,
    IPerUserJobAdSearchQuery perUserSearch,
    IMatchProfileBuilder profileBuilder)
    : IQueryHandler<BrowseCriterionAdsQuery, PagedResult<JobAdDto>?>
{
    public async ValueTask<PagedResult<JobAdDto>?> Handle(
        BrowseCriterionAdsQuery query, CancellationToken cancellationToken)
    {
        var criterion = await CriterionOwnerScopedLoader.LoadForCurrentUserAsync(
            db, currentUser, failedAccessLogger,
            query.CriterionId, CriterionReadOperation.BrowseCriterionAds, cancellationToken);

        if (criterion is null)
            return null;

        if (query.OnlyMatching)
        {
            var resolved = await CriterionMatchingAdSet.ResolveAsync(
                profileBuilder, perUserSearch, browse, criterion.Criteria, cancellationToken);

            // Only the Resolved arm can honour the filter. NotAssessed and SetTooLarge fall through
            // to the unfiltered browse below — see the class docblock for why that is not an empty
            // page.
            if (resolved is CriterionMatchingAds.Resolved matching)
            {
                var ordinal = matching.Matching
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToList();

                // The TOTAL is the whole matching set, not the page — the set is exact (the port
                // refuses rather than truncating), so this pagination quantity happens to equal the
                // magnitude. It is still a pagination quantity and is still never rendered as one:
                // the surface reads the composed response's Matching member.
                return await LoadPageAsync(
                    ordinal, matching.Matching.Count, query.Page, query.PageSize, cancellationToken);
            }
        }

        var page = await browse.BrowseAdIdsAsync(
            new CompanyBrowseCriteria(criterion.Criteria, query.Page, query.PageSize),
            cancellationToken);

        return await LoadPageAsync(
            page.Items, page.TotalCount, page.Page, page.PageSize, cancellationToken);
    }

    /// <summary>
    /// Loads and projects the ads named by <paramref name="ordinal"/>, in THAT order. Shared by both
    /// arms so the projection and the re-sequencing have one home: a second copy would be a second
    /// place for the ordinal rule above to be got wrong.
    /// </summary>
    private async Task<PagedResult<JobAdDto>> LoadPageAsync(
        IReadOnlyList<JobAdId> ordinal,
        int totalCount,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // An empty page short-circuits: `= ANY('{}')` would be a round-trip that cannot match a row.
        if (ordinal.Count == 0)
            return new PagedResult<JobAdDto>([], totalCount, page, pageSize);

        var rows = await db.JobAds
            .AsNoTracking()
            .Where(j => ordinal.Contains(j.Id))
            .Select(j => new JobAdDto(
                j.Id.Value,
                j.Title,
                j.Company.Name,
                j.Url,
                j.Source.Value,
                j.Status.Value,
                j.PublishedAt,
                j.ExpiresAt,
                j.CreatedAt))
            .ToListAsync(cancellationToken);

        // Follow the port's sequence. A row the port named and this load did not return has been
        // deleted between the two round-trips; it drops out here explicitly rather than shifting
        // everything after it.
        var byId = rows.ToDictionary(d => d.Id);
        var items = ordinal
            .Select(id => byId.GetValueOrDefault(id.Value))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        return new PagedResult<JobAdDto>(items, totalCount, page, pageSize);
    }
}
