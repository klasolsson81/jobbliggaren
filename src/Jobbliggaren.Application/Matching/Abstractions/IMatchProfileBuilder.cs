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
    /// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6 + R5-REBIND Option H) — builds the
    /// FULL profile for the GLOBAL match-SORT path from the preferences (the embedded
    /// Fast profile) PLUS the primary CV's <b>top-5 plaintext</b> denormalized skills
    /// (<c>Resume.TopSkills</c>, ADR 0058/0059), resolved to concept-ids via
    /// <see cref="ISkillResolver"/>. <b>NO DEK / no field-encryption key is read</b> —
    /// <c>TopSkills</c> is a plaintext projection column. Used by the unbounded
    /// "Sortera efter matchning" sort, where the skill signal is only a binary GIN
    /// overlap rung (emits no verdict) → top-5 can only under-lift, never falsely lift
    /// (honest reduced-recall). No primary CV / no resolved skills →
    /// <c>CvSkillConceptIds</c> empty (degrade to Fast). Owner-scoped.
    /// </summary>
    ValueTask<FullCandidateMatchProfile> BuildFullFromTopSkillsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6 + R5-REBIND Option H) — builds the
    /// FULL profile for the page-scoped match-TAG batch from the preferences PLUS the
    /// primary CV's <b>complete</b> skills (<c>Resume.MasterVersion.Content.Skills</c>),
    /// resolved to concept-ids via <see cref="ISkillResolver"/>. The CV content is
    /// encrypted, so this path warms the owner DEK <b>imperatively</b> (parity
    /// <c>FieldEncryptionKeyPrefetchBehavior</c>) before materialising the content —
    /// <b>fail-closed</b>: a KMS/DEK failure PROPAGATES (it never degrades to an empty
    /// skill set, which would be a dishonest <c>NotAssessed</c>). The complete set is
    /// required here because <c>ScoreConceptCoverage</c> emits <c>NoMatch</c> (not
    /// <c>NotAssessed</c>) on a disjoint non-empty set, so a truncated set would
    /// mis-report a covered must-have as missing on a verdict-bearing surface
    /// (CLAUDE.md §5). No primary CV / no resolved skills → <c>CvSkillConceptIds</c>
    /// empty (degrade to Fast). Owner-scoped. security-auditor-gated.
    /// </summary>
    ValueTask<FullCandidateMatchProfile> BuildFullFromCvSkillsAsync(CancellationToken cancellationToken);
}
