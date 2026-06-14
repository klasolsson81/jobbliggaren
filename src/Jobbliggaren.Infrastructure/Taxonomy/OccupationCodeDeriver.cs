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
/// parity).</item>
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

    // Cached once on first use; a fault leaves it null so the next call retries
    // (parity with TaxonomyReadModel — no permanent fail-cache).
    private Task<DerivationCache>? _cached;

    public async ValueTask<OccupationDerivationResult> DeriveAsync(
        string title, CancellationToken cancellationToken)
    {
        // Defensive: the port can be called directly (the query validator guards
        // the Mediator path). Blank → no candidates, never throw.
        if (string.IsNullOrWhiteSpace(title))
            return new OccupationDerivationResult(title, []);

        var cache = await GetCacheAsync(cancellationToken);
        var query = title.Trim();

        // Best evidence per ssyk-4 group id (dedupe — many occupation-names map to
        // one group; exact beats stemmed, higher overlap beats lower).
        var best = new Dictionary<string, RawMatch>(StringComparer.Ordinal);

        // Pass 1 — exact normalized occupation-name match (OrdinalIgnoreCase).
        if (cache.ByExactLabel.TryGetValue(query, out var exactEntries))
        {
            foreach (var entry in exactEntries)
                Consider(best, new RawMatch(
                    entry.GroupConceptId, entry.GroupLabel,
                    OccupationMatchKind.ExactOccupationName, entry.OccupationNameLabel,
                    Score: int.MaxValue));
        }

        // Pass 2 — stemmed token-overlap (same Snowball stemmer as the FTS
        // search_vector). Title lexemes ∩ occupation-name-label lexemes.
        var titleLexemes = analyzer.ToLexemes(query, TextLanguage.Swedish)
            .ToHashSet(StringComparer.Ordinal);
        if (titleLexemes.Count > 0)
        {
            foreach (var entry in cache.AllEntries)
            {
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
                        Score: overlap));
            }
        }

        // Relevance governs SURVIVAL (kind: exact first → overlap desc → label),
        // then the surviving set is DISPLAYED in the stable order (kind → label
        // Ordinal) — deterministic, parity with TaxonomyReadModel's
        // OrderBy(Kind).ThenBy(Label, Ordinal).
        var candidates = best.Values
            .OrderBy(m => (int)m.MatchKind)
            .ThenByDescending(m => m.Score)
            .ThenBy(m => m.GroupLabel, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .OrderBy(m => (int)m.MatchKind)
            .ThenBy(m => m.GroupLabel, StringComparer.Ordinal)
            .Select(m => new OccupationCandidate(
                m.GroupConceptId, m.GroupLabel, m.MatchKind, m.MatchedOn))
            .ToList();

        return new OccupationDerivationResult(title, candidates);
    }

    // Keep the strongest evidence per ssyk-4 group.
    private static void Consider(Dictionary<string, RawMatch> best, RawMatch match)
    {
        if (!best.TryGetValue(match.GroupConceptId, out var existing)
            || IsStronger(match, existing))
        {
            best[match.GroupConceptId] = match;
        }
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

    private async Task<DerivationCache> BuildAsync(CancellationToken ct)
    {
        var tree = await taxonomy.GetTreeAsync(ct);
        var groupToSsyk = OccupationGroupMappingLoader.Load();

        // ssyk-4 group id → label (from the tree's occupation-groups).
        var groupLabelById = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups)
            .GroupBy(g => g.ConceptId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);

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

        return new DerivationCache(byExactLabel, entries);
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
        IReadOnlyList<NameEntry> AllEntries);

    private readonly record struct RawMatch(
        string GroupConceptId,
        string GroupLabel,
        OccupationMatchKind MatchKind,
        string MatchedOn,
        int Score);
}
