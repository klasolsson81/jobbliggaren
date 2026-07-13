using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// Fas 4 STEG 5 (F4-5, ADR 0074 row U5a; BUILD §8.2/§8.5) — the deterministic
/// "Fast mode" match scorer. NO AI/LLM (ADR 0071, CLAUDE.md §5): it scores ONE job
/// ad against a caller-supplied <see cref="CandidateMatchProfile"/> over three
/// thin-vertical dimensions —
/// <list type="number">
/// <item>SSYK level-4 overlap (the ad's <c>occupation_group_concept_id</c> shadow
/// vs the CV's confirmed ssyk-4 ids, F4-3);</item>
/// <item>title similarity (stemmed lexeme overlap via <see cref="ITextAnalyzer"/>,
/// F4-2 Snowball — <c>to_tsvector('swedish')</c> parity);</item>
/// <item>region + employment-type fit (the ad's <c>region_concept_id</c> /
/// <c>employment_type_concept_id</c> shadows vs the CV's preferred ids).</item>
/// </list>
/// It does NOT consume F4-4/F4-4b keyword/skill/requirement extraction — that is the
/// full match (F4-6). Explainable by design: each dimension surfaces matched/missing
/// evidence; the result is category-primary with no opaque total (Goodhart guard).
/// <para>
/// The scorer reads only the ad's public title + the STORED shadow columns and the
/// confirmed/preferred ids on the profile — no raw CV PII (parity
/// <see cref="IOccupationCodeDeriver"/>); it never logs. Implemented in
/// Infrastructure (shadow-column reads via <c>EF.Property</c> are Npgsql-bound,
/// ADR 0062 / CLAUDE.md §2.1) — the port surface stays BCL + Domain-id only.
/// </para>
/// </summary>
public interface IMatchScorer
{
    /// <summary>
    /// Scores the job ad <paramref name="jobAdId"/> against
    /// <paramref name="profile"/>. Throws
    /// <see cref="Common.Exceptions.NotFoundException"/> if the ad does not exist.
    /// A dimension whose CV-side input is empty, or whose ad-side value is absent,
    /// reports <see cref="MatchDimensionVerdict.NotAssessed"/> (never
    /// <see cref="MatchDimensionVerdict.NoMatch"/>). Deterministic: equal inputs
    /// yield an equal score, with Ordinal-stable matched/missing evidence.
    /// </summary>
    ValueTask<MatchScore> ScoreAsync(
        JobAdId jobAdId, CandidateMatchProfile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Fas 4 STEG 13 (F4-13, ADR 0076 Decision 5; senior-cto-advisor 2026-06-19
    /// Decision A = A1) — scores MANY job ads against the SAME
    /// <paramref name="profile"/> in ONE database round-trip (the page-scoped match-tag
    /// batch-overlay: parity <c>isSaved</c>/<c>isApplied</c>, ADR 0063). It is the
    /// zero-N+1 batch form of <see cref="ScoreAsync"/> — looping <see cref="ScoreAsync"/>
    /// would issue one query per ad (forbidden by the F4-13 acceptance criterion). The
    /// per-ad <see cref="MatchScore"/> equals what <see cref="ScoreAsync"/> would return
    /// for that ad and the same profile (the same Fast dimension helpers run in-memory
    /// over the single batch-loaded row set). NO AI/LLM (ADR 0071).
    /// <para>
    /// <b>Missing ads are silently OMITTED</b> from the result (unlike
    /// <see cref="ScoreAsync"/>, which throws <c>NotFoundException</c> for a single
    /// missing ad) — a batch decoration must not fail a page render because one id is
    /// stale. "Missing" means the ROW DOES NOT EXIST, and nothing else: an ARCHIVED ad
    /// is present and IS scored (no status gate — known gap #864). The old wording said
    /// "missing/soft-deleted", naming an axis that never had a writer and is now retired (#821). The result contains an entry only for each requested ad that exists; the
    /// caller treats "absent from the map" as "no data" (parity the status batch).
    /// Deterministic per key: equal inputs yield an equal score with Ordinal-stable
    /// matched/missing evidence.
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>> ScoreBatchAsync(
        IReadOnlyList<JobAdId> jobAdIds, CandidateMatchProfile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Fas 4 STEG 6 (F4-6, ADR 0074 row U5b; senior-cto-advisor Decision A = A2 + Pa)
    /// — scores the job ad <paramref name="jobAdId"/> against
    /// <paramref name="profile"/> over the FULL set of dimensions: the four
    /// embedded F4-5 Fast dimensions PLUS skill overlap, must-have coverage and
    /// nice-to-have coverage (computed from the ad's F4-4/F4-4b extracted terms
    /// vs the CV's skill concept-ids). NO AI/LLM (ADR 0071). A second method on
    /// the same port (Pa); <see cref="ScoreAsync"/> is unchanged.
    /// <para>
    /// Throws <see cref="Common.Exceptions.NotFoundException"/> if the ad does not
    /// exist (parity <see cref="ScoreAsync"/>). The embedded
    /// <see cref="FullMatchScore.Fast"/> equals what <see cref="ScoreAsync"/> would
    /// return for the same ad and <c>profile.Fast</c>. Each of the three new
    /// dimensions reports <see cref="MatchDimensionVerdict.NotAssessed"/> (never
    /// <see cref="MatchDimensionVerdict.NoMatch"/>) when the CV side has no skill
    /// concept-ids OR the ad has no terms of that kind/source (NULL/empty
    /// <c>extracted_terms</c>); matched/missing surface Display labels, not raw
    /// concept-ids (explainable by design — ADR 0074). Deterministic: equal inputs
    /// yield an equal score with Ordinal-stable evidence.
    /// </para>
    /// <para>
    /// #300 PR-4 (ADR 0084 §F4): returns a <see cref="FullScoredMatch"/> — the score PLUS
    /// <see cref="FullScoredMatch.SsykIsRelated"/> (the ad matched only via a RELATED occupation
    /// group, not the user's exact set). The caller passes that bit to
    /// <see cref="Grading.MatchGradeCalculator.Grade(FullMatchScore, bool)"/> for the Related cap.
    /// Lit by the live <c>?includeRelated</c> toggle (off by default, #300); with it off the
    /// related set is empty, so the flag is <c>false</c> and behaviour is exact-only.
    /// </para>
    /// </summary>
    ValueTask<FullScoredMatch> ScoreFullAsync(
        JobAdId jobAdId, FullCandidateMatchProfile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — scores MANY job ads against the
    /// SAME <paramref name="profile"/> over the FULL set of dimensions in ONE database
    /// round-trip. The zero-N+1 batch form of <see cref="ScoreFullAsync"/> (the
    /// page-scoped match-tag overlay upgraded to Full): looping
    /// <see cref="ScoreFullAsync"/> would issue one query per ad. The per-ad
    /// <see cref="FullMatchScore"/> equals what <see cref="ScoreFullAsync"/> would
    /// return for that ad and the same profile (the same Fast helpers + the same
    /// concept-coverage helpers run in-memory over the single batch-loaded VO set —
    /// the regression contract). NO AI/LLM (ADR 0071).
    /// <para>
    /// <b>Missing ads are silently OMITTED</b> from the result (parity
    /// <see cref="ScoreBatchAsync"/>) — a batch decoration must not fail a page render
    /// because one id is stale. An ARCHIVED ad is NOT missing: it is scored (known gap #864). Each of the three Full dimensions reports
    /// <see cref="MatchDimensionVerdict.NotAssessed"/> (never <c>NoMatch</c>) when the
    /// CV side has no skill concept-ids OR the ad has no terms of that kind/source.
    /// Deterministic per key, with Ordinal-stable evidence.
    /// </para>
    /// <para>
    /// #300 PR-4 (ADR 0084 §F4): each value is a <see cref="FullScoredMatch"/> — the score PLUS
    /// <see cref="FullScoredMatch.SsykIsRelated"/> per ad (the ad matched only via a RELATED
    /// occupation group). Lit by the live <c>?includeRelated</c> toggle (off by default, #300);
    /// with it off the related set is empty, so the flag is <c>false</c> (exact-only).
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyDictionary<JobAdId, FullScoredMatch>> ScoreFullBatchAsync(
        IReadOnlyList<JobAdId> jobAdIds, FullCandidateMatchProfile profile, CancellationToken cancellationToken);
}
