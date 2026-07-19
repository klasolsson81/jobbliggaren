using FluentValidation;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusByOrgNrBatch;

/// <summary>
/// #560 PR-C — batch size enforced BEFORE the handler (parity with the jobAdId-keyed
/// <c>GetCompanyWatchStatusBatchQueryValidator</c>). 100 org.nrs is the safe max for a search page
/// (typical 20). Larger request → 400.
///
/// <para>
/// Count-cap ONLY, no per-item format/personnummer refusal. Unlike the search/lookup paths — where a
/// pnr-shaped term would drive an existence-revealing register lookup — this handler is owner-scoped and
/// echoes nothing: a manipulated pnr-shaped value can at most return the caller's OWN watch id (which they
/// already know) or null, so there is no cross-user leak and nothing to refuse. An unknown/garbage org.nr
/// simply correlates to null.
/// </para>
/// </summary>
public sealed class GetCompanyWatchStatusByOrgNrBatchQueryValidator
    : AbstractValidator<GetCompanyWatchStatusByOrgNrBatchQuery>
{
    public const int MaxOrgNrsPerCall = 100;

    public GetCompanyWatchStatusByOrgNrBatchQueryValidator()
    {
        RuleFor(q => q.OrganizationNumbers)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(orgNrs => orgNrs.Count <= MaxOrgNrsPerCall)
            .WithMessage($"Max {MaxOrgNrsPerCall} org.nr per anrop.");
    }
}
