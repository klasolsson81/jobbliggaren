namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// #2199 — which occupation-name an ad's coded occupation-name counts as, given JobTech's
/// replaced-by edges. It is the one rule behind the /jobb occupation-name filter and its counts:
/// <see cref="Expand"/> builds the filter, <see cref="CountByPick"/> folds the counts, and both are
/// derived from <see cref="Resolve"/>.
///
/// <para>
/// <b>The rule.</b> Every name leads to a set of terminals: the names its replaced-by edges end in
/// (itself when it has none). It is counted on terminals, not on raw edges, because the edge set is a
/// union over taxonomy versions and a re-pointed edge would otherwise look like a second replacement.
/// A name with one terminal resolves to it. A name with several resolves to the one terminal in the
/// ad's own occupation group, and to nothing when no terminal or more than one is in that group: the
/// ad is then reachable only through its group (Klas, 2026-10-24, "Bara inom samma grupp"). A name
/// the lineage does not know resolves to itself.
/// </para>
///
/// <para>
/// <b>A pick</b> matches the ads coded with exactly that name, and the ads that resolve to its single
/// terminal. A pick with several terminals has no terminal of its own to share, so it matches its own
/// ads only.
/// </para>
///
/// <para>
/// Referential integrity (unique ids, known targets, an acyclic graph) is checked fail-loud by the
/// Infrastructure loader at host start. The constructor re-checks what its own lookups need.
/// </para>
/// </summary>
public sealed class OccupationNameLineage
{
    private readonly Dictionary<string, string?> _groupOf;
    private readonly Dictionary<string, string[]> _terminalsOf;
    private readonly Dictionary<string, List<string>> _singleTerminalNamesOf;
    private readonly Dictionary<string, List<string>> _sameGroupNamesOf;

    public OccupationNameLineage(string version, IReadOnlyList<OccupationNameLineageEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(entries);

        Version = version;
        _groupOf = new Dictionary<string, string?>(entries.Count, StringComparer.Ordinal);
        var edges = new Dictionary<string, IReadOnlyList<string>>(entries.Count, StringComparer.Ordinal);
        var current = new List<string>();
        foreach (var entry in entries)
        {
            if (!_groupOf.TryAdd(entry.ConceptId, entry.GroupConceptId))
                throw new ArgumentException($"The occupation-name lineage lists {entry.ConceptId} twice.", nameof(entries));
            if (entry.ReplacedByConceptIds.Count > 0)
                edges.Add(entry.ConceptId, entry.ReplacedByConceptIds);
            if (entry.CurrentIn.Count > 0)
                current.Add(entry.ConceptId);
        }

        _terminalsOf = new Dictionary<string, string[]>(entries.Count, StringComparer.Ordinal);
        foreach (var id in _groupOf.Keys)
            TerminalsOf(id, edges, new HashSet<string>(StringComparer.Ordinal));

        _singleTerminalNamesOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _sameGroupNamesOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (id, terminals) in _terminalsOf)
        {
            if (terminals.Length == 1)
            {
                Add(_singleTerminalNamesOf, terminals[0], id);
                continue;
            }

            foreach (var group in terminals.Select(t => _groupOf[t]).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (Resolve(id, group) is { } terminal)
                    Add(_sameGroupNamesOf, terminal, id);
            }
        }

