using Jobbliggaren.Application.Common.Abstractions;
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
public sealed class GetJobAdQueryHandler(IAppDbContext db)
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
                Status = j.Status.Value,
                j.Title,
                CompanyName = j.Company.Name,
                j.Description,
                j.Url,
                Source = j.Source.Value,
                j.PublishedAt,
                j.ExpiresAt,
                j.CreatedAt,
                j.Contacts,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<JobAdDetailDto>(
                DomainError.NotFound("JobAd.NotFound", "Annonsen finns inte."));

        // The body is deliberately NEUTRAL. Saying "raderad enligt artikel 17" would let ANY caller
        // — the ad id is public, and Arbetsförmedlingen publishes the same ad in its open
        // Historiska annonser dataset — correlate the two and infer that the named recruiter in
        // that ad exercised a right. The erasure would then broadcast the very fact it exists to
        // protect. So: it is gone, and we do not say why.
        if (row.Status == JobAdStatus.Erased.Value)
            return Result.Failure<JobAdDetailDto>(
                DomainError.Gone("JobAd.Gone", "Annonsen är inte längre tillgänglig."));

        return Result.Success(new JobAdDetailDto(
            query.Id,
            row.Title,
            row.CompanyName,
            row.Description,
            row.Url,
            row.Source,
            row.Status,
            row.PublishedAt,
            row.ExpiresAt,
            row.CreatedAt,
            JobAdContactDto.ListFrom(row.Contacts)));
    }
}
