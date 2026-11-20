namespace Jobbliggaren.Application.JobAds.Junior;

/// <summary>
/// #2264 — one occupation field the junior rules are switched on for: its JobTech concept id, its occupation groups as
/// the taxonomy holds them now, and its rules.
/// </summary>
public sealed record JuniorField(
    string OccupationFieldConceptId,
    IReadOnlySet<string> OccupationGroups,
    JuniorRuleSet Rules);

/// <summary>
/// #2264 — the occupation fields the junior classification is switched on for, one rule file each, resolved against
/// the taxonomy. The sweep that writes verdicts and every read of the list resolve the fields here, so "Data/IT" means
/// the same groups when a verdict is written and when it is read. More fields are switched on one at a time, each by
/// its own rule file.
/// </summary>
public interface IJuniorFieldCatalog
{
    ValueTask<IReadOnlyList<JuniorField>> ResolveEnabledAsync(CancellationToken cancellationToken);
}
