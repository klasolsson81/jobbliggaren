namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// Fas 4 STEG 6 (F4-6, ADR 0074 row U5b; senior-cto-advisor Decision B = B2,
/// skill-only v1) — the CV-side input to the deterministic FULL match score. It
/// is the Fast-mode profile PLUS the CV's skill taxonomy concept-ids, so the
/// engine can compute skill overlap + employer-requirement coverage against the
/// ad's extracted terms.
/// <para>
/// <b>Composition over a wider Fast profile (B2, CTO Decision B):</b> F4-5's
/// <see cref="CandidateMatchProfile"/> is deliberately frozen at four fields
/// (F4-5 Resolution B — do not grow Fast for a later capability). F4-6 is the
/// consumer that genuinely reads CV skill ids, so the new field lives on the
/// FULL profile that needs it (ISP), embedding the frozen Fast profile rather
/// than mutating it. <see cref="FullMatchScore"/> mirrors this by embedding
/// <see cref="MatchScore"/>.
/// </para>
/// <para>
/// <b>Skill-only v1 (CTO Decision B, F4-4b Decision 1A):</b> the CV side carries
/// only <see cref="CvSkillConceptIds"/> — the sole concept-producer the CV has
/// today (resume skills → JobTech taxonomy). There is intentionally NO
/// <c>CvKeywordLexemes</c> field: free-text keyword lexemes require parsed CV
/// prose that does not exist before F4-8/9, so keyword overlap is OMITTED v1
/// (not a <c>NotAssessed</c> placeholder — honest-data parity with F4-5
/// Decision 0). It grows additively when a real CV-free-text producer arrives.
/// </para>
/// <para>
/// <b>PII boundary (ADR 0074 Invariant 3):</b> the embedded
/// <see cref="CandidateMatchProfile.Title"/> is a plain caller-supplied string
/// and <see cref="CvSkillConceptIds"/> are derived non-PII JobTech concept-ids,
/// confirmed upstream (parity F4-5's confirmed-ssyk boundary). The personnummer
/// guard + field-encryption pipeline live at the F4-8 call-site that produces
/// this profile from CV content; F4-6 reads no raw CV PII and never logs.
/// </para>
/// </summary>
/// <param name="Fast">The frozen F4-5 Fast-mode profile (title + confirmed
/// SSYK level-4 ids + preferred region/employment-type ids), embedded unchanged.</param>
/// <param name="CvSkillConceptIds">The CV's skill taxonomy concept-ids
/// (caller-supplied, confirmed upstream) — drives BOTH the
/// <see cref="FullMatchScore.SkillOverlap"/> dimension and the requirement
/// coverage dimensions. An empty list ⇒ the three new dimensions report
/// <see cref="MatchDimensionVerdict.NotAssessed"/> (never <c>NoMatch</c>).</param>
public sealed record FullCandidateMatchProfile(
    CandidateMatchProfile Fast,
    IReadOnlyList<string> CvSkillConceptIds);
