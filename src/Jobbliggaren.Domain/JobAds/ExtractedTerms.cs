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
    /// rich ad simply keeps its most-relevant terms (requirements then skills then
    /// generic keywords survive via the sort below). Documented per the
    /// no-silent-cap discipline.
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
    /// (Kind-rank → Weight desc → Lexeme Ordinal → Source) and caps at
    /// <see cref="MaxTerms"/>. The primary key is a Kind→rank (see
    /// <see cref="SortRank"/>): <see cref="ExtractedTermKind.Requirement"/> →
    /// <see cref="ExtractedTermKind.Skill"/> → <see cref="ExtractedTermKind.Keyword"/>
    /// (F4-4b — an employer-stated requirement is the highest-authority match signal
    /// and must survive the cap before NLP-derived skills/keywords). An empty/blank
    /// input yields <see cref="Empty"/>. Throws <see cref="ArgumentException"/> on a
    /// malformed term (unexpected — the extractor never produces one; a throw here
    /// surfaces corrupt jsonb or a bug rather than silently persisting it).
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
            .OrderBy(t => SortRank(t.Kind))
            .ThenByDescending(t => t.Weight)
            .ThenBy(t => t.Lexeme, StringComparer.Ordinal)
            .ThenBy(t => (int)t.Source)
            .Take(MaxTerms)
            .ToList();

        return new ExtractedTerms(ordered);
    }

    /// <summary>
    /// Primary sort priority by <see cref="ExtractedTermKind"/> (F4-4b, CTO
    /// Decision 1c): <see cref="ExtractedTermKind.Requirement"/> (0) →
    /// <see cref="ExtractedTermKind.Skill"/> (1) →
    /// <see cref="ExtractedTermKind.Keyword"/> (2). Employer-stated requirements
    /// outrank our NLP-derived skills, which outrank generic keywords — so a
    /// requirement always survives the <see cref="MaxTerms"/> cap before an
    /// incidental keyword. Deliberately decoupled from the enum's numeric values
    /// (kept stable so the persisted jsonb enum strings never shift).
    /// </summary>
    private static int SortRank(ExtractedTermKind kind) => kind switch
    {
        ExtractedTermKind.Requirement => 0,
        ExtractedTermKind.Skill => 1,
        ExtractedTermKind.Keyword => 2,
        _ => int.MaxValue,
    };

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

        // Per-Kind invariants. Skill/Requirement ⇒ taxonomy concept-id present and
        // == the overlap token; Keyword ⇒ no concept-id. Source is tightened
        // (F4-4b): Skill/Keyword come from ad text (Title/Description); a Requirement
        // is an employer-stated must_have/nice_to_have.
        switch (term.Kind)
        {
            case ExtractedTermKind.Skill:
                RequireConceptIdEqualsLexeme(term, "Skill");
                RequireTextSource(term, "Skill");
                break;
            case ExtractedTermKind.Keyword:
                if (term.ConceptId is not null)
                    throw new ArgumentException("A Keyword term must not carry a ConceptId.", nameof(term));
                RequireTextSource(term, "Keyword");
                break;
            case ExtractedTermKind.Requirement:
                RequireConceptIdEqualsLexeme(term, "Requirement");
                if (term.Source is not (ExtractedTermSource.MustHave or ExtractedTermSource.NiceToHave))
                    throw new ArgumentException(
                        "A Requirement term's Source must be MustHave or NiceToHave.", nameof(term));
                break;
        }
    }

    // A Skill/Requirement term is concept-level: it must carry a taxonomy concept-id,
    // and its Lexeme (the set-overlap token) IS that concept-id.
    private static void RequireConceptIdEqualsLexeme(ExtractedTerm term, string kind)
    {
        if (string.IsNullOrWhiteSpace(term.ConceptId))
            throw new ArgumentException($"A {kind} term must carry a ConceptId.", nameof(term));
        if (!string.Equals(term.ConceptId, term.Lexeme, StringComparison.Ordinal))
            throw new ArgumentException(
                $"A {kind} term's Lexeme must equal its ConceptId (concept-level overlap token).", nameof(term));
    }

    // A Skill/Keyword originates from the ad text — its Source is Title or Description
    // (never a requirement source). Closes a silent modelling gap (F4-4b).
    private static void RequireTextSource(ExtractedTerm term, string kind)
    {
        if (term.Source is not (ExtractedTermSource.Title or ExtractedTermSource.Description))
            throw new ArgumentException(
                $"A {kind} term's Source must be Title or Description.", nameof(term));
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
