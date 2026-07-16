using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionMatchMagnitude;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.PreviewCriterionMatchMagnitude;

/// <summary>
/// Builds the spec from the unsaved input (Domain owns format + both-axes-required) and counts
/// through the port. No persistence is touched — nothing is read from or written to
/// <c>IAppDbContext</c>, so there is no owner-scoping question and no probe: the input is the
/// caller's own request body.
/// </summary>
public sealed class PreviewCriterionMatchMagnitudeQueryHandler(
    ICurrentUser currentUser,
    ICompanyWatchBrowseQuery browse)
    : IQueryHandler<PreviewCriterionMatchMagnitudeQuery, Result<CriterionMatchMagnitudeDto>>
{
    public async ValueTask<Result<CriterionMatchMagnitudeDto>> Handle(
        PreviewCriterionMatchMagnitudeQuery query, CancellationToken cancellationToken)
    {
        // Fail-closed parity with the sibling handlers — the endpoint is auth-gated, but the
        // handler must be correct without the front door (§2.4).
        if (!currentUser.UserId.HasValue)
        {
            return Result.Failure<CriterionMatchMagnitudeDto>(DomainError.Validation(
                "CompanyWatchCriterion.Unauthorized", "Användaren är inte autentiserad."));
        }

        var specResult = CompanyWatchCriteriaSpec.Create(
            query.Criteria.SniCodes, query.Criteria.MunicipalityCodes);
        if (specResult.IsFailure)
            return Result.Failure<CriterionMatchMagnitudeDto>(specResult.Error);

        var magnitude = await browse.CountMatchingCompaniesAsync(
            specResult.Value, CriterionMatchMagnitudeDto.Ceiling, cancellationToken);

        return Result.Success(new CriterionMatchMagnitudeDto(
            magnitude, Saturated: magnitude >= CriterionMatchMagnitudeDto.Ceiling));
    }
}
