using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;

/// <summary>
/// Read-projection for the single-ad match detail in the job modal (F4-16, ADR 0076
/// Amendment (b) §5; ADR 0053 Beslut 5 amendment). Category-primary, explainable by design:
/// the named <see cref="MatchGrade"/> (nullable — <c>null</c> when the ad earns no positive
/// tag, e.g. the user's stated occupation does not match this ad) plus a per-dimension row
/// for each of the seven match dimensions, each carrying its verdict + matched/missing
/// evidence STRINGS (the "what you have / what the ad wants" the modal renders).
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
    MatchDimensionDetailDto SsykOverlap,
    MatchDimensionDetailDto TitleSimilarity,
    MatchDimensionDetailDto RegionFit,
    MatchDimensionDetailDto EmploymentFit,
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
