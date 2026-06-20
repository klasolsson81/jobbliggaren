using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) — deterministic per-job-ad
/// keyword/skill extractor. NO AI/LLM: the title + description are normalized via
/// the F4-2 local NLP tier (<see cref="ITextAnalyzer.ToLexemes"/>, Snowball —
/// <c>to_tsvector('swedish')</c> parity) and matched against the committed JobTech
/// skill-taxonomy index (<see cref="SkillTaxonomyIndex"/>).
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
/// The inverted skill index lives in the shared singleton
/// <see cref="SkillTaxonomyIndex"/> (F4-15 extraction, ADR 0076 Decision 6) — the
/// SAME index the CV-side <see cref="SkillResolver"/> reuses, so ad-side extraction
/// and CV-side resolution can never diverge. This type owns only the ad-shaped
/// concerns (requirement pass, keyword fallback, title/description sourcing).
/// </para>
/// </summary>
internal sealed class JobAdKeywordExtractor : IJobAdKeywordExtractor
{
    private readonly ITextAnalyzer _analyzer;
    private readonly IStemmer _stemmer;
    private readonly SkillTaxonomyIndex _index;

    public JobAdKeywordExtractor(ITextAnalyzer analyzer, IStemmer stemmer, SkillTaxonomyIndex index)
    {
        _analyzer = analyzer;
        _stemmer = stemmer;
        _index = index;
    }

    public ExtractedTerms Extract(JobAdExtractionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var terms = new List<ExtractedTerm>();

        // ---- Requirement pass (F4-4b): employer-stated must_have/nice_to_have skills.
        // Already taxonomy-concept-linked → NO NLP/taxonomy match (unlike the skill pass
        // over free text). Text-independent, so it runs before the no-text short-circuits
        // (requirements are never silently dropped). The overlap token IS the concept-id
        // (concept-level, same as a Skill); Source cites must_have vs nice_to_have;
        // MatchedOn cites the requirement label (explainable by design, CLAUDE.md §5).
        foreach (var requirement in input.Requirements ?? [])
        {
            terms.Add(new ExtractedTerm(
                Lexeme: requirement.ConceptId,
                Display: requirement.Label,
                Kind: ExtractedTermKind.Requirement,
                Source: requirement.Source,
                MatchedOn: requirement.Label,
                ConceptId: requirement.ConceptId,
                Weight: requirement.Weight));
        }

        var title = input.Title ?? string.Empty;
        var description = input.Description ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
            return ExtractedTerms.From(terms);

        // Lexeme streams (Snowball, stopwords dropped) — title separately so a
        // term's Source can cite where it occurred. allLex keeps duplicates → the
        // keyword weight is the within-ad term frequency.
        var titleLex = _analyzer.ToLexemes(title, TextLanguage.Swedish);
        var descLex = _analyzer.ToLexemes(description, TextLanguage.Swedish);
        var titleSet = titleLex.ToHashSet(StringComparer.Ordinal);
        var adSet = new HashSet<string>(titleLex, StringComparer.Ordinal);
        adSet.UnionWith(descLex);
        if (adSet.Count == 0)
            return ExtractedTerms.From(terms);

        var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var lexeme in titleLex)
            frequency[lexeme] = frequency.GetValueOrDefault(lexeme) + 1;
        foreach (var lexeme in descLex)
            frequency[lexeme] = frequency.GetValueOrDefault(lexeme) + 1;

        // ---- Skill pass: bag-containment against the shared inverted index
        // (SkillTaxonomyIndex). The strongest (most-specific = most lexemes) form
        // per concept-id wins — the SAME core the CV-side resolver reuses.
        var bestForms = _index.MatchForms(adSet);

        var skillConsumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in bestForms)
            skillConsumed.UnionWith(form.Lexemes);

        terms.EnsureCapacity(terms.Count + bestForms.Count + adSet.Count);

        foreach (var form in bestForms)
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

    private static bool AnyInTitle(IReadOnlyCollection<string> formLexemes, HashSet<string> titleSet)
    {
        foreach (var lexeme in formLexemes)
            if (titleSet.Contains(lexeme))
                return true;
        return false;
    }

    // Re-tokenize (lowercase → letter/digit runs, the LocalTextAnalyzer
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

    // Maximal runs of letters/digits, lowercased — mirrors LocalTextAnalyzer's
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
}
