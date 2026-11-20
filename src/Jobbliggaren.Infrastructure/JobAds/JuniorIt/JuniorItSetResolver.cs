using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Infrastructure.JobAds.JuniorIt;

/// <summary>
/// What <see cref="JuniorItPredicate"/> needs, resolved: the curated ids, the occupation groups of the list's field and
/// the headline pattern.
/// </summary>
internal sealed record JuniorItSet(
    IReadOnlyList<string> CuratedExternalIds,
    IReadOnlyList<string> OccupationGroups,
    string HeadlinePattern);

/// <summary>
/// The one place the junior IT list meets the taxonomy (ADR 0186). The public list reads through it today, and the
/// <c>/jobb</c> filter (#2261) is meant to, so the field's groups are worked out in one place only.
/// </summary>
internal sealed class JuniorItSetResolver(JuniorItList list, ITaxonomyReadModel taxonomy)
{
    public async ValueTask<JuniorItSet> ResolveAsync(CancellationToken cancellationToken)
    {
        var tree = await taxonomy.GetTreeAsync(cancellationToken);
        var field = tree.OccupationFields.FirstOrDefault(f =>
            string.Equals(f.ConceptId, list.OccupationFieldConceptId, StringComparison.Ordinal));

        // A field the taxonomy no longer knows, or one without groups, would turn the headline arm off without a word.
        if (field is null || field.OccupationGroups.Count == 0)
            throw new InvalidOperationException(
                $"The junior IT list's occupation field {list.OccupationFieldConceptId} has no occupation groups in the taxonomy.");

        return new JuniorItSet(
            list.CuratedExternalIds,
            field.OccupationGroups.Select(g => g.ConceptId).ToList(),
            list.HeadlinePattern);
    }
}
