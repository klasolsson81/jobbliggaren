using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;

/// <summary>
/// Sets the current user's STATED job-search preferences (F4-12, ADR 0076; ADR 0079
/// STEG 3) — the desired occupation-groups (ssyk-level-4), regions, municipalities,
/// employment-types, the confirmed skill set, and stated years of experience that feed
/// the deterministic match score. All lists optional; all-empty is valid (clears stated
/// preferences / skipped onboarding). NO AI/LLM (ADR 0071), no CV/PII (concept-ids only).
/// Returns a non-generic <see cref="Result"/> (it creates no new id — it mutates the
/// caller's existing JobSeeker aggregate).
/// <para>
/// <b><see cref="PreferredMunicipalities"/> (Spår 3, ADR 0076-amendment 2026-06-21):</b>
/// the finer-grained location granularity. Optional field — the wire stays additive
/// (a body that omits it binds to <c>null</c> → honest empty).
/// </para>
/// <para>
/// <b><see cref="PreferredSkills"/> + <see cref="ExperienceYears"/> (ADR 0079 STEG 3):</b>
/// the confirmed skill concept-ids (CV-seeded ∪ user-edits, the trusted capability source)
/// and the stated total years of experience (nullable: null = not stated). Both optional
/// and additive — the frontend submits them only from STEG 3 PR-C; until then they are
/// absent (skills empty, experience null). This is a FULL-REPLACE write: a body that omits
/// a field clears it, so the frontend MUST round-trip every dimension (page-wipe guard).
/// </para>
/// </summary>
public sealed record SetMatchPreferencesCommand(
    IReadOnlyList<string>? PreferredOccupationGroups,
    IReadOnlyList<string>? PreferredRegions,
    IReadOnlyList<string>? PreferredEmploymentTypes,
    IReadOnlyList<string>? PreferredMunicipalities = null,
    IReadOnlyList<string>? PreferredSkills = null,
    int? ExperienceYears = null)
    : ICommand<Result>, IAuthenticatedRequest;
