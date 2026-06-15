namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// The deterministic keyword/skill extraction of a single <see cref="JobAd"/>
/// (F4-4, ADR 0071/0074). An immutable, normalized value object: the canonical,
/// deduplicated, deterministically-ordered, bounded set of <see cref="ExtractedTerm"/>s.
/// <para>
/// <b>Empty is valid</b> — it is the "not-yet-extracted" / "nothing matched"
/// state (every one of the ~54k pre-F4-4 rows is empty until the backfill runs;
/// an ad whose text resolves to nothing is also empty). No non-empty invariant
/// (unlike <c>SearchCriteria</c>).
/// </para>
/// <para>
/// <see cref="From"/> is the single normalization point — the extractor and the
/// jsonb read-path both go through it, so the persisted form is canonical and
/// idempotent (re-reading a stored value yields the same instance shape).
/// </para>
/// </summary>
public sealed class ExtractedTerms : IEquatable<ExtractedTerms>
{
    /// <summary>
    /// Relevance/DoS bound on the persisted term count. NOT a coverage claim — a
    /// rich ad simply keeps its most-relevant terms (skills survive before generic
    /// keywords via the sort below). Documented per the no-silent-cap discipline.
    /// </summary>
    public const int MaxTerms = 64;

    public static ExtractedTerms Empty { get; } = new([]);

    public IReadOnlyList<ExtractedTerm> Terms { get; }

    public bool IsEmpty => Terms.Count == 0;

    private ExtractedTerms(IReadOnlyList<ExtractedTerm> terms) => Terms = terms;

    /// <summary>
    /// Builds the canonical value object: validates each term's invariants,
    /// deduplicates on (<see cref="ExtractedTerm.Lexeme"/>,
    /// <see cref="ExtractedTerm.Kind"/>, <see cref="ExtractedTerm.Source"/>)
    /// keeping the highest weight, sorts deterministically
    /// (Kind → Weight desc → Lexeme Ordinal → Source) and caps at
    /// <see cref="MaxTerms"/>. An empty/blank input yields <see cref="Empty"/>.
    /// Throws <see cref="ArgumentException"/> on a malformed term (unexpected —
    /// the extractor never produces one; a throw here surfaces corrupt jsonb or a
    /// bug rather than silently persisting it).
    /// </summary>
    public static ExtractedTerms From(IEnumerable<ExtractedTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        // Dedupe keeping the strongest (highest-weight) occurrence per identity.
        var byKey = new Dictionary<(string, ExtractedTermKind, ExtractedTermSource), ExtractedTerm>();
        foreach (var term in terms)
        {
            Validate(term);
            var key = (term.Lexeme, term.Kind, term.Source);
            if (!byKey.TryGetValue(key, out var existing) || term.Weight > existing.Weight)
                byKey[key] = term;
        }

        if (byKey.Count == 0)
            return Empty;

        var ordered = byKey.Values
            .OrderBy(t => (int)t.Kind)
            .ThenByDescending(t => t.Weight)
            .ThenBy(t => t.Lexeme, StringComparer.Ordinal)
            .ThenBy(t => (int)t.Source)
            .Take(MaxTerms)
            .ToList();

        return new ExtractedTerms(ordered);
    }

    private static void Validate(ExtractedTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        if (string.IsNullOrWhiteSpace(term.Lexeme))
            throw new ArgumentException("ExtractedTerm.Lexeme must be non-empty.", nameof(term));
        if (string.IsNullOrWhiteSpace(term.Display))
            throw new ArgumentException("ExtractedTerm.Display must be non-empty.", nameof(term));
        // Evidence-citation invariant (ADR 0074): every term cites its source span.
        if (string.IsNullOrWhiteSpace(term.MatchedOn))
            throw new ArgumentException("ExtractedTerm.MatchedOn (cited evidence) must be non-empty.", nameof(term));
        if (!double.IsFinite(term.Weight) || term.Weight < 0)
            throw new ArgumentException("ExtractedTerm.Weight must be finite and non-negative.", nameof(term));

        // Skill ⇒ taxonomy concept-id present and == the overlap token; Keyword ⇒ no concept-id.
        switch (term.Kind)
        {
            case ExtractedTermKind.Skill:
                if (string.IsNullOrWhiteSpace(term.ConceptId))
                    throw new ArgumentException("A Skill term must carry a ConceptId.", nameof(term));
                if (!string.Equals(term.ConceptId, term.Lexeme, StringComparison.Ordinal))
                    throw new ArgumentException("A Skill term's Lexeme must equal its ConceptId (concept-level overlap token).", nameof(term));
                break;
            case ExtractedTermKind.Keyword:
                if (term.ConceptId is not null)
                    throw new ArgumentException("A Keyword term must not carry a ConceptId.", nameof(term));
                break;
        }
    }

    public bool Equals(ExtractedTerms? other)
        => other is not null && Terms.SequenceEqual(other.Terms);

    public override bool Equals(object? obj) => Equals(obj as ExtractedTerms);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var term in Terms)
            hash.Add(term);
        return hash.ToHashCode();
    }
}
