using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// Fas 4 STEG 3 (F4-3, ADR 0040 amendment + ADR 0074, senior-cto-advisor
/// Decision 1–6) — deterministic SSYK level-4 derivation (Variant V2). A free-text
/// occupational title is matched against the JobTech occupation-<b>name</b> labels
/// (via <see cref="ITaxonomyReadModel.GetTreeAsync"/> — reusing the taxonomy ACL,
/// ADR 0043) in two passes:
/// <list type="number">
/// <item>exact normalized (OrdinalIgnoreCase, NO diacritic folding — Decision 4);</item>
/// <item>stemmed token-overlap via the F4-2 NLP tier
/// (<see cref="ITextAnalyzer.ToLexemes"/>, Snowball — <c>to_tsvector('swedish')</c>
/// parity), gated so the overlap must rest on at least one DISTINCTIVE
/// occupation-name token (one whose occupation-names stay within fewer than
/// <see cref="MaxGroupSpread"/> distinct ssyk-4 groups) — a stemmed match grounded
/// solely on a scattered word (the bare "operatör", whose occupation-names fan out
/// across 7 unrelated groups) is low-precision cross-group fan-out and is dropped
/// (Klas 2026-06-21; senior-cto-advisor Variant C). Exact hits are never gated.</item>
/// </list>
/// Each occupation-name hit is resolved up to its ssyk-level-4 yrkesgrupp via the
/// committed frozen map (<see cref="OccupationGroupMappingLoader"/>), deduplicated
/// per group (strongest evidence kept) and returned as a ranked candidate list:
/// exact before stemmed, then group-label Ordinal. The engine PROPOSES, the user
/// CONFIRMS (ADR 0040 Beslut 4); nothing is persisted; a title with no match →
/// empty list → manual selection.
/// <para>
/// Singleton with a lazy single-load cache mirroring <see cref="TaxonomyReadModel"/>:
/// the occupation-name exact-lookup index + precomputed label lexemes + the
/// group-label lookup are built once from the cached snapshot tree + the frozen
/// map. Bounded, immutable reference data → no per-call DB hit. The title is NEVER
/// logged (CLAUDE.md §5 / BUILD §13) — this type takes no <c>ILogger</c>.
/// </para>
/// </summary>
internal sealed class OccupationCodeDeriver(
    ITaxonomyReadModel taxonomy, ITextAnalyzer analyzer) : IOccupationCodeDeriver
{
    // Bounded candidate cap — a relevance/UX/DoS bound so a single common token
    // ("chef") cannot fan out across the whole ~400-group taxonomy. NOT a coverage
    // claim: a title with no precise match always degrades to manual selection
    // anyway, and exact hits (few) always survive (they rank first). Documented per
    // the no-silent-cap discipline.
    private const int MaxCandidates = 25;

    // Generic-token cutoff for the stemmed pass (Klas 2026-06-21; senior-cto-advisor
    // Variant C). An occupation-name lexeme whose matchable occupation-names fan out
    // across >= this many DISTINCT ssyk-4 groups is "generic": a stemmed overlap
    // grounded SOLELY on such tokens is low-precision cross-group fan-out and is
    // dropped. Group-spread measures the diagnosed harm (cross-group fan-out)
    // DIRECTLY — document-frequency only correlates with it, and on the real
    // lexeme-level corpus the DF axis has no empty band (operatör DF≈7, assistent≈5,
    // ingenjör/tekniker/lärare≈4 — a guess disguised as a threshold). Group-spread,
    // measured over the 2153 mapped occupation-name entries, does (live values
    // 2026-06-21): scattered/noise tokens span many groups (operatör 7, chef 8,
    // tekniker 5, assistent 5, ingenjör 4, projektledare 14, maskinoperatör 17) while
    // focused/signal tokens span few (systemutveckl 1, advokat 1, jurist 1, cnc 2,
    // sjuksköterska 2, utvecklare 2, lärare 3). The empty band sits between spread 3
    // (lärare, last distinctive) and spread 4 (ingenjör, first generic) — 4 cuts there.
    // GroupSpreadGate_AnchorsBracketTheThreshold (integration test) pins the anchors
    // around G against the live snapshot so a future bump fails loud, never silently
    // re-leaks. Build pattern mirrors SkillTaxonomyIndex (reuse — CLAUDE.md §9.1).
    // Explainable gate over a cited quantity ("token points to N unrelated
    // occupations"), not an opaque score (CLAUDE.md §5).
    // internal (not private) so the band-witness test reads the SAME value the gate
    // uses — no duplicated test constant to drift out of sync (InternalsVisibleTo
    // Api.IntegrationTests; code-reviewer + dotnet-architect Minor).
    internal const int MaxGroupSpread = 4;

    // Cached once on first use; a fault leaves it null so the next call retries
    // (parity with TaxonomyReadModel — no permanent fail-cache).
    private Task<DerivationCache>? _cached;

    public ValueTask<OccupationDerivationResult> DeriveAsync(
        string title, CancellationToken cancellationToken) =>
        DeriveManyAsync([title], cancellationToken);

    public async ValueTask<OccupationDerivationResult> DeriveManyAsync(
        IReadOnlyList<string> titles, CancellationToken cancellationToken)
    {
        // Echo the first non-blank title (parity with the single-title result shape).
        // Defensive: the port can be called directly. All-blank → no candidates, never throw.
        var echo = string.Empty;
        foreach (var t in titles)
        {
            if (!string.IsNullOrWhiteSpace(t))
            {
                echo = t.Trim();
                break;
            }
        }
        if (echo.Length == 0)
            return new OccupationDerivationResult(echo, []);

        var cache = await GetCacheAsync(cancellationToken);

        // Best evidence per ssyk-4 group id across ALL source titles (dedupe — many
        // occupation-names map to one group; exact beats stemmed, higher overlap beats lower).
        // SourceOrder = the lowest (highest-priority) source-title index that reached the group
        // → the caller's priority ordering (current education before work history; Klas
        // 2026-06-21) governs the display order WITHIN a match kind. Evidence strength
        // (kind/score) is unchanged — SourceOrder is a separate, explainable display dimension,
        // never an opaque relevance weight (CLAUDE.md §5).
        var best = new Dictionary<string, RawMatch>(StringComparer.Ordinal);

        for (var order = 0; order < titles.Count; order++)
        {
            var query = titles[order];
            if (string.IsNullOrWhiteSpace(query))
                continue;
            query = query.Trim();

            // Pass 1 — exact normalized occupation-name match (OrdinalIgnoreCase).
            if (cache.ByExactLabel.TryGetValue(query, out var exactEntries))
            {
                foreach (var entry in exactEntries)
                    Consider(best, new RawMatch(
                        entry.GroupConceptId, entry.GroupLabel,
                        OccupationMatchKind.ExactOccupationName, entry.OccupationNameLabel,
                        Score: int.MaxValue, SourceOrder: order));
            }

            // Pass 2 — stemmed token-overlap (same Snowball stemmer as the FTS
            // search_vector). Title lexemes ∩ occupation-name-label lexemes.
            var titleLexemes = analyzer.ToLexemes(query, TextLanguage.Swedish)
                .ToHashSet(StringComparer.Ordinal);
            if (titleLexemes.Count > 0)
            {
                foreach (var entry in cache.AllEntries)
                {
                    // Group-spread gate (CTO Variant C): the overlap must rest on at
                    // least one DISTINCTIVE occupation-name token — one whose
                    // occupation-names stay within fewer than MaxGroupSpread distinct
                    // ssyk-4 groups. An overlap grounded only on a scattered token (the
                    // bare "operatör", whose occupation-names fan out across 7 unrelated
                    // groups) is low-precision cross-group fan-out → dropped before it can
                    // become a candidate. Exact Pass-1 hits above are never gated.
                    if (!HasDistinctiveSharedToken(
                            titleLexemes, entry.LabelLexemes, cache.GroupSpread))
                        continue;

                    var overlap = 0;
                    foreach (var lexeme in titleLexemes)
                    {
                        if (entry.LabelLexemes.Contains(lexeme))
                            overlap++;
                    }

                    if (overlap > 0)
                        Consider(best, new RawMatch(
                            entry.GroupConceptId, entry.GroupLabel,
                            OccupationMatchKind.StemmedTokenOverlap, entry.OccupationNameLabel,
                            Score: overlap, SourceOrder: order));
                }
            }
        }

        // Relevance governs SURVIVAL (kind → source-priority → overlap desc → label), then the
        // surviving set is DISPLAYED in the stable order (kind → source-priority → label
        // Ordinal) — deterministic. Note: source-priority outranks overlap, so under the
        // MaxCandidates cap a lower-overlap candidate from a higher-priority source (e.g. the
        // current education degree) is intentionally kept over a higher-overlap one from a
        // lower-priority source (the work history) — the desired career-changer trade-off.
        var candidates = best.Values
            .OrderBy(m => (int)m.MatchKind)
            .ThenBy(m => m.SourceOrder)
            .ThenByDescending(m => m.Score)
            .ThenBy(m => m.GroupLabel, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .OrderBy(m => (int)m.MatchKind)
            .ThenBy(m => m.SourceOrder)
            .ThenBy(m => m.GroupLabel, StringComparer.Ordinal)
            .Select(m => new OccupationCandidate(
                m.GroupConceptId, m.GroupLabel, m.MatchKind, m.MatchedOn))
            .ToList();

        return new OccupationDerivationResult(echo, candidates);
    }

    // Keep the strongest evidence per ssyk-4 group; always carry the lowest (highest-priority)
    // SourceOrder seen for the group, independent of which evidence wins.
    private static void Consider(Dictionary<string, RawMatch> best, RawMatch match)
    {
        if (!best.TryGetValue(match.GroupConceptId, out var existing))
        {
            best[match.GroupConceptId] = match;
            return;
        }

        var minOrder = Math.Min(match.SourceOrder, existing.SourceOrder);
        var winner = IsStronger(match, existing) ? match : existing;
        best[match.GroupConceptId] = winner with { SourceOrder = minOrder };
    }

    // Exact beats stemmed; within a kind, higher overlap wins; deterministic
    // tiebreak on the cited occupation-name label (Ordinal).
    private static bool IsStronger(RawMatch candidate, RawMatch current)
    {
        if (candidate.MatchKind != current.MatchKind)
            return candidate.MatchKind < current.MatchKind; // Exact (0) < Stemmed (1)
        if (candidate.Score != current.Score)
            return candidate.Score > current.Score;
        return string.CompareOrdinal(candidate.MatchedOn, current.MatchedOn) < 0;
    }

    // The stemmed-pass survival predicate (CTO Variant C, G = MaxGroupSpread): a
    // stemmed overlap survives iff the title shares at least one occupation-name
    // lexeme that is DISTINCTIVE — its occupation-names stay within fewer than
    // MaxGroupSpread distinct ssyk-4 groups. The dropped reason is fully derivable
    // from this shape: an overlap with no distinctive shared token rests entirely on
    // scattered tokens (the bare "operatör", whose occupation-names fan out across
    // 7 unrelated groups) and is excluded as low-precision cross-group fan-out.
    // Explainable boolean gate over a cited quantity ("token points to N unrelated
    // occupations") — never an opaque score (CLAUDE.md §5; ADR 0040/0074 transparency
    // invariant).
    private static bool HasDistinctiveSharedToken(
        IReadOnlySet<string> titleLexemes,
        IReadOnlySet<string> labelLexemes,
        IReadOnlyDictionary<string, int> groupSpread)
    {
        foreach (var lexeme in titleLexemes)
        {
            if (labelLexemes.Contains(lexeme)
                && groupSpread.GetValueOrDefault(lexeme) < MaxGroupSpread)
            {
                return true;
            }
        }
        return false;
    }

    private async ValueTask<DerivationCache> GetCacheAsync(CancellationToken ct)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is { IsCompletedSuccessfully: true })
            return cached.Result;

        // Await BEFORE publishing: a fault throws here and leaves _cached
        // unpublished → next call retries. A rare concurrent cold-start may build
        // twice (idempotent in-memory projection); last write wins (parity with
        // TaxonomyReadModel.GetStateAsync).
        var task = BuildAsync(ct);
        var cache = await task;
        Volatile.Write(ref _cached, task);
        return cache;
    }

    // Test-only seam (InternalsVisibleTo Api.IntegrationTests): the frozen per-lexeme
    // group-spread the gate reads. Lets the band-witness test pin the anchors around
    // MaxGroupSpread against the LIVE seeded snapshot (no hardcoded counts) so a
    // snapshot bump that moved a token across G fails loud rather than silently
    // re-leaking. Production callers use DeriveManyAsync only.
    internal async ValueTask<IReadOnlyDictionary<string, int>> GetGroupSpreadAsync(
        CancellationToken cancellationToken) =>
        (await GetCacheAsync(cancellationToken)).GroupSpread;

    private async Task<DerivationCache> BuildAsync(CancellationToken ct)
    {
        var tree = await taxonomy.GetTreeAsync(ct);
        var groupToSsyk = OccupationGroupMappingLoader.Load();

        // ssyk-4 group id → label (from the tree's occupation-groups). If one
        // concept-id ever surfaces under more than one occupation field, pick the
        // Ordinal-minimum label deterministically — NEVER an enumeration-order-
        // dependent First(): this deriver's whole contract is reproducibility
        // (class docstring; ADR 0040/0071), and the rest of the file already
        // Ordinal-tie-breaks (the candidate ThenBy on GroupLabel). Today the
        // snapshot carries one label per group, so this is a defensive pin, not a
        // visible change — it forecloses a silent drift if a future snapshot ever
        // lists a group twice with diverging labels.
        var groupLabelById = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups)
            .GroupBy(g => g.ConceptId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Label, StringComparer.Ordinal).First().Label,
                StringComparer.Ordinal);

        var entries = new List<NameEntry>();
        foreach (var occupation in tree.OccupationFields.SelectMany(f => f.Occupations))
        {
            // V2: an occupation-name is matchable only if the frozen map resolves
            // it to a ssyk-4 group that exists in the snapshot. Occupation-names
            // absent from the map degrade to manual selection — the F4-3
            // partial-coverage rule (ADR 0040 amendment). Live-verified 2026-06-14:
            // 2153 of the 2323 snapshot occupation-names map; 170 are unmapped (the
            // frozen map's 2179 entries include ids not in this v30 snapshot —
            // benign taxonomy drift; 0 dangling group targets).
            if (!groupToSsyk.TryGetValue(occupation.ConceptId, out var groupId)
                || !groupLabelById.TryGetValue(groupId, out var groupLabel))
            {
                continue;
            }

            var lexemes = analyzer.ToLexemes(occupation.Label, TextLanguage.Swedish)
                .ToHashSet(StringComparer.Ordinal);

            entries.Add(new NameEntry(occupation.Label, lexemes, groupId, groupLabel));
        }

        var byExactLabel = entries
            .GroupBy(e => e.OccupationNameLabel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<NameEntry>)g.ToList(),
                StringComparer.OrdinalIgnoreCase);

        // Group-spread per occupation-name lexeme: the number of DISTINCT ssyk-4
        // groups the lexeme's occupation-names fan out to. Built in one pass over the
        // same bounded entries (same build-pattern as SkillTaxonomyIndex.BuildIndex's
        // df map, value type HashSet<groupId> instead of int), then projected to the
        // group COUNT and frozen on the cache. Drives the stemmed-pass generic-token
        // gate — a high-spread lexeme ("operatör" → 7 groups) is generic, a low-spread
        // one ("systemutveckl" → 1 group) is distinctive.
        var groupSpreadSets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var lexeme in entry.LabelLexemes)
            {
                if (!groupSpreadSets.TryGetValue(lexeme, out var groups))
                    groupSpreadSets[lexeme] = groups = new HashSet<string>(StringComparer.Ordinal);
                groups.Add(entry.GroupConceptId);
            }
        }
        var groupSpread = groupSpreadSets.ToDictionary(
            kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);

        return new DerivationCache(byExactLabel, entries, groupSpread);
    }

    // One matchable occupation-name: its label (exact lookup + cited evidence), the
    // precomputed Snowball lexemes (stemmed pass), and the ssyk-4 group it resolves
    // to (id + label) via the frozen map.
    private sealed record NameEntry(
        string OccupationNameLabel,
        IReadOnlySet<string> LabelLexemes,
        string GroupConceptId,
        string GroupLabel);

    private sealed record DerivationCache(
        IReadOnlyDictionary<string, IReadOnlyList<NameEntry>> ByExactLabel,
        IReadOnlyList<NameEntry> AllEntries,
        // Occupation-name lexeme → group-spread (how many distinct ssyk-4 groups its
        // occupation-names fan out to). Frozen reference data — the stemmed pass reads
        // it to drop overlaps grounded only on scattered (high-spread) tokens.
        IReadOnlyDictionary<string, int> GroupSpread);

    private readonly record struct RawMatch(
        string GroupConceptId,
        string GroupLabel,
        OccupationMatchKind MatchKind,
        string MatchedOn,
        int Score,
        int SourceOrder);
}
