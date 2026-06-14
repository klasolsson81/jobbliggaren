using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.DeriveOccupationCodes;

/// <summary>
/// Fas 4 STEG 3 (F4-3) — derives proposed ssyk-level-4 occupation-group
/// candidate(s) for a free-text occupational <see cref="Title"/> via deterministic
/// taxonomy lookup (ADR 0040 amendment, ADR 0074). Read-only: nothing is persisted
/// (the <c>SavedSearch</c> is created downstream of the user's confirm — ADR 0040
/// Beslut 4). The DoS-/garbage-floor (NotEmpty + MaximumLength) is enforced by
/// <see cref="DeriveOccupationCodesQueryValidator"/> in the Validation pipeline
/// before the handler.
/// </summary>
public sealed record DeriveOccupationCodesQuery(string Title)
    : IQuery<OccupationDerivationResult>;
