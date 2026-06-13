using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;

/// <summary>
/// ADR 0043 — reverse-lookup för redan-sparade sökningar/valda chips
/// (concept-id → namn). Cap per anrop speglar domänens
/// <c>SearchCriteria.MaxConceptIds</c> ×2 (en sparad sökning bär som mest
/// MaxConceptIds Ssyk + MaxConceptIds Region) — refererad konstant, ej
/// hårdkodad (DRY/domän-konsekvens, MAP-3). Speglar Suggest-mönstret.
/// </summary>
public sealed record ResolveTaxonomyLabelsQuery(IReadOnlyList<string> ConceptIds)
    : IQuery<IReadOnlyList<TaxonomyLabelDto>>;
