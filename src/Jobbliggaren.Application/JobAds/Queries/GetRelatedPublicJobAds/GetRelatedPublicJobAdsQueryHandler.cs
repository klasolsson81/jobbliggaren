using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;

/// <summary>
/// The related listed ads under one public job page (ADR 0182): other listed ads in the anchor's occupation group, same
/// kommun first, then same län, then the rest of Sweden, newest first within each tier; at most one row per employer and
/// never the anchor's employer; at most <see cref="RelatedPublicJobAdLimits.MaxRows"/>; no fallback to another group.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every anchor that is not listed answers the same 404</b>, whether unknown, never imported, ended or erased: the
/// publication predicate is in the anchor read itself, so no state of a non-listed row reaches this handler and there is
/// no switch that could grow a 410 arm. The detail read already tells 404 from 410; this one reveals strictly less.
/// </para>
/// <para>
/// <b>The employer rule carries Art. 9 as well as relevance.</b> Ads about one assisted person come from one provider,
/// so one row per employer, never the anchor's, keeps two of them out of any one section. An employer is its
/// organization number, and an ad that carries none takes no part, as a row or as an anchor: without it, the rule would
/// rest on how a name is spelt. Two rows with one number, or with one name, are one employer. The number never leaves
/// this handler.
/// </para>
/// </remarks>
public sealed class GetRelatedPublicJobAdsQueryHandler(IAppDbContext db, ITaxonomyReadModel taxonomy, IDateTimeProvider clock)
    : IQueryHandler<GetRelatedPublicJobAdsQuery, Result<RelatedPublicJobAdsDto>>
{
    private static readonly RelatedPublicJobAdsDto None = new([]);

    public async ValueTask<Result<RelatedPublicJobAdsDto>> Handle(
        GetRelatedPublicJobAdsQuery query, CancellationToken cancellationToken)
    {
        var id = new JobAdId(query.Id);
        var now = clock.UtcNow;

        var anchor = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Where(JobAdPublication.IsListed(now))
            .Select(j => new
            {
                j.OccupationGroupConceptId,
                j.MunicipalityConceptId,
                j.RegionConceptId,
                CompanyName = j.Company.Name,
                j.OrganizationNumber,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (anchor is null)
            return Result.Failure<RelatedPublicJobAdsDto>(JobAdErrors.NotFound);

        if (anchor.OccupationGroupConceptId is not { } group || anchor.OrganizationNumber is not { } anchorNumber)
            return Result.Success(None);

        // An int rank with an explicit null guard, never a bool sort key: EF rewrites `col == @p` for a null @p into
        // `col IS NULL`, which would rank every row without a kommun as the anchor's own.
        var kommun = anchor.MunicipalityConceptId;
        var lan = anchor.RegionConceptId;
        var anchorName = anchor.CompanyName;

        // A narrow row, never the entity: RawPayload carries the pre-scrub text.
        var candidates = await db.JobAds
            .AsNoTracking()
            .Where(JobAdPublication.IsListed(now))
            .Where(j => j.OccupationGroupConceptId == group && j.Id != id
                && j.OrganizationNumber != null && j.OrganizationNumber != anchorNumber && j.Company.Name != anchorName)
            .OrderBy(j => kommun != null && j.MunicipalityConceptId == kommun ? 0
                : lan != null && j.RegionConceptId == lan ? 1
                : 2)
            .ThenByDescending(j => j.PublishedAt)
            .ThenBy(j => j.Id)
            .Take(RelatedPublicJobAdLimits.CandidateWindow)
            .Select(j => new Candidate(
                j.Id.Value, j.Title, j.Company.Name, j.OrganizationNumber!, j.MunicipalityConceptId, j.RegionConceptId,
                j.PublishedAt))
            .ToListAsync(cancellationToken);

        // The read's order is kept: the first row of each new employer, until the cap.
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        var employerNames = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Candidate>(RelatedPublicJobAdLimits.MaxRows);
        foreach (var candidate in candidates)
        {
            if (rows.Count == RelatedPublicJobAdLimits.MaxRows)
                break;
            if (numbers.Contains(candidate.OrganizationNumber) || employerNames.Contains(candidate.CompanyName))
                continue;
            numbers.Add(candidate.OrganizationNumber);
            employerNames.Add(candidate.CompanyName);
            rows.Add(candidate);
        }

        var names = await PublicTaxonomyNames.ResolveAsync(
            taxonomy, rows.SelectMany(r => new[] { r.MunicipalityConceptId, r.RegionConceptId }), cancellationToken);

        return Result.Success(new RelatedPublicJobAdsDto(rows
            .Select(r => new PublicJobAdSummaryDto(
                r.Id,
                r.Title,
                r.CompanyName,
                PublicTaxonomyNames.NameOf(names, r.MunicipalityConceptId),
                PublicTaxonomyNames.NameOf(names, r.RegionConceptId),
                r.PublishedAt))
            .ToList()));
    }

    private sealed record Candidate(
        Guid Id,
        string Title,
        string CompanyName,
        string OrganizationNumber,
        string? MunicipalityConceptId,
        string? RegionConceptId,
        DateTimeOffset PublishedAt)
    {
        // A sole trader's organization number is a personnummer, and MEL renders a plain {X} through ToString().
        public override string ToString() => $"Candidate({Id}, org.nr redacted)";
    }
}
