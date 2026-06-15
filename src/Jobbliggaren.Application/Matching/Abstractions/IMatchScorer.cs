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
}
