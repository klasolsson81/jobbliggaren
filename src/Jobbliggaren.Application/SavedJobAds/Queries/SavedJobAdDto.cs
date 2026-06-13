using Jobbliggaren.Application.Applications.Queries;

namespace Jobbliggaren.Application.SavedJobAds.Queries;

/// <summary>
/// F6 P5 Punkt 2 Del A — read-projektion för <c>/sparade</c>-listan.
/// JobAd-metadata via ADR 0048 in-handler-join (<see cref="JobAdSummaryDto"/>).
/// <c>JobAd</c> är nullable när annonsen soft-deletats — UI renderar
/// "Annonsen är borttagen" eller filtrerar bort raden.
/// </summary>
public sealed record SavedJobAdDto(
    Guid Id,
    Guid JobAdId,
    DateTimeOffset SavedAt,
    JobAdSummaryDto? JobAd);
