using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;

/// <summary>
/// Read-projection for the page-scoped match-tag batch-overlay on the /jobb list
/// (F4-13, ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 Decision C = C2a). The
/// per-user match overlay is delivered via this dedicated batch port — parity
/// <c>isSaved</c>/<c>isApplied</c> (ADR 0063). <c>JobAdDto</c> / <c>IJobAdSearchQuery</c>
/// stay UNTOUCHED (public, anonymously cacheable — Decision 5).
/// <para>
/// <b>Category-primary, NO opaque total (Goodhart guard — ADR 0076 Decision 4 / ADR 0071
/// / CLAUDE.md §5; ADR 0053 Beslut 5 forbids the percentage ring):</b> there is
/// intentionally NO numeric/percentage/sort-key field. Each entry carries the named
/// ordinal <see cref="MatchGrade"/> (the card tag) plus the four per-dimension verdicts
/// (the explainability contract, forward-compat with the F4-16 modal). An architecture
/// test pins this shape so a number can never leak onto the wire.
/// </para>
/// </summary>
public sealed record JobAdMatchBatchDto(IReadOnlyDictionary<Guid, JobAdMatchEntryDto> Entries);

/// <summary>
/// One ad's match overlay. Present in <see cref="JobAdMatchBatchDto.Entries"/> ONLY when
/// the ad earned a positive tag (occupation/SSYK Match — the gate). An ad that does not
/// qualify, or does not exist, is simply absent (the FE renders no chip). An ARCHIVED ad,
/// however, IS still tagged — <c>MatchScorer</c> has no status gate (known gap #864).
/// </summary>
/// <param name="Grade">The named match grade (the card's <c>.jp-matchchip</c> tag) —
/// a bounded category, never a number. F4-16 paints the golden rung: a Strong Fast match
/// with CV-skill overlap is graded <c>Top</c> ("Toppmatch", ADR 0076 Amendment (b) §1).</param>
/// <param name="SsykOverlap">The occupation-group dimension verdict (always Match for a
/// present entry — the gate).</param>
/// <param name="TitleSimilarity">The title dimension verdict (always NotAssessed on the
/// preference path — no CV title; carried for honest forward-compat).</param>
/// <param name="RegionFit">The region dimension verdict.</param>
/// <param name="EmploymentFit">The employment-type dimension verdict.</param>
/// <param name="SkillOverlap">F4-15 — the CV-skill ∩ ad-skill coverage verdict (from
/// the FULL score over the complete CV skills). Carried for the F4-16 modal's
/// matched/missing display; the F4-15 chip does not yet render it.</param>
/// <param name="MustHaveCoverage">F4-15 — the ad's <c>must_have</c> requirement coverage
/// verdict. Forward-compat with the F4-16 modal.</param>
/// <param name="NiceToHaveCoverage">F4-15 — the ad's <c>nice_to_have</c> requirement
/// coverage verdict. Forward-compat with the F4-16 modal.</param>
public sealed record JobAdMatchEntryDto(
    MatchGrade Grade,
    MatchDimensionVerdict SsykOverlap,
    MatchDimensionVerdict TitleSimilarity,
    MatchDimensionVerdict RegionFit,
    MatchDimensionVerdict EmploymentFit,
    MatchDimensionVerdict SkillOverlap,
    MatchDimensionVerdict MustHaveCoverage,
    MatchDimensionVerdict NiceToHaveCoverage);
