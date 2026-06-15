using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) — deterministic per-job-ad
/// keyword/skill extractor. NO AI/LLM: the title + description are normalized via
/// the F4-2 local NLP tier (<see cref="ITextAnalyzer.ToLexemes"/>, Snowball —
/// <c>to_tsvector('swedish')</c> parity) and matched against the committed JobTech
/// skill-taxonomy + synonym snapshot (<see cref="JobAdSkillTaxonomyLoader"/>).
/// <list type="number">
/// <item><b>Skill</b> terms — a taxonomy skill concept whose label/synonym lexemes
/// are all present in the ad (bag containment), resolved to its concept-id.</item>
/// <item><b>Keyword</b> terms — the remaining salient lexemes not consumed by a
/// skill, the honest fallback the OQ4 coverage gap implies.</item>
/// </list>
/// Every term cites its evidence (<c>MatchedOn</c> — explainable by design,
/// CLAUDE.md §5). The result is normalized + bounded by
/// <see cref="ExtractedTerms.From"/>. The ad text is NEVER logged (this type takes
/// no <c>ILogger</c>); only public ad text is read (no CV-PII, ADR 0074 inv. 3).
/// <para>
/// Singleton with a lazily-built skill index (inverted on each label form's rarest
/// lexeme → bounded per-ad work over the ~54k corpus). Mirrors
/// <see cref="OccupationCodeDeriver"/>; the index is immutable reference data.
/// </para>
/// </summary>
internal sealed class JobAdKeywordExtractor : IJobAdKeywordExtractor
{
    private readonly ITextAnalyzer _analyzer;
    private readonly IStemmer _stemmer;
    private readonly Lazy<SkillIndex> _index;

    public JobAdKeywordExtractor(ITextAnalyzer analyzer, IStemmer stemmer)
    {
        _analyzer = analyzer;
        _stemmer = stemmer;
        _index = new Lazy<SkillIndex>(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ExtractedTerms Extract(JobAdExtractionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var title = input.Title ?? string.Empty;
        var description = input.Description ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
            return ExtractedTerms.Empty;

        // Lexeme streams (Snowball, stopwords dropped) — title separately so a
        // term's Source can cite where it occurred. allLex keeps duplicates → the
        // keyword weight is the within-ad term frequency.
        var titleLex = _analyzer.ToLexemes(title, TextLanguage.Swedish);
        var descLex = _analyzer.ToLexemes(description, TextLanguage.Swedish);
        var titleSet = titleLex.ToHashSet(StringComparer.Ordinal);
        var adSet = new HashSet<string>(titleLex, StringComparer.Ordinal);
        adSet.UnionWith(descLex);
        if (adSet.Count == 0)
            return ExtractedTerms.Empty;

        var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var lexeme in titleLex)
            frequency[lexeme] = frequency.GetValueOrDefault(lexeme) + 1;
        foreach (var lexeme in descLex)
            frequency[lexeme] = frequency.GetValueOrDefault(lexeme) + 1;

        var index = _index.Value;

        // ---- Skill pass: bag-containment against the inverted index. ----
        // A form is only checked when its anchor (rarest) lexeme is in the ad; the
        // strongest (most-specific = most lexemes) form per concept-id wins.
        var bestByConcept = new Dictionary<string, SkillForm>(StringComparer.Ordinal);
        foreach (var lexeme in adSet)
        {
            if (!index.ByAnchor.TryGetValue(lexeme, out var forms))
                continue;
            foreach (var form in forms)
            {
                if (!ContainsAll(adSet, form.Lexemes))
                    continue;
                if (!bestByConcept.TryGetValue(form.ConceptId, out var existing)
                    || IsMoreSpecific(form, existing))
                {
                    bestByConcept[form.ConceptId] = form;
                }
            }
        }

        var skillConsumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in bestByConcept.Values)
            skillConsumed.UnionWith(form.Lexemes);

        var terms = new List<ExtractedTerm>(bestByConcept.Count + adSet.Count);

        foreach (var form in bestByConcept.Values)
        {
            var source = AnyInTitle(form.Lexemes, titleSet)
                ? ExtractedTermSource.Title
                : ExtractedTermSource.Description;
            terms.Add(new ExtractedTerm(
                Lexeme: form.ConceptId,        // concept-level overlap token
                Display: form.PreferredLabel,
                Kind: ExtractedTermKind.Skill,
                Source: source,
                MatchedOn: form.MatchedOn,      // the matched label/synonym span (cited evidence)
                ConceptId: form.ConceptId,
                Weight: form.Lexemes.Count));   // specificity
        }

        // ---- Keyword pass: salient lexemes not consumed by a skill. ----
        // A representative surface form (for Display/evidence) is recovered by
        // re-tokenizing the ad text with the same pipeline; the stem falls back to
        // itself if no surface is found (robust to any tokenization edge).
        var stemToSurface = BuildSurfaceMap(title, description, adSet, skillConsumed);
        foreach (var stem in adSet)
        {
            if (skillConsumed.Contains(stem))
                continue;
            var surface = stemToSurface.GetValueOrDefault(stem, stem);
            var source = titleSet.Contains(stem)
                ? ExtractedTermSource.Title
                : ExtractedTermSource.Description;
            terms.Add(new ExtractedTerm(
                Lexeme: stem,
                Display: surface,
                Kind: ExtractedTermKind.Keyword,
                Source: source,
                MatchedOn: surface,
                ConceptId: null,
                Weight: frequency.GetValueOrDefault(stem, 1)));
        }

        return ExtractedTerms.From(terms);
    }

