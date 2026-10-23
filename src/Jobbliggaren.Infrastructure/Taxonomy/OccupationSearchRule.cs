using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// The occupation pickers' search rule (#2198, ADR 0180) as a pure function over the shared
/// <see cref="OccupationNameIndex"/>: <see cref="Prepare"/> once, then <see cref="Search"/> per query.
/// <list type="bullet">
/// <item>Text is NFC-normalised and lower-cased, then split on every character that is not a letter
/// or a digit. Diacritics are not folded (parity with the deriver's Decision 4).</item>
/// <item>A query token hits a label token at its start, or at its end when the query token is at
/// least <see cref="SuffixFloor"/> long: Swedish puts a compound's head last, so
/// <c>sjuksköterska</c> reaches <c>Anestesisjuksköterska</c>, while <c>ux</c> never reaches
/// <c>vuxenutbildning</c>. Every query token must hit (AND).</item>
/// <item>Matched occupation-names are grouped by head word. A head word whose matched names sit in
/// one group adds them to that group's row; one whose names span several groups becomes a Word row.
/// Group labels are matched by the same rule and merge into their group's row.</item>
/// <item>Rows order by tier (a whole-label match of a name, then of a group label, then the start
/// arm, then rows reached only through the end arm), then by Swedish collation, then Ordinal.</item>
/// </list>
/// The parameters are a picker's search rule, not CV-review rubric data (AGENTS.md §5); their
/// derivation is dated in ADR 0180.
/// </summary>
internal static class OccupationSearchRule
{
    /// <summary>The shortest query token the end arm accepts: 3 floods (<c>ker</c>), 5 loses <c>kock</c>.</summary>
    internal const int SuffixFloor = 4;

    /// <summary>A query needs at least one token this long; the acceptance words <c>it</c> and <c>UX</c> are two.</summary>
    internal const int MinQueryTokenLength = 2;

    internal const int MaxRows = 50;

    /// <summary>
    /// Removed from label tokens. A query drops them only when another token remains, so a lone
    /// <c>fl</c> still reaches <c>Florist</c>. Never <c>av</c>, which is the start of <c>AV-tekniker</c>.
    /// </summary>
    internal static readonly FrozenSet<string> LabelStopwords =
        new[] { "och", "inom", "samt", "m", "fl" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Swedish order (Å &lt; Ä &lt; Ö after Z). Needs ICU; invariant globalization fails loud here.</summary>
    internal static readonly StringComparer Collation =
        StringComparer.Create(CultureInfo.GetCultureInfo("sv-SE"), CompareOptions.None);

    internal static PreparedOccupationIndex Prepare(OccupationNameIndexContents contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var names = contents.Names
            .Select(n => new PreparedName(
                n,
                Normalize(n.NameLabel),
                LabelTokens(n.NameLabel),
                HeadWordSpelling(n.NameLabel)))
            .ToArray();
        var groups = contents.Groups
            .Select(g => new PreparedGroup(g, Normalize(g.Label), LabelTokens(g.Label)))
            .ToArray();

        return new PreparedOccupationIndex(names, groups);
    }

    internal static OccupationSearchResult Search(PreparedOccupationIndex index, string query)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);

        var normalizedQuery = Normalize(query);
        var queryTokens = QueryTokens(normalizedQuery);
        if (queryTokens.Length == 0 || queryTokens.Max(t => t.Length) < MinQueryTokenLength)
            return OccupationSearchResult.Empty;

        var byHead = new Dictionary<string, List<(PreparedName Name, Tier Tier)>>(StringComparer.Ordinal);
        foreach (var name in index.Names)
        {
            var tier = Match(normalizedQuery, queryTokens, name.Normalized, name.Tokens, Tier.ExactName);
            if (tier is null)
                continue;

            var key = name.HeadWord.ToLowerInvariant();
            if (!byHead.TryGetValue(key, out var hits))
                byHead[key] = hits = [];
            hits.Add((name, tier.Value));
        }

        var groupRows = new Dictionary<string, GroupAccumulator>(StringComparer.Ordinal);
        var wordRows = new List<WordAccumulator>();
        foreach (var (key, hits) in byHead)
        {
            var groupIds = hits.Select(h => h.Name.Entry.GroupConceptId).Distinct(StringComparer.Ordinal).ToList();
            if (groupIds.Count == 1)
            {
                var row = GroupRow(groupRows, hits[0].Name.Entry.GroupConceptId, hits[0].Name.Entry.GroupLabel);
                row.Names.AddRange(hits);
            }
            else
            {
                var spelling = hits.Select(h => h.Name.HeadWord).Order(StringComparer.Ordinal).First();
                wordRows.Add(new WordAccumulator(key, spelling, hits));
            }
        }

        foreach (var group in index.Groups)
        {
            var tier = Match(normalizedQuery, queryTokens, group.Normalized, group.Tokens, Tier.ExactGroupLabel);
            if (tier is not null)
                GroupRow(groupRows, group.Entry.ConceptId, group.Entry.Label).LabelTier = tier;
        }

        var rows = groupRows.Values.Select(ToRow)
            .Concat(wordRows.Select(ToRow))
            .OrderBy(r => r.Tier)
            .ThenBy(r => r.SortLabel, Collation)
            .ThenBy(r => r.SortLabel, StringComparer.Ordinal)
            .ThenBy(r => r.SortKey, StringComparer.Ordinal)
            .ToList();

