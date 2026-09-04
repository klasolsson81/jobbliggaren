using FluentValidation;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.BrowseCriterionAds;

/// <summary>
/// #1559 — "show me the ACTIVE job ads posted by the companies my criterion
/// <paramref name="CriterionId"/> matches", newest first. Owner-scoped: a criterion that does not
/// exist is indistinguishable from one owned by somebody else (both → <c>null</c> → 404).
///
/// <para>
/// <b>Why this surface exists at all rather than a link to <c>/jobb</c></b> (senior-cto-advisor
/// 2026-09-04, closing the issue's open question 1). <c>/jobb</c> cannot express this set: it has no
/// SNI axis; its only company axis is <c>?employer=</c>, whose producer refuses above
/// <c>MAX_CONCEPT_IDS</c> = 400 org.nrs on an every-value-or-none doctrine, against criteria that
/// routinely match thousands of companies; and its <c>municipality</c> axis is the AD's
/// workplace while a criterion's kommun is the company's REGISTERED SEAT. Any link built from those
/// axes would be partial or false, so the criterion's own id is the destination. The structural
/// sibling is <c>/foretag/bevakade/nya</c> (#1576), which exists for the same reason.
/// </para>
///
/// <para>
/// Returns a NULLABLE <see cref="PagedResult{T}"/> rather than <c>Result&lt;&gt;</c>, parity
/// <c>BrowseCompaniesQuery</c> — NotFound is the only error this query can produce, and
/// <see cref="PagedResult{T}"/>'s type identity is load-bearing for the response-type-keyed pipeline
/// behaviors (<c>PagedResultContractTests</c>).
/// </para>
/// </summary>
public sealed record BrowseCriterionAdsQuery(Guid CriterionId, int Page, int PageSize)
    : IQuery<PagedResult<JobAdDto>?>, IAuthenticatedRequest;

/// <summary>
/// Transport bounds only — the PREDICATE is not user input on this request, it is the persisted
/// <c>CompanyWatchCriteriaSpec</c> the Domain validated at write time. Parity
/// <c>BrowseCompaniesQueryValidator</c>.
/// </summary>
public sealed class BrowseCriterionAdsQueryValidator : AbstractValidator<BrowseCriterionAdsQuery>
{
    public BrowseCriterionAdsQueryValidator()
    {
        RuleFor(q => q.CriterionId).NotEmpty();

        // The SAME constants the port caps its count against — that is what makes "TotalPages can
        // never exceed MaxPage" true by construction rather than by coincidence, on this surface as
        // on the company browse. See CompanyBrowseCriteria.MaxServableRows.
        RuleFor(q => q.Page).InclusiveBetween(1, CompanyBrowseCriteria.MaxPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, CompanyBrowseCriteria.MaxPageSize);
    }
}
