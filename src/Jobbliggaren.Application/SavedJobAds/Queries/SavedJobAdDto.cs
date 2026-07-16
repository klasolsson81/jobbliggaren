using Jobbliggaren.Application.Applications.Queries;

namespace Jobbliggaren.Application.SavedJobAds.Queries;

/// <summary>
/// F6 P5 Punkt 2 Del A — read-projektion för <c>/sparade</c>-listan.
/// JobAd-metadata via ADR 0048 in-handler-join (<see cref="JobAdSummaryDto"/>).
/// <c>JobAd</c> är nullable rent strukturellt (LEFT JOIN + DefaultIfEmpty).
/// <para>
/// Den tidigare utsagan ("nullable när annonsen soft-deletats") var falsk, och
/// axeln den namngav är retirerad: JobAd har ingen soft-delete (#821). En annons
/// som inte längre är aktiv joinar fortfarande och bär <c>JobAdSummaryDto.Status</c> ==
/// "Archived"; det, inte null-heten, är signalen ett UI ska läsa för "annonsen är
/// inte längre aktiv". (Erased filtreras bort uppströms av listans
/// <c>!= Erased</c>-grind; det retirerade Expired hade aldrig en writer, #886.)
/// </para></summary>
public sealed record SavedJobAdDto(
    Guid Id,
    Guid JobAdId,
    DateTimeOffset SavedAt,
    JobAdSummaryDto? JobAd);