        CurrentConceptIds = current;
    }

    /// <summary>The artefact's version stamp.</summary>
    public string Version { get; }

    /// <summary>The names that are current in at least one taxonomy version: the ones counts are reported for.</summary>
    public IReadOnlyList<string> CurrentConceptIds { get; }

    /// <summary>
    /// The current name an ad coded <paramref name="occupationNameConceptId"/> in group
    /// <paramref name="adGroupConceptId"/> counts as, or <see langword="null"/> when it counts as none.
    /// </summary>
    public string? Resolve(string occupationNameConceptId, string? adGroupConceptId)
    {
        if (!_terminalsOf.TryGetValue(occupationNameConceptId, out var terminals))
            return occupationNameConceptId;

        if (terminals.Length == 1)
            return terminals[0];

        string? match = null;
        foreach (var terminal in terminals)
        {
            if (adGroupConceptId is null || !string.Equals(_groupOf[terminal], adGroupConceptId, StringComparison.Ordinal))
                continue;
            if (match is not null)
                return null;
            match = terminal;
        }

        return match;
    }

    /// <summary>The single terminal a pick shares with the ads that resolve to it, or <see langword="null"/> for a pick with several.</summary>
    public string? TargetOf(string pick) =>
        _terminalsOf.TryGetValue(pick, out var terminals)
            ? terminals.Length == 1 ? terminals[0] : null
            : pick;

    /// <summary>Whether an ad coded <paramref name="adOccupationNameConceptId"/> in <paramref name="adGroupConceptId"/> answers <paramref name="pick"/>.</summary>
    public bool Matches(string pick, string adOccupationNameConceptId, string? adGroupConceptId) =>
        string.Equals(adOccupationNameConceptId, pick, StringComparison.Ordinal)
        || (TargetOf(pick) is { } target
            && string.Equals(Resolve(adOccupationNameConceptId, adGroupConceptId), target, StringComparison.Ordinal));

    /// <summary>
    /// The picks as the filter composes them: the names that match whatever their ad's group, and per
    /// group the names that match only in that group. It is exactly the set of (name, group) pairs
    /// <see cref="Matches"/> admits for at least one pick.
    /// </summary>
    public OccupationNameExpansion Expand(IReadOnlyCollection<string> picks)
    {
        ArgumentNullException.ThrowIfNull(picks);
        if (picks.Count == 0)
            return OccupationNameExpansion.Empty;

        var anyGroup = new HashSet<string>(picks, StringComparer.Ordinal);
        var targets = picks.Select(TargetOf).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        foreach (var target in targets)
        {
            if (_singleTerminalNamesOf.TryGetValue(target, out var names))
                anyGroup.UnionWith(names);
        }

        var sameGroup = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!_sameGroupNamesOf.TryGetValue(target, out var names) || _groupOf[target] is not { } group)
                continue;
            foreach (var name in names.Where(n => !anyGroup.Contains(n)))
            {
                if (!sameGroup.TryGetValue(group, out var set))
                    sameGroup.Add(group, set = new SortedSet<string>(StringComparer.Ordinal));
                set.Add(name);
            }
        }

        return new OccupationNameExpansion(
            [.. anyGroup.Order(StringComparer.Ordinal)],
            [.. sameGroup.Select(kv => new SameGroupOccupationNames(kv.Key, [.. kv.Value]))]);
    }

    /// <summary>
    /// The exact count for every current name, from ad counts grouped by (name, group): the number of
    /// ads <see cref="Matches"/> admits for that name as a pick. Names with no ads are left out.
    /// </summary>
    /// <remarks>
    /// A pick with a single terminal is answered by every ad that resolves to it, its own ads included
    /// (they resolve to the same terminal), so its count is that terminal's total. A pick with several
    /// terminals is answered by its own ads only.
    /// </remarks>
    public IReadOnlyDictionary<string, int> CountByPick(IEnumerable<OccupationNamePairCount> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var byTarget = new Dictionary<string, int>(StringComparer.Ordinal);
        var own = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            own[row.OccupationNameConceptId] = own.GetValueOrDefault(row.OccupationNameConceptId) + row.Count;
            if (Resolve(row.OccupationNameConceptId, row.OccupationGroupConceptId) is { } target)
                byTarget[target] = byTarget.GetValueOrDefault(target) + row.Count;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pick in CurrentConceptIds)
        {
            var count = TargetOf(pick) is { } target ? byTarget.GetValueOrDefault(target) : own.GetValueOrDefault(pick);
            if (count > 0)
                counts.Add(pick, count);
        }

        return counts;
    }

    private string[] TerminalsOf(string id, Dictionary<string, IReadOnlyList<string>> edges, HashSet<string> path)
    {
        if (_terminalsOf.TryGetValue(id, out var known))
            return known;
        if (!path.Add(id))
            throw new ArgumentException($"The occupation-name lineage has a replaced-by cycle through {id}.");

        string[] terminals;
        if (!edges.TryGetValue(id, out var next))
        {
            terminals = [id];
        }
        else
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var target in next)
            {
                if (!_groupOf.ContainsKey(target))
                    throw new ArgumentException($"{id} is replaced by {target}, which the occupation-name lineage does not list.");
                set.UnionWith(TerminalsOf(target, edges, path));
            }

            terminals = [.. set];
        }

        path.Remove(id);
        _terminalsOf[id] = terminals;
        return terminals;
    }

    private static void Add(Dictionary<string, List<string>> index, string key, string value)
    {
        if (!index.TryGetValue(key, out var list))
            index.Add(key, list = []);
        list.Add(value);
    }
}

/// <summary>One occupation-name in the lineage: its group, the versions it is current in, and what replaced it.</summary>
public sealed record OccupationNameLineageEntry(
    string ConceptId,
    string? GroupConceptId,
    IReadOnlyList<string> CurrentIn,
    IReadOnlyList<string> ReplacedByConceptIds);

/// <summary>
/// The occupation-name picks as the filter composes them: <see cref="ConceptIds"/> match in any group,
/// and each <see cref="SameGroupOccupationNames"/> matches only in its own group.
/// </summary>
public sealed record OccupationNameExpansion(
    IReadOnlyList<string> ConceptIds,
    IReadOnlyList<SameGroupOccupationNames> SameGroupOnly)
{
    public static OccupationNameExpansion Empty { get; } = new([], []);

    public bool IsEmpty => ConceptIds.Count == 0 && SameGroupOnly.Count == 0;
}

/// <summary>Names that answer a pick only when the ad's group is <see cref="GroupConceptId"/>.</summary>
public sealed record SameGroupOccupationNames(string GroupConceptId, IReadOnlyList<string> ConceptIds);

/// <summary>The number of ads coded with one occupation-name in one group.</summary>
public sealed record OccupationNamePairCount(string OccupationNameConceptId, string? OccupationGroupConceptId, int Count);
