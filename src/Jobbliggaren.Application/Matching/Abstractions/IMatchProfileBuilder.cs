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
    /// <para>
    /// <b><paramref name="includeRelated"/> (ADR 0084 §Architecture(4), issue #300):</b> when
    /// <c>true</c>, the stated exact ssyk-4 occupation set is broadened with its RELATED
    /// (substitutable) groups via <see cref="JobAds.Abstractions.ITaxonomyReadModel.GetRelatedOccupationGroupsAsync"/>
    /// into <see cref="CandidateMatchProfile.RelatedSsykGroupConceptIds"/>. Default <c>false</c>
    /// = exact-only behaviour. The live FE include-related toggle (ADR 0084 question A,
    /// <c>?relaterade=on</c>, off by default) is the only thing that flips it true; with it off
    /// no related set is supplied, so behaviour is exact-only.
    /// </para>
    /// </summary>
    ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(
        CancellationToken cancellationToken, bool includeRelated = false);

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
    /// <para>
    /// <b><paramref name="includeRelated"/> (ADR 0084 §Architecture(4)):</b> when <c>true</c>,
    /// broadens the exact ssyk-4 set with its substitutable groups into the embedded
    /// <c>Fast.RelatedSsykGroupConceptIds</c>. Default <c>false</c> = exact-only; the live
    /// <c>?relaterade=on</c> toggle flips it. Related ads cap at <c>MatchGrade.Related</c> — never Good/Strong (so
    /// they never enter this path's headline-grade count, ADR 0084 question D list-only).
    /// </para>
    /// </summary>
    ValueTask<FullCandidateMatchProfile> BuildFullForSortAsync(
        CancellationToken cancellationToken, bool includeRelated = false);

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
    /// <para>
    /// <b><paramref name="includeRelated"/> (ADR 0084 §Architecture(4)):</b> when <c>true</c>,
    /// broadens the exact ssyk-4 set with its substitutable groups into the embedded
    /// <c>Fast.RelatedSsykGroupConceptIds</c>. Default <c>false</c> = exact-only; the live
    /// <c>?relaterade=on</c> toggle flips it. A related hit caps at <c>MatchGrade.Related</c> in the verdict.
    /// </para>
    /// </summary>
    ValueTask<FullCandidateMatchProfile> BuildFullForVerdictAsync(
        CancellationToken cancellationToken, bool includeRelated = false);

    /// <summary>
    /// ADR 0080 Vag 4 PR-2 (Beslut 3) — builds the FULL profile for an EXPLICIT user-id,
    /// for BACKGROUND / SYSTEM contexts that have no <c>ICurrentUser</c> (the Worker
    /// background-matching scan iterates opted-in users and scores ads per user). Identical
    /// build to <see cref="BuildFullForSortAsync"/> — the confirmed plaintext skill set +
    /// the denormalized <c>LatestRole</c>, <b>DEK-FREE</b> (no per-user KMS in the hot loop —
    /// the STEG 3 enabler that unblocks Wave 4). The ONLY difference is the load key: the
    /// JobSeeker is fetched by the passed <paramref name="userId"/> instead of via
    /// <c>ICurrentUser</c> (an OCP extension — the request-scoped owner path is untouched).
    /// An unknown user / no JobSeeker / no stated preferences yields the honest EMPTY profile
    /// (empty SSYK → the Worker simply produces no matches for that user).
    /// </summary>
    ValueTask<FullCandidateMatchProfile> BuildFullForUserIdAsync(
        Guid userId, CancellationToken cancellationToken);
}