        return new OccupationSearchResult(
            rows.Take(MaxRows).Select(r => r.Row).ToList(),
            rows.Count);
    }

    private static GroupAccumulator GroupRow(
        Dictionary<string, GroupAccumulator> rows, string groupConceptId, string groupLabel)
    {
        if (!rows.TryGetValue(groupConceptId, out var row))
            rows[groupConceptId] = row = new GroupAccumulator(groupConceptId, groupLabel);
        return row;
    }

    private static RankedRow ToRow(GroupAccumulator group)
    {
        var tiers = group.Names.Select(n => n.Tier).ToList();
        if (group.LabelTier is { } labelTier)
            tiers.Add(labelTier);

        var names = group.Names
            .OrderBy(n => n.Tier)
            .ThenBy(n => n.Name.Entry.NameLabel, Collation)
            .ThenBy(n => n.Name.Entry.NameLabel, StringComparer.Ordinal)
            .Select(n => n.Name.Entry.NameLabel)
            .ToList();

        var exact = tiers.Any(t => t is Tier.ExactName or Tier.ExactGroupLabel);
        var row = OccupationSearchRow.ForGroup(
            group.GroupConceptId, group.GroupLabel, names, group.LabelTier is not null, exact);

        return new RankedRow(row, tiers.Min(), group.GroupLabel, group.GroupConceptId);
    }

    private static RankedRow ToRow(WordAccumulator word)
    {
        var variants = word.Hits
            .OrderBy(h => h.Tier)
            .ThenBy(h => h.Name.Entry.NameLabel, Collation)
            .ThenBy(h => h.Name.Entry.NameLabel, StringComparer.Ordinal)
            .Select(h => new OccupationSearchVariant(
                h.Name.Entry.NameLabel, h.Name.Entry.GroupConceptId, h.Name.Entry.GroupLabel))
            .ToList();

        var tier = word.Hits.Min(h => h.Tier);
        var row = OccupationSearchRow.ForWord(word.Spelling, variants, tier == Tier.ExactName);

        return new RankedRow(row, tier, word.Spelling, word.Key);
    }

    // The tier a label reaches for the query, or null when some query token hits none of its tokens.
    private static Tier? Match(
        string normalizedQuery, string[] queryTokens, string normalizedLabel, string[] labelTokens, Tier exactTier)
    {
        if (string.Equals(normalizedQuery, normalizedLabel, StringComparison.Ordinal))
            return exactTier;

        var tier = Tier.Prefix;
        foreach (var queryToken in queryTokens)
        {
            var hit = TokenHit(queryToken, labelTokens);
            if (hit is null)
                return null;
            if (hit == Tier.Suffix)
                tier = Tier.Suffix;
        }
        return tier;
    }

    private static Tier? TokenHit(string queryToken, string[] labelTokens)
    {
        var suffix = false;
        foreach (var labelToken in labelTokens)
        {
            if (labelToken.StartsWith(queryToken, StringComparison.Ordinal))
                return Tier.Prefix;
            if (queryToken.Length >= SuffixFloor && labelToken.EndsWith(queryToken, StringComparison.Ordinal))
                suffix = true;
        }
        return suffix ? Tier.Suffix : null;
    }

    private static string[] QueryTokens(string normalizedQuery)
    {
        var tokens = Tokenize(normalizedQuery);
        var kept = tokens.Where(t => !LabelStopwords.Contains(t)).ToArray();
        return kept.Length > 0 ? kept : tokens;
    }

    private static string[] LabelTokens(string label) =>
        Tokenize(Normalize(label)).Where(t => !LabelStopwords.Contains(t)).ToArray();

    // NFC, lower case, whitespace collapsed: the picker's "whole label" comparison. The deriver's
    // exact pass only trims, on purpose (ADR 0180).
    private static string Normalize(string text) =>
        string.Join(' ', text.Normalize(NormalizationForm.FormC).ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string[] Tokenize(string normalized)
    {
        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i <= normalized.Length; i++)
        {
            var inToken = i < normalized.Length && char.IsLetterOrDigit(normalized[i]);
            if (inToken && start < 0)
            {
                start = i;
            }
            else if (!inToken && start >= 0)
            {
                tokens.Add(normalized[start..i]);
                start = -1;
            }
        }
        return [.. tokens];
    }

    // The label's first run of letters, digits and hyphens, in its source spelling: the issue's head
    // word, which keeps the hyphen (st-läkare) so it groups variants the way users read them.
    private static string HeadWordSpelling(string label)
    {
        var text = label.Normalize(NormalizationForm.FormC);
        var start = 0;
        while (start < text.Length && !IsHeadWordChar(text[start]))
            start++;
        var end = start;
        while (end < text.Length && IsHeadWordChar(text[end]))
            end++;
        return end > start ? text[start..end] : text;
    }

    private static bool IsHeadWordChar(char c) => char.IsLetterOrDigit(c) || c == '-';

    private enum Tier
    {
        ExactName = 0,
        ExactGroupLabel = 1,
        Prefix = 2,
        Suffix = 3,
    }

    private sealed class GroupAccumulator(string groupConceptId, string groupLabel)
    {
        public string GroupConceptId { get; } = groupConceptId;
        public string GroupLabel { get; } = groupLabel;
        public List<(PreparedName Name, Tier Tier)> Names { get; } = [];
        public Tier? LabelTier { get; set; }
    }

    private sealed record WordAccumulator(string Key, string Spelling, List<(PreparedName Name, Tier Tier)> Hits);

    private sealed record RankedRow(OccupationSearchRow Row, Tier Tier, string SortLabel, string SortKey);
}

/// <summary>The index with every label normalised and tokenised once.</summary>
internal sealed record PreparedOccupationIndex(
    IReadOnlyList<PreparedName> Names,
    IReadOnlyList<PreparedGroup> Groups);

internal sealed record PreparedName(OccupationNameEntry Entry, string Normalized, string[] Tokens, string HeadWord);

internal sealed record PreparedGroup(OccupationGroupEntry Entry, string Normalized, string[] Tokens);
