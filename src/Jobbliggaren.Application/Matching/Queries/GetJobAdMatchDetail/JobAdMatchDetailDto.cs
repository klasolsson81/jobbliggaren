using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;

/// <summary>
/// Read-projection for the single-ad match detail in the job modal (F4-16, ADR 0076
/// Amendment (b) §5; ADR 0053 Beslut 5 amendment). Category-primary, explainable by design:
/// the named <see cref="MatchGrade"/> (nullable — <c>null</c> when the ad earns no positive
/// tag, e.g. the user's stated occupation does not match this ad) plus a per-dimension row
/// for each of the seven match dimensions, each carrying its verdict + matched/missing
/// evidence (the "what you have / what the ad wants" the modal renders). Three row types,
/// one per evidence PROVENANCE: display text this layer already has
/// (<see cref="MatchDimensionDetailDto"/>), a coded common noun the client's catalogue names
/// (<see cref="MatchCodedDimensionDetailDto"/>), and register data this layer names but may
/// fail to (<see cref="MatchRegisterDimensionDetailDto"/>).
/// <para>
/// <b>NO opaque total (Goodhart guard — ADR 0076 Decision 4 / ADR 0071 / CLAUDE.md §5;
/// ADR 0053 Beslut 5 forbids the percentage ring):</b> there is intentionally NO
/// numeric/percentage/sort-key field anywhere on this DTO or its rows. The grade is a
/// bounded named category; the rows are verdict + string evidence. An architecture test
/// pins this shape so a number can never leak onto the modal wire.
/// </para>
/// </summary>
/// <param name="Grade">The named match grade for this ad given the current user's profile,
/// or <c>null</c> when the ad earns no positive tag (occupation/SSYK not a Match — the gate).
/// The modal renders the breakdown either way.</param>
/// <param name="SsykOverlap">The occupation-group dimension row.</param>
/// <param name="TitleSimilarity">The title dimension row (NotAssessed on the preference path
/// — no CV title is read in F4-16; LatestRole→title is a forward-note, not this STEG).</param>
/// <param name="RegionFit">The region dimension row.</param>
/// <param name="EmploymentFit">The employment-type dimension row.</param>
/// <param name="SkillOverlap">The CV-skill ∩ ad-skill coverage row (drives the golden grade).</param>
/// <param name="MustHaveCoverage">The ad's <c>must_have</c> requirement coverage row.</param>
/// <param name="NiceToHaveCoverage">The ad's <c>nice_to_have</c> requirement coverage row.</param>
public sealed record JobAdMatchDetailDto(
    MatchGrade? Grade,
    MatchRegisterDimensionDetailDto SsykOverlap,
    MatchDimensionDetailDto TitleSimilarity,
    MatchRegisterDimensionDetailDto RegionFit,
    MatchCodedDimensionDetailDto EmploymentFit,
    MatchDimensionDetailDto SkillOverlap,
    MatchDimensionDetailDto MustHaveCoverage,
    MatchDimensionDetailDto NiceToHaveCoverage);

/// <summary>
/// One dimension's modal row: its <see cref="MatchDimensionVerdict"/> plus the
/// matched/missing evidence strings (Display labels / shared title lexemes — never raw CV
/// text, never an opaque number). <see cref="Matched"/> = the overlap (what you have for
/// this ad); <see cref="Missing"/> = what the ad wants that the CV lacks (the civic-useful
/// direction). A 1:1 wire mirror of the Application-side <see cref="MatchDimension"/>, minus
/// nothing and plus nothing — there is deliberately no numeric score on a row (Goodhart).
/// </summary>
public sealed record MatchDimensionDetailDto(
    MatchDimensionVerdict Verdict,
    IReadOnlyList<string> Matched,
    IReadOnlyList<string> Missing);

