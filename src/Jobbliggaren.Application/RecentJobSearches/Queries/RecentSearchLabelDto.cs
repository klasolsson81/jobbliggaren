using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.RecentJobSearches.Queries;

/// <summary>
/// Which dimension names the recent search.
/// </summary>
/// <remarks>
/// Named explicitly, including <see cref="Query"/> — even though the client also receives
/// <c>Q</c> and could branch on it. The branch predicate is one knowledge piece; a second
/// copy of it in TypeScript is the drift this shape exists to prevent (CTO 2026-08-28).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RecentSearchLabelKind>))]
public enum RecentSearchLabelKind
{
    /// <summary>Free-text search. The single part carries the query verbatim.</summary>
    Query,

    /// <summary>
    /// The selection was exactly every group in ONE occupation field, so the field's own name
    /// says it — ADR 0067:208 rule (i), set-equality against the taxonomy tree.
    /// </summary>
    OccupationField,

    /// <summary>One or more taxonomy dimensions, related by <see cref="RecentSearchLabelDto.Join"/>.</summary>
    Dimensions,

    /// <summary>No dimension narrows the search.</summary>
    All,
}

/// <summary>
/// How the parts relate — the SEMANTICS of the join, never the joining word.
/// </summary>
/// <remarks>
/// The distinction is a truth about the search predicate, not a style choice, which is why it
/// is derived here rather than on the client. The geo axes are UNIONED
/// (<c>JobAdSearchComposition</c>, #551 PR-B D5: "kommun ∨ län ∨ remote"), so their parts are
/// alternatives. The refinement axes are orthogonal ANDs, so calling them alternatives would
/// state something false about what the click returns. Which word renders each is catalogue
/// copy and belongs to the locale.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RecentSearchLabelJoin>))]
public enum RecentSearchLabelJoin
{
    /// <summary>Fewer than two parts; nothing is joined.</summary>
    None,

    /// <summary>Union — each part is an alternative to the others.</summary>
    Disjunction,

    /// <summary>Orthogonal narrowing — every part holds at once.</summary>
    Conjunction,
}

/// <summary>What a single part of the label is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecentSearchLabelPartKind>))]
public enum RecentSearchLabelPartKind
{
    /// <summary>
    /// A resolved REGISTER name, carried in <see cref="RecentSearchLabelPartDto.Text"/>. Place
    /// names, region names and occupation-group names are proper-noun data and stay Swedish in
    /// every locale.
    /// </summary>
    /// <remarks>
    /// Not every taxonomy name is one of these — the klass 2 terms are common nouns and travel
    /// as <see cref="Coded"/> instead (#1537).
    /// </remarks>
    Named,

    /// <summary>
    /// The distance facet. It carries no <see cref="RecentSearchLabelPartDto.Text"/> because it
    /// is not taxonomy data — it is a word, and which word depends on the locale AND on the
    /// part's position (Swedish capitalises it only where it leads).
    /// </summary>
    Remote,

    /// <summary>
    /// The employer axis. Carries neither <see cref="RecentSearchLabelPartDto.Text"/> nor
    /// <see cref="RecentSearchLabelPartDto.ConceptId"/>: the value is an org.nr, and for an
    /// enskild firma that IS the holder's personnummer (#841), so the label never names it —
    /// Klas chose the value-free form (2026-08-23), delivered with #1471 once the replay carried
    /// the axis. <see cref="RecentSearchLabelPartDto.MoreCount"/> still counts the employers
    /// beyond the first, so the client can say how many without saying which.
    /// </summary>
    Employer,

    /// <summary>
    /// A taxonomy concept whose NAME is locale copy.
    /// It carries <see cref="RecentSearchLabelPartDto.ConceptId"/> and no
    /// <see cref="RecentSearchLabelPartDto.Text"/> — the mirror image of <see cref="Named"/>.
    /// </summary>
    /// <remarks>
    /// <c>Heltid</c>, <c>Vikariat</c> and <c>Behovsanställning</c> are common nouns, so shipping
    /// them resolved put Swedish in front of an English reader (#1537). Sending the code instead
    /// of the name is what makes a missing catalogue key impossible to satisfy silently in
    /// Swedish: there is no Swedish to fall back TO on this part.
    /// <para>
    /// A REGISTER concept the taxonomy snapshot could not resolve travels as this kind too
    /// (#1540). It is the same situation reached from the other side: the part has no name to
    /// carry, and the word that stands in for one is locale copy. What decides the kind is
    /// therefore whether a name is available, not which taxonomy class the concept belongs to.
    /// </para>
    /// </remarks>
    Coded,
}

/// <summary>
/// One part of the label: what names it, plus how many further selections it stands for.
/// </summary>
/// <param name="Kind">Which of <paramref name="Text"/> and <paramref name="ConceptId"/> is set.</param>
/// <param name="Text">
/// The resolved register label. Set exactly when <paramref name="Kind"/> is
/// <see cref="RecentSearchLabelPartKind.Named"/>.
/// </param>
/// <param name="ConceptId">
/// The taxonomy concept the client looks the name up by. Set exactly when
/// <paramref name="Kind"/> is <see cref="RecentSearchLabelPartKind.Coded"/>.
/// </param>
/// <param name="MoreCount">
/// Selections beyond the named one, <c>0</c> when the name covers them all. Counts the same
/// unit the name states — ADR 0067:208 rule (iii).
/// </param>
public sealed record RecentSearchLabelPartDto(
    RecentSearchLabelPartKind Kind,
    string? Text,
    string? ConceptId,
    int MoreCount);

/// <summary>
/// The recent search's display label as STRUCTURE, not prose.
/// </summary>
/// <remarks>
/// <para>
/// Which dimension names the row, and how its parts relate, are derived from the search
/// criteria and the taxonomy tree — a correctness concern, and one an architecture test
/// already measures (<c>GeoUnionLabelParityTests</c>: a geo term that never reaches
/// <c>DeriveOrtLabel</c> makes the label name a strict SUBSET of what the click returns).
/// That derivation stays here. The words that render it are locale copy and live in
/// <c>messages/{sv,en}/jobads.json</c>.
/// </para>
/// <para>
/// This replaced a pre-composed Swedish string (#1430). That string reached an English user
/// verbatim on three surfaces, worst inside an already-translated frame:
/// <c>"Remove the search: Göteborg eller distans"</c>.
/// </para>
/// </remarks>
public sealed record RecentSearchLabelDto(
    RecentSearchLabelKind Kind,
    RecentSearchLabelJoin Join,
    IReadOnlyList<RecentSearchLabelPartDto> Parts);
