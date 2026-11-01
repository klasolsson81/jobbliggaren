using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Application.Applications.Queries;

/// <summary>
/// The "Ort" of an application's ad: its municipality concept-id resolved to a Swedish name through
/// the taxonomy ACL (ADR 0043), in ONE bounded call per read (#316, #2248). Shared by the activity
/// report and the two list reads so the card and the report can never name a different Ort. An id
/// that does not resolve (taxonomy drift) is dropped — an opaque concept-id is never shown (§5).
/// Not an <see cref="ITaxonomyReadModel"/> consumer itself: each caller passes the port it was
/// injected with, so the ACL allowlist keeps naming the handlers that read taxonomy.
/// </summary>
internal static class MunicipalityLabels
{
    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        ITaxonomyReadModel taxonomy, IEnumerable<string?> conceptIds, CancellationToken cancellationToken)
    {
        var distinct = conceptIds
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToList();

        if (distinct.Count == 0)
            return new Dictionary<string, string>();

        var labels = await taxonomy.ResolveLabelsAsync(distinct, cancellationToken);

        var map = new Dictionary<string, string>(labels.Count);
        foreach (var label in labels)
        {
            if (label.Label is not null)
                map[label.ConceptId] = label.Label;
        }

        return map;
    }

    public static string? For(IReadOnlyDictionary<string, string> labels, string? conceptId) =>
        conceptId is not null && labels.TryGetValue(conceptId, out var label) ? label : null;
}