/// <summary>
/// The same row for a dimension whose evidence is CODED rather than named: employment type
/// (klass 2). It carries concept ids, and the client resolves each to locale copy.
/// </summary>
/// <remarks>
/// <para>
/// A separate type rather than concept ids inside <see cref="MatchDimensionDetailDto"/>,
/// whose <see cref="MatchDimensionDetailDto.Matched"/> is documented as display labels. Six
/// of the seven dimensions really do carry display text; letting one of them mean something
/// else would make that type lie, and would push per-property knowledge onto the client that
/// this layer owns (CTO 2026-08-28).
/// </para>
/// <para>
/// Employment type is the only coded dimension: <c>SsykOverlap</c> and <c>RegionFit</c> name
/// occupation groups and regions, which are proper-noun register data and stay Swedish in
/// every locale (#1430). Employment type is a common noun and does not (#1537). The register
/// pair carries its own type (<see cref="MatchRegisterDimensionDetailDto"/>) because its
/// naming can FAIL, which neither of the other two can.
/// </para>
/// </remarks>
public sealed record MatchCodedDimensionDetailDto(
    MatchDimensionVerdict Verdict,
    IReadOnlyList<string> MatchedConceptIds,
    IReadOnlyList<string> MissingConceptIds);

/// <summary>
/// The same row for the two dimensions whose evidence is REGISTER data this layer names from
/// the taxonomy snapshot: occupation group (<c>SsykOverlap</c>) and region (<c>RegionFit</c>).
/// Each entry keeps its concept id beside the name, so an id the snapshot cannot name stays
/// COUNTED instead of vanishing.
/// </summary>
/// <remarks>
/// <para>
/// A third row type rather than a widened <see cref="MatchDimensionDetailDto"/> (#1598): four
/// of the seven dimensions carry no concept id at all — <c>TitleSimilarity</c> carries Snowball
/// stems, and the three CV dimensions carry Display labels — so widening that type would force
/// them to hold a null or synthetic id. It is the same argument that made
/// <see cref="MatchCodedDimensionDetailDto"/> its own type (CTO 2026-08-28), applied to a third
/// evidence provenance.
/// </para>
/// <para>
/// <b>Why not two parallel lists (names + ids):</b> their correspondence would be positional
/// and unenforceable — nothing in the type would stop the lists from differing in length. That
/// is precisely the defect class #1597 introduced in <c>MapLabels</c>, and pairing the id with
/// its own name makes it unrepresentable rather than merely unlikely.
/// </para>
/// <para>
/// <b>The invariant:</b> ONE entry per incoming concept id, in the scorer's order, never
/// fewer. The client's "the ad specifies nothing here" branch reads emptiness, so a filtered
/// list would make it claim silence about an ad that spoke — the defect this type exists to
/// close. Pinned in <c>GetJobAdMatchDetailQueryHandlerTests</c>.
/// </para>
/// </remarks>
public sealed record MatchRegisterDimensionDetailDto(
    MatchDimensionVerdict Verdict,
    IReadOnlyList<MatchRegisterConceptDto> Matched,
    IReadOnlyList<MatchRegisterConceptDto> Missing);

/// <summary>
/// One register concept on a match-detail row: the taxonomy concept id, plus the name the
/// snapshot gave it — <c>null</c> when it has no row in the snapshot at all.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ConceptId"/> is the entry's IDENTITY and is never rendered.</b> It rides the
/// wire so an unnamed entry can still EXIST and be counted; the client says how many such
/// entries a row has and stops there. Interpolating it into copy would put the external
/// system's ubiquitous language in front of the user, which is the one thing the ACL exists to
/// prevent (§5, ADR 0043) — and unlike the toolbar's chip, nothing here lets the reader act on
/// the id. It cannot be looked up on the client either: a name is absent exactly when the
/// concept has no row in the snapshot, and the picker tree the client reads is built from that
/// same list (<c>TaxonomyReadModel.LoadAsync</c>), so it is missing there too.
/// </para>
/// Absence IS the signal, mirroring
/// <see cref="Application.JobAds.Queries.GetTaxonomyTree.TaxonomyLabelDto"/>, whose doctrine
/// this shares. A separate type rather than a reuse of that one: identical shape, different
/// change-reason — the port's row answers a lookup, this one is a piece of rendered evidence
/// (DRY is a unit of knowledge, not of code shape; the house decided that once already for the
/// municipality/employment-type pair, <c>TaxonomyTreeDto</c>).
/// </remarks>
public sealed record MatchRegisterConceptDto(string ConceptId, string? Label);
