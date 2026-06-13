using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.UserStatus.Queries.HasApplied;

/// <summary>
/// ADR 0063 — modal-yta single-anrop ("har inloggad JobSeeker ansökt på
/// denna JobAd"). Symmetri med `isJobAdSaved`-lookup-mönstret från PR1-4.
/// Anonym user → false (no 401-friktion).
/// </summary>
public sealed record HasAppliedQuery(Guid JobAdId) : IQuery<bool>;
