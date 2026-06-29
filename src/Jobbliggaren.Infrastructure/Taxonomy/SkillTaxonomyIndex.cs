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

    // #277 — group ESCO + AF twin skill concept-ids that share ONE exact-label surface into a
    // single chip. A "surface group" is the set of concept-ids one exact-label surface
    // co-produces (the value list in ExactByLabel); the canonical id is the surface's already-
    // materialised element [0] (preferred-first, then ConceptId Ordinal — #253) and its label is
    // that element's PreferredLabel. This is NOT a pairwise twin rule: it groups strictly by
    // shared literal surface, so genuinely-distinct same-surface concepts each stay their own
    // group only if they DON'T share a surface — two concepts carrying the SAME literal (the
    // "C#" twins) are one group; "scala"/"oracle-kunskaper" group by whichever surface each
    // concept's preferred label is. NEVER drops a concept-id (every input id is in exactly one
    // output group). NO new asset parsing — reuses the lazy ExactByLabel / LabelByConceptId.

    /// <summary>
    /// (#277, saved/resolved chips): partition an input set of concept-ids by the
    /// exact-label surface they share. Two input ids are in the SAME group iff one appears in the
    /// other's preferred-label surface (LabelByConceptId → ExactByLabel) — a SYMMETRIC,
    /// order-independent relation, computed by union-find so the partition does not depend on input
    /// order (the C# twins collapse whether the ESCO or AF id is seen first). The canonical id of a
    /// component is the member that is the [0] (preferred-first) of the broadest surface linking the
    /// component — concretely the member with the most input-members on its preferred-label surface,
    /// tie-broken by being that surface's element [0] (preferred), then ConceptId Ordinal; its label
    /// is the canonical's PreferredLabel. An id with no co-resolving twin in the set, an unknown id,
    /// or an id whose preferred label is absent → a one-member group (its own label if known, else
    /// the bare id — never dropped). Every input id appears in EXACTLY one output group (guarded).
    /// Groups are returned in first-seen order of their members over the distinct input.
    /// </summary>
    public IReadOnlyList<SkillSurfaceGroup> GroupConceptIds(IEnumerable<string> conceptIds)
    {
        ArgumentNullException.ThrowIfNull(conceptIds);

        var index = _index.Value;

        // Distinct, blank-skipped, input order preserved — the universe every output id comes from.
        var inputOrder = new List<string>();
        var inputSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in conceptIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !inputSet.Add(id))
                continue;
            inputOrder.Add(id);
        }

        // Union-find over the distinct input. Link id ↔ every co-resolving id on id's OWN
        // preferred-label surface that is also in the input set (symmetric: if A's surface holds B
        // OR B's surface holds A, the union merges them either way).
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in inputOrder)
            parent[id] = id;

        foreach (var id in inputOrder)
        {
            foreach (var coId in SurfaceMembersInSet(index, id, inputSet))
                Union(parent, id, coId);
        }

        // Collect components in first-seen member order; pick a deterministic canonical per group.
        var componentMembers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var componentOrder = new List<string>();
        foreach (var id in inputOrder)
        {
            var root = Find(parent, id);
            if (!componentMembers.TryGetValue(root, out var list))
            {
                componentMembers[root] = list = [];
                componentOrder.Add(root);
            }

            list.Add(id);
        }

        var groups = new List<SkillSurfaceGroup>(componentOrder.Count);
        foreach (var root in componentOrder)
        {
            var members = componentMembers[root];
            var (canonicalId, canonicalLabel) = ChooseCanonical(index, members, inputSet);
            groups.Add(new SkillSurfaceGroup(canonicalId, canonicalLabel, members));
        }

        // Guard the no-drop/partition invariant (#277): every distinct input id is in exactly one
        // output group.
        var emitted = groups.SelectMany(g => g.MemberConceptIds).ToList();
        if (emitted.Count != inputSet.Count
            || !emitted.ToHashSet(StringComparer.Ordinal).SetEquals(inputSet))
        {
            throw new InvalidOperationException(
                "SkillTaxonomyIndex.GroupConceptIds violated the no-drop/partition invariant " +
                "(#277): every input concept-id must appear in exactly one output group.");
        }

        return groups;
    }

    // The input-set ids that co-resolve with <paramref name="id"/> on ITS OWN preferred-label
    // surface (excluding itself). Empty for an unknown id / an id whose preferred label is absent.
    private static IEnumerable<string> SurfaceMembersInSet(
        SkillIndex index, string id, HashSet<string> inputSet)
    {
        if (!index.LabelByConceptId.TryGetValue(id, out var preferredLabel)
            || !index.ExactByLabel.TryGetValue(preferredLabel, out var surface))
            yield break;

        foreach (var form in surface)
            if (!string.Equals(form.ConceptId, id, StringComparison.Ordinal)
                && inputSet.Contains(form.ConceptId))
                yield return form.ConceptId;
    }

    // Deterministic canonical for a component: the member whose preferred-label surface carries the
    // MOST input-members (the broadest/"bare" surface — e.g. ESCO "C#" over AF "C#,
    // programmeringsspråk"), tie-broken by being that surface's element [0] (preferred), then by
    // ConceptId Ordinal. Its label is the canonical's PreferredLabel (or the bare id if unknown).
    private static (string CanonicalId, string CanonicalLabel) ChooseCanonical(
        SkillIndex index, List<string> members, HashSet<string> inputSet)
    {
        string bestId = members[0];
        int bestBreadth = -1;
        bool bestIsSurfaceHead = false;
        foreach (var id in members)
        {
            var breadth = 0;
            var isSurfaceHead = false;
            if (index.LabelByConceptId.TryGetValue(id, out var preferredLabel)
                && index.ExactByLabel.TryGetValue(preferredLabel, out var surface))
            {
                breadth = surface.Count(f => inputSet.Contains(f.ConceptId));
                isSurfaceHead = surface.Count > 0
                    && string.Equals(surface[0].ConceptId, id, StringComparison.Ordinal);
            }

            var better = breadth > bestBreadth
                || (breadth == bestBreadth && isSurfaceHead && !bestIsSurfaceHead)
                || (breadth == bestBreadth && isSurfaceHead == bestIsSurfaceHead
                    && string.CompareOrdinal(id, bestId) < 0);
            if (better)
            {
                bestId = id;
                bestBreadth = breadth;
                bestIsSurfaceHead = isSurfaceHead;
            }
        }

        return (bestId, index.LabelByConceptId.GetValueOrDefault(bestId, bestId));
    }

    private static string Find(Dictionary<string, string> parent, string id)
    {
        var root = id;
        while (!string.Equals(parent[root], root, StringComparison.Ordinal))
            root = parent[root];
        // Path-compression for the next lookup.
        while (!string.Equals(parent[id], root, StringComparison.Ordinal))
        {
            var next = parent[id];
            parent[id] = root;
            id = next;
        }

        return root;
    }

    private static void Union(Dictionary<string, string> parent, string a, string b)
    {
        var ra = Find(parent, a);
        var rb = Find(parent, b);
        if (string.Equals(ra, rb, StringComparison.Ordinal))
            return;
        // Deterministic merge: the Ordinal-smaller root becomes the parent.
        if (string.CompareOrdinal(ra, rb) <= 0)
            parent[rb] = ra;
        else
            parent[ra] = rb;
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
    /// ADR 0079 STEG 3 — the CV-side resolver (F4-15): free text → the winning
    /// <see cref="SkillForm"/> per concept-id (carrying the preferred label) so the CV-side
    /// resolver can surface user-readable skill chips. Normalises <paramref name="freeText"/>
    /// to its Swedish lexeme set (the same pipeline the ad side uses) and matches the best
    /// form per concept-id (bag containment). Blank/empty input or no match → empty (never
    /// throws — an unresolvable CV skill is normal, not an error).
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
        // The CV fast-path consumers (ResolveForms/ResolveDetailed) read only
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

/// <summary>
/// #277 — one grouped skill surface: the canonical (preferred-first) concept-id + its label,
/// and ALL the member concept-ids that share the exact-label surface (a singleton carries one
/// member). The Infrastructure-internal result of <see cref="SkillTaxonomyIndex"/>'s grouping
/// helpers; the resolver maps it to the Application <c>ResolvedSkillGroup</c> port shape so the
/// member ids never leak the internal SkillForm. Non-PII taxonomy metadata.
/// </summary>
internal sealed record SkillSurfaceGroup(
    string CanonicalConceptId,
    string CanonicalLabel,
    IReadOnlyList<string> MemberConceptIds);
