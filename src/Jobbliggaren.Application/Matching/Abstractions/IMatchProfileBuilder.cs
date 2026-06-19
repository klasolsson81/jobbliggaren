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
}
