namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// Builds the current user's <see cref="CandidateMatchProfile"/> from their STATED
/// match preferences (F4-12/F4-13, ADR 0076). The single SSOT for the
/// preference→profile mapping — consumed BOTH by
/// <c>BuildMatchProfileFromPreferencesQueryHandler</c> (the explicit query) AND by the
/// page-scoped match-tag batch handler (F4-13), so the rule lives in exactly one
/// place (DRY/SPOT). The new query handler delegating to this collaborator avoids a
/// handler-invokes-handler anti-pattern (CLAUDE.md §2.3).
/// <para>
/// <b>Preference-only, no CV/DEK/PII (ADR 0076 Decision 2 amendment / F4-12 Verdict 2a):</b>
/// it reads only the stored <c>MatchPreferences</c> (owner-scoped, current user only) —
/// no CV content, no field-encryption key. <c>Title</c> is always empty on the produced
/// profile (the preference path carries no CV title → the title-similarity dimension
/// reports <c>NotAssessed</c>; CV influence on matching begins at F4-15). An absent
/// user / JobSeeker / preference set yields an honest EMPTY profile (empty lists →
/// the match dimensions report <c>NotAssessed</c>, never <c>NoMatch</c>), never an error.
/// </para>
/// </summary>
public interface IMatchProfileBuilder
{
    /// <summary>
    /// Builds the <see cref="CandidateMatchProfile"/> for the current authenticated
    /// user from their stored preferences. Owner-scoped (reads only the current user's
    /// preferences). Returns the honest empty profile when there is no authenticated
    /// user, no JobSeeker, or no stated preferences.
    /// </summary>
    ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Builds the FULL profile for the GLOBAL match-SORT path: the embedded Fast profile
    /// PLUS the user's CONFIRMED skill set (<c>MatchPreferences.PreferredSkills</c>,
    /// plaintext concept-ids) as <c>CvSkillConceptIds</c>.
    /// <para>
    /// <b>ADR 0079 STEG 3 PR-D (Beslut 1):</b> the trusted capability source is the
    /// user-confirmed set (CV-proposals ∪ user-edits), NOT the raw CV skills. This is
    /// IDENTICAL to <see cref="BuildFullForVerdictAsync"/> (the two members stay distinct
    /// only for call-site intent — sort vs verdict — but share one implementation) and is
    /// <b>DEK-FREE</b>: the confirmed concept-ids are plaintext on the JobSeeker, so the SQL
    /// golden rung reads the SAME source as the verdict scorer (sort==grade coherent — a
    /// removed/added skill can never lift an ad in one path while the other ignores it).
    /// The former top-5 plaintext <c>Resume.TopSkills</c> path (R5-REBIND Option H) is gone.
    /// </para>
    /// The only CV read is the denormalized plaintext <c>Resume.LatestRole</c> for the Title
    /// dimension (STEG 4, ADR 0058/0059, DEK-free). Empty confirmed set →
    /// <c>CvSkillConceptIds</c> empty (skill dimensions <c>NotAssessed</c>). Owner-scoped.
    /// </summary>
    ValueTask<FullCandidateMatchProfile> BuildFullForSortAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Builds the FULL profile for the page-scoped match-TAG / modal VERDICT surface: the
    /// Fast profile PLUS the user's CONFIRMED skill set
    /// (<c>MatchPreferences.PreferredSkills</c>, plaintext concept-ids).
    /// <para>
    /// <b>ADR 0079 STEG 3 PR-D (Beslut 1):</b> reads the confirmed set — and is now
    /// <b>DEK-FREE</b>. The former DEK-warmed read of the complete encrypted
    /// <c>Content.Skills</c> (R5-REBIND Option H, fail-closed) is REMOVED: the confirmed set
    /// is the complete, curated set by definition, so there is no truncation/mis-report risk
    /// (the reason the old path needed the full encrypted skills) and no per-request KMS
    /// dependency. IDENTICAL to <see cref="BuildFullForSortAsync"/> — both read the same
    /// confirmed source, so the verdict and the sort can never diverge on a skill.
    /// </para>
    /// The only CV read is the denormalized plaintext <c>Resume.LatestRole</c> for the Title
    /// dimension (STEG 4, ADR 0058/0059, DEK-free). Empty confirmed set →
    /// <c>CvSkillConceptIds</c> empty (skill/requirement dimensions <c>NotAssessed</c>,
    /// honest). Owner-scoped.
    /// </summary>
    ValueTask<FullCandidateMatchProfile> BuildFullForVerdictAsync(CancellationToken cancellationToken);
}
