using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;

/// <summary>
/// Sets the current user's STATED job-search preferences (F4-12, ADR 0076) — the
/// desired occupation-groups (ssyk-level-4), regions, and employment-types that feed
/// the deterministic match score. All lists optional; all-empty is valid (clears
/// stated preferences / skipped onboarding). NO AI/LLM (ADR 0071), no CV/PII.
/// Returns a non-generic <see cref="Result"/> (it creates no new id — it mutates the
/// caller's existing JobSeeker aggregate).
/// </summary>
public sealed record SetMatchPreferencesCommand(
    IReadOnlyList<string>? PreferredOccupationGroups,
    IReadOnlyList<string>? PreferredRegions,
    IReadOnlyList<string>? PreferredEmploymentTypes)
    : ICommand<Result>, IAuthenticatedRequest;
