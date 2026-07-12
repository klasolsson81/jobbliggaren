using Jobbliggaren.Application.Applications.Queries;

namespace Jobbliggaren.Application.SavedJobAds.Queries;

/// <summary>
/// F6 P5 Punkt 2 Del A — read-projektion för <c>/sparade</c>-listan.
/// JobAd-metadata via ADR 0048 in-handler-join (<see cref="JobAdSummaryDto"/>).
/// <c>JobAd</c> är nullable rent strukturellt (LEFT JOIN + DefaultIfEmpty).
/// <para>
/// #805-3 sanningssynk: den tidigare utsagan ("nullable när annonsen
/// soft-deletats") var falsk — JobAd.DeletedAt saknar writer, så det globala
/// query-filtret exkluderar aldrig en rad (#821). En annons som inte längre är
/// aktiv joinar fortfarande och bär <c>JobAdSummaryDto.Status</c> ==
/// "Archived"/"Expired"; det, inte null-heten, är signalen ett UI ska läsa för
/// "annonsen är inte längre aktiv".
/// </para></summary>
public sealed record SavedJobAdDto(
    Guid Id,
    Guid JobAdId,
    DateTimeOffset SavedAt,
    JobAdSummaryDto? JobAd);
