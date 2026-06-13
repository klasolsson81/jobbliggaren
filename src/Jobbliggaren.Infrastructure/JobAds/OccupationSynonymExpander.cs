using Jobbliggaren.Application.JobAds.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// STEG 6 Approach B (2026-05-24) — IOptions-baserad implementation av
/// <see cref="IOccupationSynonymExpander"/>. Mapping bind:as från
/// <c>appsettings.json</c> → <see cref="SearchSynonymsOptions.Occupations"/>.
///
/// <para>
/// Söktermen lower-cas:as och whitespace-trim:as innan lookup. Saknad mapping
/// returnerar tom array — Q-grenen i <c>JobAdSearchQuery.ApplyCriteria</c>
/// faller då tillbaka på ren FTS + title-LIKE (befintligt beteende).
/// </para>
/// </summary>
internal sealed class OccupationSynonymExpander(
    IOptions<SearchSynonymsOptions> options) : IOccupationSynonymExpander
{
    public IReadOnlyCollection<string> Expand(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return [];

        var key = q.Trim();
        var map = options.Value.Occupations;

        if (map.TryGetValue(key, out var conceptIds) && conceptIds.Length > 0)
            return conceptIds;

        return [];
    }
}
