using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// #2143 (ADR 0167) — turns the raw search text of a <see cref="JobAdFilterCriteria"/> into what the
/// shared filter composes: the positive residual and each minus-word as a <see cref="TextTerm"/>, and
/// the taxonomy concepts the minus-words name exactly. Every method of the two search ports calls it
/// first, so list, count, facets, the per-user paths and every replay of a stored search parse the
/// same text the same way.
/// </summary>
internal sealed class JobAdSearchTextResolver(
    ISearchQueryParser parser,
    IOccupationSynonymExpander synonymExpander,
    ITaxonomyLabelMatcher labelMatcher)
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

        return new ResolvedJobAdFilter(criteria, new ResolvedSearchText(positive, excluded, labels));
    }

    // The substring arms need a trigram the index can serve (TD-94 / ADR 0062-amendment 2026-06-13;
    // #2143): without one, the LIKE scans the whole index and the recheck detoasts every row.
    private TextTerm ToTerm(string term, TextTermRole role) => new(
        term,
        LikePatterns.HasIndexableTrigram(term) ? LikePatterns.Contains(term) : null,
        synonymExpander.Expand(term),
        role);
}

/// <summary>
/// A filter whose search text has been resolved. <see cref="JobAdSearchComposition.ApplyFilter"/> takes
/// only this, so it cannot compose a text that was resolved from another criteria.
/// </summary>
internal sealed class ResolvedJobAdFilter(JobAdFilterCriteria criteria, ResolvedSearchText text)
{
    public JobAdFilterCriteria Criteria { get; } = criteria;

    public ResolvedSearchText Text { get; } = text;
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
