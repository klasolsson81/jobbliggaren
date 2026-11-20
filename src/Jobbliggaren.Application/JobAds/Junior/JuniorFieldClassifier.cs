using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Junior;

/// <summary>
/// #2264 — runs the rules of the enabled field an ad's occupation group belongs to, or answers <see langword="null"/>
/// for an ad in no enabled field.
/// </summary>
public sealed class JuniorFieldClassifier
{
    private readonly Dictionary<string, JuniorField> _byGroup = new(StringComparer.Ordinal);

    public JuniorFieldClassifier(IReadOnlyList<JuniorField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var field in fields)
        {
            foreach (var group in field.OccupationGroups)
                _byGroup.TryAdd(group, field);
        }
    }

    public JuniorClassification? Classify(string? occupationGroupConceptId, JuniorClassificationInput input) =>
        occupationGroupConceptId is { } group && _byGroup.TryGetValue(group, out var field)
            ? JuniorClassifier.Classify(field.Rules, input)
            : null;
}
