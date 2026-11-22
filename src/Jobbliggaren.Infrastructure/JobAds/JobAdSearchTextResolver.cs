using System.Linq.Expressions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// #2143 (ADR 0167) — turns the raw search text of a <see cref="JobAdFilterCriteria"/> into what the
/// shared filter composes: the positive residual and each minus-word as a <see cref="TextTerm"/>, and
/// the taxonomy concepts the minus-words name exactly. Every method of the two search ports calls it
/// first, so list, count, facets, the per-user paths and every replay of a stored search parse the
/// same text the same way.
/// <para>
/// #2199 — it also expands the occupation-name picks through the lineage, and folds the
/// occupation-name counts with the same lineage, so the filter and the counts have one reader.
/// </para>
/// <para>
/// #2261, #2264 — it also builds the junior IT list's membership predicate when the criteria ask for it:
/// <see cref="JuniorMembership.Of"/> over the stored assessments, the one definition <c>/junior</c> reads too
/// (ADR 0187). The filter carries the predicate, never its ingredients.
/// </para>
/// </summary>
internal sealed class JobAdSearchTextResolver(
    ISearchQueryParser parser,
    IOccupationSynonymExpander synonymExpander,
    ITaxonomyLabelMatcher labelMatcher,
    IOccupationNameLineageProvider occupationNames,
    AppDbContext db,
    IJuniorFieldCatalog juniorFields)
{
    public async ValueTask<ResolvedJobAdFilter> ResolveAsync(
        JobAdFilterCriteria criteria, CancellationToken cancellationToken)
    {
        var parsed = parser.Parse(criteria.Q);
        var positive = parsed.ResidualQ is { } residual ? ToTerm(residual, TextTermRole.Positive) : null;
        var excluded = parsed.ExcludedTerms.Select(term => ToTerm(term, TextTermRole.Excluded)).ToList();
        var labels = excluded.Count == 0
            ? TaxonomyLabelMatchDto.Empty
            : await labelMatcher.MatchExactLabelsAsync(parsed.ExcludedTerms, cancellationToken);
        var junior = criteria.JuniorIt
            ? JuniorMembership.Of(
                db.JuniorAssessments,
                JuniorMembership.GroupsOf(await juniorFields.ResolveEnabledAsync(cancellationToken)))
            : null;

        return new ResolvedJobAdFilter(
            criteria,
            new ResolvedSearchText(positive, excluded, labels),
            occupationNames.Lineage.Expand(criteria.OccupationName),
            junior);
    }

    /// <summary>#2199 — the exact count per occupation-name pick, from ad counts grouped by (name, group).</summary>
    public IReadOnlyDictionary<string, int> CountOccupationNames(IEnumerable<OccupationNamePairCount> rows) =>
        occupationNames.Lineage.CountByPick(rows);

    // The substring arms need a trigram the index can serve (TD-94 / ADR 0062-amendment 2026-06-13;
    // #2143): without one, the LIKE scans the whole index and the recheck detoasts every row.
    private TextTerm ToTerm(string term, TextTermRole role) => new(
        term,
        LikePatterns.HasIndexableTrigram(term) ? LikePatterns.Contains(term) : null,
        synonymExpander.Expand(term),
        role);
}

/// <summary>
/// A filter whose search text and occupation-name picks have been resolved.
/// <see cref="JobAdSearchComposition.ApplyFilter"/> takes only this, so it cannot compose a resolution
/// taken from another criteria.
/// </summary>
internal sealed class ResolvedJobAdFilter(
    JobAdFilterCriteria criteria,
    ResolvedSearchText text,
    OccupationNameExpansion occupationNames,
    Expression<Func<JobAd, bool>>? juniorItMembership)
{
    public JobAdFilterCriteria Criteria { get; } = criteria;

    public ResolvedSearchText Text { get; } = text;

    /// <summary>#2199 — <see cref="JobAdFilterCriteria.OccupationName"/> expanded through the lineage.</summary>
    public OccupationNameExpansion OccupationNames { get; } = occupationNames;

    /// <summary>#2261 — the junior IT list's membership when <see cref="JobAdFilterCriteria.JuniorIt"/> is on, else null.</summary>
    public Expression<Func<JobAd, bool>>? JuniorItMembership { get; } = juniorItMembership;
}

/// <summary>The positive residual, the minus-words, and the concepts the minus-words name exactly.</summary>
internal sealed record ResolvedSearchText(
    TextTerm? Positive,
    IReadOnlyList<TextTerm> Excluded,
    TaxonomyLabelMatchDto ExcludedLabels)
{
    /// <summary>
    /// True when the composed query carries a full-text predicate — positive or excluded — and so
    /// reads <c>search_vector</c>; the count paths gate the bitmap-plan hygiene on it (#744, TD-94).
    /// </summary>
    public bool HasFreeTextPredicate => Positive is not null || Excluded.Count > 0;
}

/// <summary>
/// One word or residual phrase of the search text, with what each match arm needs: the term for
/// the full-text arm, the escaped LIKE pattern (null without an indexable trigram) and the expanded
/// SSYK ids.
/// </summary>
internal sealed record TextTerm(
    string Term, string? ContainsPattern, IReadOnlyCollection<string> Ssyks, TextTermRole Role);

internal enum TextTermRole
{
    Positive,
    Excluded,
}
