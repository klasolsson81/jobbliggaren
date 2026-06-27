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
    // ADR 0079 STEG 3 PR-C — skill typeahead bounds (a flat 20k-concept vocabulary
    // has no browsable hierarchy, unlike occupations → the FE searches server-side).
    private const int MinSearchQueryLength = 2;

    private readonly ITextAnalyzer _analyzer;
    private readonly Lazy<SkillIndex> _index;

    public SkillTaxonomyIndex(ITextAnalyzer analyzer)
    {
        _analyzer = analyzer;
        _index = new Lazy<SkillIndex>(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// ADR 0079 STEG 3 PR-C — skill typeahead for the editable skill chips' "add"
    /// affordance. Case-insensitive substring match of <paramref name="query"/> against
    /// every concept's preferred label + synonyms, deduped per concept-id (keeping the
    /// best rank), ranked PREFIX-before-CONTAINS, then shortest label, then ordinal
    /// (deterministic). Returns each winning concept-id with its canonical
    /// <c>PreferredLabel</c> (never a synonym — the chip displays the canonical label),
    /// capped at <paramref name="max"/>. Query shorter than the minimum, or blank →
    /// empty (no flooding on 1-char input). NOT a lexeme/Snowball match (that is
    /// <see cref="ResolveForms"/> for full skill names) — a typeahead needs literal
    /// substring so partial words ("jav") surface ("Java", "JavaScript").
    /// </summary>
    /// <summary>
    /// ADR 0079 STEG 3 PR-C — reverse-lookup (concept-id → canonical preferred label) for
    /// the saved skill chips' cold-load display, the skill analog of the occupation
    /// <c>ResolveTaxonomyLabels</c> (ADR 0043): the flat ~20k-concept skill vocabulary is
    /// never shipped to the FE as a tree, so a settings page that pre-fills stored
    /// PreferredSkills concept-ids resolves their labels here instead of rendering opaque
    /// ids. Unknown ids are dropped silently (graceful — never a crash, never a stale token).
    /// Deterministic ordinal order.
    /// </summary>
    public IReadOnlyList<(string ConceptId, string Label)> ResolveLabels(IEnumerable<string> conceptIds)
    {
        ArgumentNullException.ThrowIfNull(conceptIds);
        var labels = _index.Value.LabelByConceptId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(string ConceptId, string Label)>();
        foreach (var id in conceptIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                continue;
            if (labels.TryGetValue(id, out var label))
                result.Add((id, label));
        }
        result.Sort((a, b) => string.CompareOrdinal(a.ConceptId, b.ConceptId));
        return result;
    }

    public IReadOnlyList<(string ConceptId, string Label)> Search(string query, int max)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        var trimmed = query.Trim();
        if (trimmed.Length < MinSearchQueryLength || max <= 0)
            return [];

        var needle = trimmed.ToLowerInvariant();
        var bestRank = new Dictionary<string, int>(StringComparer.Ordinal);
        var labelById = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in _index.Value.SearchEntries)
        {
            var idx = entry.Normalized.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
                continue;
            var rank = idx == 0 ? 0 : 1; // prefix beats contains
            if (!bestRank.TryGetValue(entry.ConceptId, out var current) || rank < current)
            {
                bestRank[entry.ConceptId] = rank;
                labelById[entry.ConceptId] = entry.PreferredLabel;
            }
        }

        return bestRank
            .Select(kv => (kv.Key, Label: labelById[kv.Key], Rank: kv.Value))
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Label.Length)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .Take(max)
            .Select(x => (x.Key, x.Label))
            .ToList();
    }

    /// <summary>
    /// The CV-side resolver (F4-15): free text → distinct JobTech skill concept-ids.
    /// Normalises <paramref name="freeText"/> to its Swedish lexeme set (the same
    /// pipeline the ad side uses), matches the best form per concept-id (bag
    /// containment), and returns each winning concept-id. Blank/empty input or no
    /// match → empty (never throws — an unresolvable CV skill is normal, not an error).
    /// </summary>
    public IReadOnlyList<string> ResolveConceptIds(string freeText) =>
        // Distinct concept-ids (MatchForms already keeps one form per concept-id).
        ResolveForms(freeText).Select(f => f.ConceptId).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// ADR 0079 STEG 3 — like <see cref="ResolveConceptIds"/> but returns the winning
    /// <see cref="SkillForm"/> per concept-id (carrying the preferred label) so the CV-side
    /// resolver can surface user-readable skill chips. Blank/empty input or no match → empty
    /// (never throws — an unresolvable CV skill is normal, not an error).
    /// </summary>
    public IReadOnlyCollection<SkillForm> ResolveForms(string freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return [];

        var index = _index.Value;

        // #253 ACC-2/ACC-4 — exact-label fast-path: a discrete CV skill string that
        // LITERALLY matches a taxonomy label/synonym (case-insensitive, trimmed)
        // resolves to EXACTLY those concept(s), short-circuiting the lexeme-bag path
        // below. This is what stops "C#" (which tokenises to the bare lexeme {c}) from
        // fanning out to the C language + C++ across both layers, and keeps BOTH the
        // ESCO and AF "C#" twins (A-pure: correctness over chip-minimalism — the user
        // confirms which to keep). The ad-side MatchForms over free PROSE deliberately
        // does NOT get this path (prose carries no discrete skill string to match); the
        // ADR 0076 Decision 6 one-index parity holds — the CV-side concept set is now a
        // precise SUBSET of the ad-prose fan-out, never larger, so overlap stays sound.
        if (index.ExactByLabel.TryGetValue(freeText.Trim(), out var exact))
            return exact;

        var lexemes = _analyzer.ToLexemes(freeText, TextLanguage.Swedish).ToHashSet(StringComparer.Ordinal);
        if (lexemes.Count == 0)
            return [];

        return MatchForms(lexemes);
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
        // ADR 0079 STEG 3 PR-C — parallel substring-search entries (lower-cased label +
        // synonyms → concept-id + canonical preferred label) for the typeahead. Built once
        // alongside the lexeme index; the search returns the PreferredLabel even on a
        // synonym hit.
        var searchEntries = new List<SkillSearchEntry>();
        // ADR 0079 STEG 3 PR-C — concept-id → canonical label, for the saved-chip
        // reverse-lookup (one entry per concept; last write wins, but a concept-id is
        // unique in the snapshot so order is irrelevant).
        var labelByConceptId = new Dictionary<string, string>(StringComparer.Ordinal);
        // #253 ACC-2/ACC-4 — exact-label fast-path index: TRIMMED literal label/synonym
        // (case-insensitive) → the concepts that carry it verbatim, deduped per
        // concept-id (preferred-label match flagged so it can win over a synonym match
        // for the same concept). Built alongside the lexeme/search/label structures.
        var exactBuilder =
            new Dictionary<string, Dictionary<string, (SkillForm Form, bool IsPreferred)>>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var concept in concepts)
        {
            AddForm(concept, concept.PreferredLabel, forms, seen);
            AddSearchEntry(concept, concept.PreferredLabel, searchEntries);
            AddExactLabel(concept, concept.PreferredLabel, isPreferred: true, exactBuilder);
            if (!string.IsNullOrWhiteSpace(concept.PreferredLabel))
                labelByConceptId[concept.ConceptId] = concept.PreferredLabel;
            foreach (var synonym in concept.Synonyms)
            {
                AddForm(concept, synonym, forms, seen);
                AddSearchEntry(concept, synonym, searchEntries);
                AddExactLabel(concept, synonym, isPreferred: false, exactBuilder);
            }
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

        // Finalize the exact-label fast-path: per literal, the matching concepts'
        // forms — preferred-label matches first, then ConceptId Ordinal — so the order
        // is deterministic and reproducible regardless of asset row order (#253).
        var exactByLabel = new Dictionary<string, IReadOnlyList<SkillForm>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, byConcept) in exactBuilder)
        {
            exactByLabel[key] = byConcept.Values
                .OrderByDescending(e => e.IsPreferred)
                .ThenBy(e => e.Form.ConceptId, StringComparer.Ordinal)
                .Select(e => e.Form)
                .ToList();
        }

        return new SkillIndex(byAnchor, searchEntries, labelByConceptId, exactByLabel);
    }

    private static void AddSearchEntry(
        SkillConcept concept, string label, List<SkillSearchEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;
        entries.Add(new SkillSearchEntry(
            label.Trim().ToLowerInvariant(), concept.ConceptId, concept.PreferredLabel));
    }

    // #253 ACC-2/ACC-4 — record one literal label/synonym surface for the exact-label
    // fast-path. Keyed by the TRIMMED literal (case-insensitive), NOT lexemes — that is
    // the whole point: it bypasses the Snowball tokenisation that shreds "C#" → {c}.
    // Deduped per concept-id within a key; a preferred-label match wins over a synonym
    // match for the SAME concept (display + deterministic ordering).
    private void AddExactLabel(
        SkillConcept concept,
        string label,
        bool isPreferred,
        Dictionary<string, Dictionary<string, (SkillForm Form, bool IsPreferred)>> exact)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;
        var key = label.Trim();
        // The CV fast-path consumers (ResolveConceptIds/ResolveDetailed) read only
        // ConceptId + PreferredLabel, so this form's Lexemes are not read on that path.
        // We still build them faithfully (never []) so a SkillForm stays a CONSISTENT
        // value object regardless of which path constructed it (the lexeme-bag path
        // builds the same lexemes) — avoiding a construction-path asymmetry. The lazy
        // one-time build cost is deliberately accepted (BuildIndex is not on any
        // ADR 0045 hot-path budget).
        var lexemes = _analyzer.ToLexemes(label, TextLanguage.Swedish).ToHashSet(StringComparer.Ordinal);
        var form = new SkillForm(concept.ConceptId, concept.PreferredLabel, key, lexemes);
        if (!exact.TryGetValue(key, out var byConcept))
            exact[key] = byConcept =
                new Dictionary<string, (SkillForm Form, bool IsPreferred)>(StringComparer.Ordinal);
        if (!byConcept.TryGetValue(concept.ConceptId, out var existing) || (isPreferred && !existing.IsPreferred))
            byConcept[concept.ConceptId] = (form, isPreferred);
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

    private sealed record SkillIndex(
        IReadOnlyDictionary<string, List<SkillForm>> ByAnchor,
        IReadOnlyList<SkillSearchEntry> SearchEntries,
        IReadOnlyDictionary<string, string> LabelByConceptId,
        // #253 — exact-label fast-path: trimmed literal label/synonym (case-insensitive)
        // → the concepts carrying it verbatim, preferred-first then ConceptId Ordinal.
        IReadOnlyDictionary<string, IReadOnlyList<SkillForm>> ExactByLabel);

    // ADR 0079 STEG 3 PR-C — one substring-searchable label form (lower-cased) → its
    // concept-id + canonical preferred label (so a synonym hit still displays the
    // canonical label on the chip).
    private sealed record SkillSearchEntry(string Normalized, string ConceptId, string PreferredLabel);
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
