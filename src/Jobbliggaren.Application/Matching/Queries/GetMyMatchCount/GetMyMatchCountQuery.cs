using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;

/// <summary>
/// ADR 0079 STEG 6 — "min match-count" för Översikts live-notis. Parameterlös: counten är
/// den autentiserade användarens egen (profilen byggs ur <c>ICurrentUser</c> inne i
/// <c>IMatchProfileBuilder</c>).
/// </summary>
public sealed record GetMyMatchCountQuery : IQuery<MyMatchCountDto>;