    private static bool ContainsAll(HashSet<string> ad, IReadOnlyCollection<string> formLexemes)
    {
        foreach (var lexeme in formLexemes)
            if (!ad.Contains(lexeme))
                return false;
        return true;
    }

    private static bool AnyInTitle(IReadOnlyCollection<string> formLexemes, HashSet<string> titleSet)
    {
        foreach (var lexeme in formLexemes)
            if (titleSet.Contains(lexeme))
                return true;
        return false;
    }

    // More lexemes = more specific; deterministic tiebreak on the cited label.
    private static bool IsMoreSpecific(SkillForm candidate, SkillForm current)
    {
        if (candidate.Lexemes.Count != current.Lexemes.Count)
            return candidate.Lexemes.Count > current.Lexemes.Count;
        return string.CompareOrdinal(candidate.MatchedOn, current.MatchedOn) < 0;
    }

    // Re-tokenize (lowercase → letter/digit runs, the SwedishTextAnalyzer
    // tokenization) and stem each surface token with the same Snowball stemmer, so
    // a keyword's Display is a real surface form rather than a truncated stem. Only
    // stems present in the ad's lexeme set and not consumed by a skill are mapped;
    // the first surface seen wins (deterministic).
    private Dictionary<string, string> BuildSurfaceMap(
        string title, string description, HashSet<string> adSet, HashSet<string> skillConsumed)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        AddSurfaces(title, adSet, skillConsumed, map);
        AddSurfaces(description, adSet, skillConsumed, map);
        return map;
    }

    private void AddSurfaces(
        string text, HashSet<string> adSet, HashSet<string> skillConsumed, Dictionary<string, string> map)
    {
        if (string.IsNullOrEmpty(text))
            return;
        foreach (var surface in Tokenize(text))
        {
            var stem = _stemmer.Stem(surface, TextLanguage.Swedish);
            if (string.IsNullOrEmpty(stem)
                || skillConsumed.Contains(stem)
                || !adSet.Contains(stem)
                || map.ContainsKey(stem))
            {
                continue;
            }
            map[stem] = surface;
        }
    }

    // Maximal runs of letters/digits, lowercased — mirrors SwedishTextAnalyzer's
    // tokenization (åäö are letters and stay in tokens).
    private static IEnumerable<string> Tokenize(string text)
    {
        var lower = text.ToLowerInvariant();
        var start = -1;
        for (var i = 0; i < lower.Length; i++)
        {
            if (char.IsLetterOrDigit(lower[i]))
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                yield return lower[start..i];
                start = -1;
            }
        }
        if (start >= 0)
            yield return lower[start..];
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
        // its RAREST lexeme so per-ad matching only probes selective lexemes.
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

    // One matchable skill label form: its concept-id (+ preferred label for
    // display), the source label span (cited evidence) and its lexeme set.
    private sealed record SkillForm(
        string ConceptId,
        string PreferredLabel,
        string MatchedOn,
        IReadOnlySet<string> Lexemes);

    private sealed record SkillIndex(IReadOnlyDictionary<string, List<SkillForm>> ByAnchor);
}
