using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the shared, lazily-built inverted
/// JobTech skill-taxonomy index. Extracted behaviour-preservingly from
/// <see cref="JobAdKeywordExtractor"/> (à la F4-14's <c>JobAdSearchComposition</c>)
/// so that BOTH the ad-side extractor AND the CV-side <see cref="SkillResolver"/>
/// resolve free text against ONE index — never a parallel resolver (ADR 0076
/// Decision 6; the existing <c>JobAdKeywordExtractor</c> integration tests are the
/// regression gate for the extraction). NO AI/LLM (ADR 0071).
/// <para>
/// The index inverts every skill-concept label form (preferred + synonyms) on its
/// RAREST lexeme (document frequency over the ~20k-concept corpus), so per-call
/// matching only probes selective lexemes. <see cref="MatchForms"/> returns the
/// most-specific (most-lexemes) form per concept-id whose label lexemes are all
/// contained in a given lexeme bag (bag containment). Immutable reference data;
/// the singleton holds the <see cref="Lazy{T}"/> so the build runs once.
/// </para>
/// <para>
/// Reads only the committed taxonomy snapshot + the public input text via
/// <see cref="ITextAnalyzer.ToLexemes"/> (Snowball, <c>to_tsvector('swedish')</c>
/// parity). It takes no <c>ILogger</c> and never logs the input (CLAUDE.md §5 —
/// the CV skill names it resolves are PII-adjacent).
/// </para>
/// </summary>
internal sealed class SkillTaxonomyIndex
{
    private readonly ITextAnalyzer _analyzer;
    private readonly Lazy<SkillIndex> _index;

    public SkillTaxonomyIndex(ITextAnalyzer analyzer)
    {
        _analyzer = analyzer;
        _index = new Lazy<SkillIndex>(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// The CV-side resolver (F4-15): free text → distinct JobTech skill concept-ids.
    /// Normalises <paramref name="freeText"/> to its Swedish lexeme set (the same
    /// pipeline the ad side uses), matches the best form per concept-id (bag
    /// containment), and returns each winning concept-id. Blank/empty input or no
    /// match → empty (never throws — an unresolvable CV skill is normal, not an error).
    /// </summary>
    public IReadOnlyList<string> ResolveConceptIds(string freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return [];

        var lexemes = _analyzer.ToLexemes(freeText, TextLanguage.Swedish).ToHashSet(StringComparer.Ordinal);
        if (lexemes.Count == 0)
            return [];

        var forms = MatchForms(lexemes);
        if (forms.Count == 0)
            return [];

        // Distinct concept-ids (MatchForms already keeps one form per concept-id).
        return forms.Select(f => f.ConceptId).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The ad-side / shared matching core (F4-4 skill pass, lifted verbatim): the
    /// most-specific form per concept-id whose label lexemes are ALL present in
    /// <paramref name="lexemeBag"/>. A form is only checked when its anchor (rarest)
    /// lexeme is in the bag.
    /// </summary>
    public IReadOnlyCollection<SkillForm> MatchForms(IReadOnlySet<string> lexemeBag)
    {
        var index = _index.Value;
        var bestByConcept = new Dictionary<string, SkillForm>(StringComparer.Ordinal);
        foreach (var lexeme in lexemeBag)
        {
            if (!index.ByAnchor.TryGetValue(lexeme, out var forms))
                continue;
            foreach (var form in forms)
            {
                if (!ContainsAll(lexemeBag, form.Lexemes))
                    continue;
                if (!bestByConcept.TryGetValue(form.ConceptId, out var existing)
                    || IsMoreSpecific(form, existing))
                {
                    bestByConcept[form.ConceptId] = form;
                }
            }
        }

        // Materialize (not the live Dictionary.Values view) so the result is a stable
        // snapshot — robust to any future caller that holds it across a mutation.
        return bestByConcept.Values.ToList();
    }

    private static bool ContainsAll(IReadOnlySet<string> bag, IReadOnlyCollection<string> formLexemes)
    {
        foreach (var lexeme in formLexemes)
            if (!bag.Contains(lexeme))
                return false;
        return true;
    }

    // More lexemes = more specific; deterministic tiebreak on the cited label.
    private static bool IsMoreSpecific(SkillForm candidate, SkillForm current)
    {
        if (candidate.Lexemes.Count != current.Lexemes.Count)
            return candidate.Lexemes.Count > current.Lexemes.Count;
        return string.CompareOrdinal(candidate.MatchedOn, current.MatchedOn) < 0;
    }

    private SkillIndex BuildIndex()
    {
        var concepts = JobAdSkillTaxonomyLoader.Load();

        // Flatten to label forms (preferred + synonyms), deduped per
        // (concept, lexeme-set) so a redundant synonym does not double the work.
        var forms = new List<SkillForm>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in concepts)
        {
            AddForm(concept, concept.PreferredLabel, forms, seen);
            foreach (var synonym in concept.Synonyms)
                AddForm(concept, synonym, forms, seen);
        }

        // Document frequency of each lexeme across all forms → anchor each form on
        // its RAREST lexeme so per-call matching only probes selective lexemes.
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var form in forms)
            foreach (var lexeme in form.Lexemes)
                df[lexeme] = df.GetValueOrDefault(lexeme) + 1;

        var byAnchor = new Dictionary<string, List<SkillForm>>(StringComparer.Ordinal);
        foreach (var form in forms)
        {
            var anchor = RarestLexeme(form.Lexemes, df);
            if (!byAnchor.TryGetValue(anchor, out var list))
                byAnchor[anchor] = list = [];
            list.Add(form);
        }

        return new SkillIndex(byAnchor);
    }

    private void AddForm(SkillConcept concept, string label, List<SkillForm> forms, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;
        var lexemes = _analyzer.ToLexemes(label, TextLanguage.Swedish).ToHashSet(StringComparer.Ordinal);
        if (lexemes.Count == 0)
            return;
        // Dedupe identical (concept, lexeme-set) forms.
        var key = concept.ConceptId + "|" + string.Join('|', lexemes.OrderBy(x => x, StringComparer.Ordinal));
        if (!seen.Add(key))
            return;
        forms.Add(new SkillForm(concept.ConceptId, concept.PreferredLabel, label.Trim(), lexemes));
    }

    private static string RarestLexeme(IReadOnlyCollection<string> lexemes, Dictionary<string, int> df)
    {
        var rarest = string.Empty;
        var min = int.MaxValue;
        foreach (var lexeme in lexemes)
        {
            var count = df.GetValueOrDefault(lexeme, 0);
            // Deterministic tiebreak on Ordinal so the anchor is reproducible.
            if (count < min || (count == min && string.CompareOrdinal(lexeme, rarest) < 0))
            {
                min = count;
                rarest = lexeme;
            }
        }
        return rarest;
    }

    private sealed record SkillIndex(IReadOnlyDictionary<string, List<SkillForm>> ByAnchor);
}

/// <summary>
/// One matchable skill label form: its concept-id (+ preferred label for display),
/// the source label span (cited evidence) and its lexeme set. Shared by
/// <see cref="SkillTaxonomyIndex"/>'s consumers (the extractor's skill pass + the
/// CV-side resolver); <c>internal</c> so it never leaks past Infrastructure.
/// </summary>
internal sealed record SkillForm(
    string ConceptId,
    string PreferredLabel,
    string MatchedOn,
    IReadOnlySet<string> Lexemes);
