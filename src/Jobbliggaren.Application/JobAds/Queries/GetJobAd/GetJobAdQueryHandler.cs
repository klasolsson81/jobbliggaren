using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobAd;

/// <summary>
/// Ad detail. Returns <see cref="ErrorKind.Gone"/> (410) for an ad erased under GDPR Art. 17
/// (ADR 0106 Tier B, #842) and <see cref="ErrorKind.NotFound"/> (404) for an id we never held.
/// </summary>
/// <remarks>
/// This handler selects by id with no status predicate, so the guard is explicit.
/// <b>Which read paths are gated on <c>Status != Erased</c> is a TABLE in ADR 0106 §D9 — do not
/// re-enumerate them in a comment here, because such an enumeration drifts.</b>
/// <para>
/// <b>410, not 404.</b> 404 says "we never had this", which is false. 410 says "it existed and it
/// is deliberately gone." The applicant's own frozen record (ADR 0086's <c>AdSnapshot</c>) is
/// untouched, so she keeps her evidence of what she applied to; that is what that aggregate exists
/// for.
/// </para>
/// <para>
/// <b>Returns the DETAIL DTO (#842 Tier A, R2/ISP)</b> — a distinct type from the list's
/// <see cref="JobAdDto"/>, so the contact block PR4 adds here can never leak onto the
/// search wire. See <see cref="JobAdDetailDto"/>.
/// </para>
/// </remarks>
public sealed class GetJobAdQueryHandler(IAppDbContext db, IDateTimeProvider clock)
    : IQueryHandler<GetJobAdQuery, Result<JobAdDetailDto>>
{
    public async ValueTask<Result<JobAdDetailDto>> Handle(
        GetJobAdQuery query, CancellationToken cancellationToken)
    {
        // A NARROW anonymous row, not the entity: materialising JobAd would pull RawPayload
        // (org.nr + pre-scrub recruiter free text) into handler memory for nothing (§3.6). The
        // DTO is constructed AFTER the guards below — Contacts is a scalar-converted jsonb VO
        // whose inner list cannot be projected in SQL, and a tombstone never gets a contact
        // block built at all.
        var row = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == new JobAdId(query.Id))
            .Select(j => new
            {
                j.Status,
                ImportedFrom = j.External != null ? j.External.Source : null,
                j.Title,
                CompanyName = j.Company.Name,
                j.Description,
                j.Url,
                j.ApplicationUrl,
                Source = j.Source.Value,
                j.PublishedAt,
                j.ExpiresAt,
                j.CreatedAt,
                j.Contacts,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<JobAdDetailDto>(JobAdErrors.NotFound);

        if (row.Status == JobAdStatus.Erased)
            return Result.Failure<JobAdDetailDto>(JobAdErrors.Gone);

        // Listedness is the one JobAdPublication predicate (ADR 0160), computed from the import
        // provenance (External?.Source) — never the user-settable JobAd.Source — so the signed-in
        // share row (ADR 0183 D6) shows only for an ad the public surface actually lists, and the
        // client never re-derives it. Erased already answered 410 above, so Classify sees a live row.
        var isListed = JobAdPublication.Classify(row.Status, row.ExpiresAt, row.ImportedFrom, clock.UtcNow)
            == JobAdPublicationState.Listed;

        return Result.Success(new JobAdDetailDto(
            query.Id,
            row.Title,
            row.CompanyName,
            row.Description,
            row.Url,
            row.ApplicationUrl?.Value,
            row.Source,
            row.Status.Value,
            row.PublishedAt,
            row.ExpiresAt,
            row.CreatedAt,
            JobAdContactDto.ListFrom(row.Contacts),
            isListed));
    }
}
