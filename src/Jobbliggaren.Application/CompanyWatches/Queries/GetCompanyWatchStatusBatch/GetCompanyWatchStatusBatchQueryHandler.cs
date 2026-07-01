using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusBatch;

/// <summary>
/// #311 #455 (ADR 0087 D8(c)) — composes the follow-state overlay for a page of ads. Correlates the
/// current user's active follows (bounded — a handful) against each requested ad's employer org.nr
/// (resolved server-side via <see cref="IJobAdEmployerReader"/>). The raw org.nr is used ONLY to
/// correlate and is NEVER placed in the returned <see cref="CompanyWatchStatusDto"/> — the FE receives
/// the opaque <c>CompanyWatchId</c> (for unfollow) and a <c>Followable</c> flag, nothing more.
/// </summary>
public sealed class GetCompanyWatchStatusBatchQueryHandler(
    IAppDbContext db,
    IJobAdEmployerReader employerReader,
    ICurrentUser currentUser)
    : IQueryHandler<GetCompanyWatchStatusBatchQuery, CompanyWatchStatusBatchDto>
{
    private static readonly CompanyWatchStatusBatchDto Empty = new([]);

    public async ValueTask<CompanyWatchStatusBatchDto> Handle(
        GetCompanyWatchStatusBatchQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue || query.JobAdIds.Count == 0)
            return Empty;

        var userId = currentUser.UserId.Value;

        // The user's active follows. Load the (bounded) entities then project org.nr client-side —
        // parity ListCompanyWatchesQueryHandler (a value-converted VO member does not project cleanly
        // server-side). The active-partial UNIQUE(user_id, organization_number) guarantees one row per
        // org.nr, but GroupBy-First is defensive.
        var watches = await db.CompanyWatches
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .ToListAsync(cancellationToken);

        var companyWatchIdByOrgNr = watches
            .GroupBy(w => w.OrganizationNumber.Value)
            .ToDictionary(g => g.Key, g => g.First().Id.Value);

        // org.nr per requested ad, resolved server-side (raw org.nr stays here, never surfaced).
        var orgNrByJobAd = await employerReader.GetOrganizationNumbersByJobAdIdsAsync(
            query.JobAdIds, cancellationToken);

        var statuses = query.JobAdIds
            .Distinct()
            .Select(jobAdId =>
            {
                var followable = orgNrByJobAd.TryGetValue(jobAdId, out var orgNr) && orgNr is not null;
                Guid? companyWatchId =
                    followable && companyWatchIdByOrgNr.TryGetValue(orgNr!, out var watchId)
                        ? watchId
                        : null;
                return new CompanyWatchStatusDto(jobAdId, companyWatchId, followable);
            })
            .ToList();

        return new CompanyWatchStatusBatchDto(statuses);
    }
}
