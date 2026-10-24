using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// The one taxonomy-wide occupation-name index (#2198, ADR 0180): every occupation-name the
/// frozen occupation-name→ssyk-level-4 map resolves to a group present in the snapshot, with that
/// group, plus every ssyk-level-4 group once. Two consumers read it and each builds its own view:
/// <see cref="OccupationCodeDeriver"/> (CV proposal, spread-gated) and the picker's
/// <c>OccupationSearcher</c> (no gate). The index holds no lexemes and no tokens, so neither
/// consumer's quantity leaks into the other's.
/// <para>
/// Singleton with a lazy single-load cache mirroring <see cref="TaxonomyReadModel"/>: a fault
/// leaves it unpublished so the next call retries.
/// </para>
/// </summary>
internal sealed class OccupationNameIndex(ITaxonomyReadModel taxonomy)
{
    private Task<OccupationNameIndexContents>? _cached;

    internal async ValueTask<OccupationNameIndexContents> GetAsync(CancellationToken ct)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is { IsCompletedSuccessfully: true })
            return cached.Result;

        // Await BEFORE publishing: a fault throws here and leaves _cached unpublished → next
        // call retries. A rare concurrent cold-start may build twice (idempotent in-memory
        // projection); last write wins (parity with TaxonomyReadModel.GetStateAsync).
        var task = BuildAsync(ct);
        var contents = await task;
        Volatile.Write(ref _cached, task);
        return contents;
    }

    private async Task<OccupationNameIndexContents> BuildAsync(CancellationToken ct)
    {
        var tree = await taxonomy.GetTreeAsync(ct);
        var groupToSsyk = LoadFrozenMap();

        // ssyk-4 group id → label (from the tree's occupation-groups). If one concept-id ever
        // surfaces under more than one occupation field, pick the Ordinal-minimum label
        // deterministically — NEVER an enumeration-order-dependent First(): both consumers'
        // contract is reproducibility (ADR 0040/0071). Today the snapshot carries one label per
        // group, so this is a defensive pin, not a visible change — it forecloses a silent drift
        // if a future snapshot ever lists a group twice with diverging labels.
        var groupLabelById = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups)
            .GroupBy(g => g.ConceptId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Label, StringComparer.Ordinal).First().Label,
                StringComparer.Ordinal);

        var groups = new List<OccupationGroupEntry>();
        var seenGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in tree.OccupationFields.SelectMany(f => f.OccupationGroups))
        {
            if (seenGroups.Add(group.ConceptId))
                groups.Add(new OccupationGroupEntry(group.ConceptId, groupLabelById[group.ConceptId]));
        }

        var names = new List<OccupationNameEntry>();
        foreach (var occupation in tree.OccupationFields.SelectMany(f => f.Occupations))
        {
            // An occupation-name is matchable only if the frozen map resolves it to a ssyk-4
            // group that exists in the snapshot. Occupation-names absent from the map degrade to
            // manual selection — the F4-3 partial-coverage rule (ADR 0040 amendment).
            // Live-verified 2026-06-14: 2153 of the 2323 snapshot occupation-names map; 170 are
            // unmapped (the frozen map's 2179 entries include ids not in this v30 snapshot —
            // benign taxonomy drift; 0 dangling group targets).
            if (!groupToSsyk.TryGetValue(occupation.ConceptId, out var groupId)
                || !groupLabelById.TryGetValue(groupId, out var groupLabel))
            {
                continue;
            }

            names.Add(new OccupationNameEntry(occupation.ConceptId, occupation.Label, groupId, groupLabel));
        }

        return new OccupationNameIndexContents(names, groups);
    }

    // Synchronous on purpose: the call to the loader sits on this declaring type itself, not on
    // BuildAsync's compiler-generated state machine, so the architecture pin on the loader's
    // dependents sees exactly this class.
    private static IReadOnlyDictionary<string, string> LoadFrozenMap() =>
        OccupationGroupMappingLoader.Load();
}

/// <summary>The index's contents: the matchable names in tree order and the groups once each.</summary>
internal sealed record OccupationNameIndexContents(
    IReadOnlyList<OccupationNameEntry> Names,
    IReadOnlyList<OccupationGroupEntry> Groups);

/// <summary>One matchable occupation-name and the ssyk-4 group the frozen map resolves it to.</summary>
internal sealed record OccupationNameEntry(string ConceptId, string NameLabel, string GroupConceptId, string GroupLabel);

/// <summary>One ssyk-4 group with its deterministic label.</summary>
internal sealed record OccupationGroupEntry(string ConceptId, string Label);
