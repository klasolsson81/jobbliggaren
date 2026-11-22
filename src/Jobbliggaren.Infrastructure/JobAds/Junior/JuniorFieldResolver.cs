using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Junior;

namespace Jobbliggaren.Infrastructure.JobAds.Junior;

/// <summary>
/// #2264 — <see cref="IJuniorFieldCatalog"/> over the loaded rule files and the taxonomy. A switched-on field the
/// taxonomy no longer knows, or one without groups, throws: it would empty the list and the queue without a word.
/// </summary>
internal sealed class JuniorFieldResolver(IReadOnlyList<JuniorRuleSet> ruleSets, ITaxonomyReadModel taxonomy) : IJuniorFieldCatalog
{
    public async ValueTask<IReadOnlyList<JuniorField>> ResolveEnabledAsync(CancellationToken cancellationToken)
    {
        var tree = await taxonomy.GetTreeAsync(cancellationToken);
        var fields = new List<JuniorField>();

        foreach (var rules in ruleSets.Where(r => r.Enabled))
        {
            var field = tree.OccupationFields.FirstOrDefault(f =>
                string.Equals(f.ConceptId, rules.OccupationFieldConceptId, StringComparison.Ordinal));

            if (field is null || field.OccupationGroups.Count == 0)
                throw new InvalidOperationException(
                    $"The junior rules' occupation field {rules.OccupationFieldConceptId} has no occupation groups in the taxonomy.");

            fields.Add(new JuniorField(
                rules.OccupationFieldConceptId,
                field.OccupationGroups.Select(g => g.ConceptId).ToHashSet(StringComparer.Ordinal),
                rules));
        }

        return fields;
    }
}
