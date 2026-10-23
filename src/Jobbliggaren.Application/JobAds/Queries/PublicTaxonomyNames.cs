using System.Collections.Frozen;
using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Application.JobAds.Queries;

/// <summary>
/// Taxonomy names for the public surface (ADR 0171): the job page, its <c>JobPosting</c>, the catalogue's rows and the
/// related section's rows name a concept through the ADR 0043 port, never by its id. A handler that uses this keeps the port in its own constructor,
/// so <c>TaxonomyAclLayerTests</c> still sees every consumer.
/// </summary>
internal static class PublicTaxonomyNames
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

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resolved in await taxonomy.ResolveLabelsAsync(ids, cancellationToken))
        {
            if (resolved.Label is { } label)
                names.Add(resolved.ConceptId, label);
        }

        return names;
    }

    public static string? NameOf(IReadOnlyDictionary<string, string> names, string? conceptId) =>
        conceptId is not null && names.TryGetValue(conceptId, out var name) ? name : null;
}
