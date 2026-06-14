using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.DeriveOccupationCodes;

/// <summary>
/// Thin adapter over <see cref="IOccupationCodeDeriver"/> (mirrors
/// <c>ResolveTaxonomyLabelsQueryHandler</c>). No matching logic in the handler —
/// the deterministic derivation lives in the Infrastructure deriver. Returns the
/// DTO directly (queries return DTOs, never <c>Result&lt;T&gt;</c>).
/// </summary>
public sealed class DeriveOccupationCodesQueryHandler(IOccupationCodeDeriver deriver)
    : IQueryHandler<DeriveOccupationCodesQuery, OccupationDerivationResult>
{
    public async ValueTask<OccupationDerivationResult> Handle(
        DeriveOccupationCodesQuery query, CancellationToken cancellationToken)
        => await deriver.DeriveAsync(query.Title, cancellationToken);
}
