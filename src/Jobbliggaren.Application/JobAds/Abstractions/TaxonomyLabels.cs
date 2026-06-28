using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// One owner for the <see cref="ITaxonomyReadModel.ResolveLabelsAsync"/>
/// graceful-degradation fallback (ADR 0043). When a concept-id does not resolve
/// (taxonomy drift, removed code) the port returns this label instead of
/// throwing. Both the Infrastructure implementation and any consumer that needs
/// to detect an unresolved label reference this single source, so the format is
/// never duplicated as a magic string across the layer boundary (CLAUDE.md §5;
/// review 2026-06-28 — flagged by code-reviewer / dotnet-architect /
/// security-auditor).
/// </summary>
public static class TaxonomyLabels
{
    /// <summary>The fallback label for an unresolved concept-id.</summary>
    public static string Unknown(string conceptId) => $"Okänd kod ({conceptId})";

    /// <summary>
    /// True when <paramref name="label"/> is the unresolved fallback (its label
    /// equals <see cref="Unknown"/> for its own concept-id) — a consumer that
    /// must not surface an opaque id can drop it rather than display it.
    /// </summary>
    public static bool IsUnresolved(TaxonomyLabelDto label) =>
        label.Label == Unknown(label.ConceptId);
}
