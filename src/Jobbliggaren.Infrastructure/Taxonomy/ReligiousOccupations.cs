using System.Collections.Frozen;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Common;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// #2199 PR 2b — the <see cref="IReligiousOccupations"/> over the artefacts loaded at host start: the names the
/// lineage files under the four groups, and, from the taxonomy snapshot the database is seeded from, the
/// occupation fields each group belongs to. Both are copied once into frozen sets. It keeps no reference to the
/// lineage, so it can never become a second reader of its search rule.
/// </summary>
internal sealed class ReligiousOccupations : IReligiousOccupations
{
    private readonly FrozenSet<string> _names;
    private readonly FrozenDictionary<string, FrozenSet<string>[]> _fieldsOf;

    public ReligiousOccupations(OccupationNameLineage lineage, TaxonomySnapshotFile snapshot)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(snapshot);

        _names = lineage.ConceptIdsInGroups(ReligiousOccupationGroups.ConceptIds).ToFrozenSet(StringComparer.Ordinal);

        // Every policy group gets an entry, an empty one when no field holds it, so a group the snapshot has
        // lost is refused rather than waved through as covered.
        var fields = snapshot.OccupationFields
            .Select(f => (f.OccupationGroups ?? []).Select(g => g.ConceptId).ToFrozenSet(StringComparer.Ordinal))
            .ToList();
        _fieldsOf = ReligiousOccupationGroups.ConceptIds.ToFrozenDictionary(
            group => group,
            group => fields.Where(f => f.Contains(group)).ToArray(),
            StringComparer.Ordinal);
    }

    public bool AreSingledOutBy(IReadOnlyCollection<string> occupationGroups, IReadOnlyCollection<string> occupationNames)
    {
        ArgumentNullException.ThrowIfNull(occupationGroups);
        ArgumentNullException.ThrowIfNull(occupationNames);

        if (occupationNames.Any(id => _names.Contains(id) || ReligiousOccupationGroups.ConceptIds.Contains(id)))
            return true;

        var picked = occupationGroups.ToHashSet(StringComparer.Ordinal);
        foreach (var id in picked)
        {
            if (_names.Contains(id))
                return true;
            if (_fieldsOf.TryGetValue(id, out var fieldsHoldingIt) && !fieldsHoldingIt.Any(field => field.IsSubsetOf(picked)))
                return true;
        }

        return false;
    }
}
