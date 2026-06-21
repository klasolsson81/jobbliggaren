using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;

/// <summary>
/// Sets the current user's STATED job-search preferences (F4-12, ADR 0076) — the
/// desired occupation-groups (ssyk-level-4), regions, municipalities, and
/// employment-types that feed the deterministic match score. All lists optional;
/// all-empty is valid (clears stated preferences / skipped onboarding). NO AI/LLM
/// (ADR 0071), no CV/PII. Returns a non-generic <see cref="Result"/> (it creates no
/// new id — it mutates the caller's existing JobSeeker aggregate).
/// <para>
/// <b><see cref="PreferredMunicipalities"/> (Spår 3, ADR 0076-amendment 2026-06-21):</b>
/// the finer-grained location granularity. Optional 4th field — the wire stays additive
/// (a body that omits it binds to <c>null</c> → honest empty). The frontend submits it
/// only from PR-D; until then it is absent and the dimension is empty.
/// </para>
/// </summary>
public sealed record SetMatchPreferencesCommand(
    IReadOnlyList<string>? PreferredOccupationGroups,
    IReadOnlyList<string>? PreferredRegions,
    IReadOnlyList<string>? PreferredEmploymentTypes,
    IReadOnlyList<string>? PreferredMunicipalities = null)
    : ICommand<Result>, IAuthenticatedRequest;
