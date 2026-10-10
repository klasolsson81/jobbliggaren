using System.Diagnostics;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;

/// <summary>
/// The anonymous detail read (ADR 0160): 200 for a Listed ad, the shared neutral 410 for an ended
/// or an erased one, and the unknown-id 404 for an id the public surface never held.
/// </summary>
/// <remarks>
/// Ended and erased answer the SAME 410. Erasure happens only on an Art. 17 request, so a public
/// 410 beside a public "ended" answer would tell every crawler which ads someone asked to have
/// erased (security-auditor, 2026-10-10; Klas accepted).
/// </remarks>
public sealed class GetPublicJobAdQueryHandler(IAppDbContext db, IDateTimeProvider clock)
    : IQueryHandler<GetPublicJobAdQuery, Result<PublicJobAdDto>>
{
    public async ValueTask<Result<PublicJobAdDto>> Handle(
        GetPublicJobAdQuery query, CancellationToken cancellationToken)
    {
        var id = new JobAdId(query.Id);
        var now = clock.UtcNow;

        // The publication facts alone first: the text of an ad that will not be shown is never read.
        var facts = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new
            {
                j.Status,
                j.ExpiresAt,
                ImportedFrom = j.External != null ? j.External.Source : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (facts is null)
            return Result.Failure<PublicJobAdDto>(JobAdErrors.NotFound);

        var state = JobAdPublication.Classify(facts.Status, facts.ExpiresAt, facts.ImportedFrom, now);
        if (state != JobAdPublicationState.Listed)
            return Answer(state, listed: null);

        // The content, classified again from the same row: the two reads share no transaction, and an
        // erasure committed between them must not be served. A narrow row, never the entity — RawPayload
        // carries the org.nr and the pre-scrub text.
        var row = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new
            {
                j.Status,
                j.ExpiresAt,
                ImportedFrom = j.External != null ? j.External.Source : null,
                j.Title,
                CompanyName = j.Company.Name,
                j.Description,
                j.Url,
                j.PublishedAt,
                j.MunicipalityConceptId,
                j.RegionConceptId,
                j.OccupationGroupConceptId,
                j.EmploymentTypeConceptId,
                j.WorktimeExtentConceptId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<PublicJobAdDto>(JobAdErrors.NotFound);

        state = JobAdPublication.Classify(row.Status, row.ExpiresAt, row.ImportedFrom, now);
        return Answer(state, state == JobAdPublicationState.Listed
            ? new PublicJobAdDto(
                query.Id,
                row.Title,
                row.CompanyName,
                row.Description,
                row.Url,
                row.PublishedAt,
                row.ExpiresAt,
                row.MunicipalityConceptId,
                row.RegionConceptId,
                row.OccupationGroupConceptId,
                row.EmploymentTypeConceptId,
                row.WorktimeExtentConceptId)
            : null);
    }

    private static Result<PublicJobAdDto> Answer(JobAdPublicationState state, PublicJobAdDto? listed) => state switch
    {
        JobAdPublicationState.Listed when listed is not null => Result.Success(listed),
        JobAdPublicationState.Ended or JobAdPublicationState.Gone => Result.Failure<PublicJobAdDto>(JobAdErrors.Gone),
        JobAdPublicationState.NotPublic => Result.Failure<PublicJobAdDto>(JobAdErrors.NotFound),
        var other => throw new UnreachableException($"Unmapped publication state {other}."),
    };
}
