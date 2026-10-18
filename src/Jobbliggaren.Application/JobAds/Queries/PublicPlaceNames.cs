using System.Collections.Frozen;
using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Application.JobAds.Queries;

/// <summary>
/// Kommun and län names for the public surface (ADR 0171): the job page, its <c>JobPosting</c> and the catalogue's rows
/// name a place through the ADR 0043 port, never by a concept id. A handler that uses this keeps the port in its own
/// constructor, so <c>TaxonomyAclLayerTests</c> still sees every consumer.
/// </summary>
internal static class PublicPlaceNames
{
    /// <summary>
    /// One port call for every id given. A null id is never sent, duplicates are sent once, no call is made when nothing
    /// is left, and an id the snapshot does not know is absent from the result (the port answers it with a null label,
    /// never a throw), so a name read from it is null.
    /// </summary>
    public static async ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        ITaxonomyReadModel taxonomy, IEnumerable<string?> conceptIds, CancellationToken cancellationToken)
    {
        var ids = conceptIds.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
            return FrozenDictionary<string, string>.Empty;

        var resolved = await taxonomy.ResolveLabelsAsync(ids, cancellationToken);
        return resolved
            .Where(l => l.Label is not null)
            .ToDictionary(l => l.ConceptId, l => l.Label!, StringComparer.Ordinal);
    }

    public static string? NameOf(IReadOnlyDictionary<string, string> names, string? conceptId) =>
        conceptId is not null && names.TryGetValue(conceptId, out var name) ? name : null;
}
