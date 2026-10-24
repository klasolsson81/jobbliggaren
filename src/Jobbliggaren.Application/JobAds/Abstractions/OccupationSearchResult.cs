using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// The answer of <see cref="IOccupationSearcher"/> (#2198, ADR 0180): the rows in display order,
/// capped by the searcher, and the exact number of rows the query reached.
/// A Group row selects its group. A variant carries its own occupation-name concept id (#2199,
/// ADR 0180 Amendment 2026-10-24): /jobb filters on it, while Matchning and the onboarding rail
/// still select the variant's group (ADR 0040 Beslut 4).
/// </summary>
public sealed record OccupationSearchResult(IReadOnlyList<OccupationSearchRow> Rows, int TotalRows)
{
    public static OccupationSearchResult Empty { get; } = new([], 0);
}

/// <summary>What a row selects: one group, or nothing until one of its variants is chosen.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OccupationSearchRowKind>))]
public enum OccupationSearchRowKind
{
    /// <summary>Selects one ssyk-level-4 group; the occupation-names that reached it are evidence.</summary>
    Group,

    /// <summary>
    /// One head word whose matched occupation-names span several groups. It expands but cannot be
    /// selected; each variant names and selects its own group (ADR 0040 Beslut 4).
    /// </summary>
    Word,
}

/// <summary>
/// One result row. Built only through <see cref="ForGroup"/> and <see cref="ForWord"/>: the fields
/// of the other kind are null or empty, never absent, so the wire shape is the same for both.
/// </summary>
public sealed record OccupationSearchRow
{
    private OccupationSearchRow(
        OccupationSearchRowKind kind,
        string? groupConceptId,
        string? groupLabel,
        IReadOnlyList<string> matchedNames,
        bool labelHit,
        string? headWord,
        IReadOnlyList<OccupationSearchVariant> variants,
        bool exactMatch)
    {
        Kind = kind;
        GroupConceptId = groupConceptId;
        GroupLabel = groupLabel;
        MatchedNames = matchedNames;
        LabelHit = labelHit;
        HeadWord = headWord;
        Variants = variants;
        ExactMatch = exactMatch;
    }

    public OccupationSearchRowKind Kind { get; }

    /// <summary>The group a <see cref="OccupationSearchRowKind.Group"/> row selects; null on a Word row.</summary>
    public string? GroupConceptId { get; }

    public string? GroupLabel { get; }

    /// <summary>The occupation-names that reached the group, in display order; empty on a Word row.</summary>
    public IReadOnlyList<string> MatchedNames { get; }

    /// <summary>The group's own label matched the query.</summary>
    public bool LabelHit { get; }

    /// <summary>The head word in its source spelling; null on a Group row.</summary>
    public string? HeadWord { get; }

    /// <summary>The variants of a Word row, in display order; empty on a Group row.</summary>
    public IReadOnlyList<OccupationSearchVariant> Variants { get; }

    /// <summary>The row holds an occupation-name or a group label equal to the whole query.</summary>
    public bool ExactMatch { get; }

    public static OccupationSearchRow ForGroup(
        string groupConceptId,
        string groupLabel,
        IReadOnlyList<string> matchedNames,
        bool labelHit,
        bool exactMatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupConceptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupLabel);
        ArgumentNullException.ThrowIfNull(matchedNames);
        if (matchedNames.Count == 0 && !labelHit)
            throw new ArgumentException("A group row needs a matched occupation-name or a label hit.", nameof(matchedNames));

        return new OccupationSearchRow(
            OccupationSearchRowKind.Group, groupConceptId, groupLabel, matchedNames, labelHit, null, [], exactMatch);
    }

    public static OccupationSearchRow ForWord(
        string headWord,
        IReadOnlyList<OccupationSearchVariant> variants,
        bool exactMatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headWord);
        ArgumentNullException.ThrowIfNull(variants);
        if (variants.Count < 2
            || variants.Select(v => v.GroupConceptId).Distinct(StringComparer.Ordinal).Count() < 2)
        {
            throw new ArgumentException("A word row needs at least two variants in at least two groups.", nameof(variants));
        }

        return new OccupationSearchRow(
            OccupationSearchRowKind.Word, null, null, [], false, headWord, variants, exactMatch);
    }
}

/// <summary>One occupation-name under a Word row, with its own concept id and its group.</summary>
public sealed record OccupationSearchVariant(
    string OccupationNameConceptId, string NameLabel, string GroupConceptId, string GroupLabel);
