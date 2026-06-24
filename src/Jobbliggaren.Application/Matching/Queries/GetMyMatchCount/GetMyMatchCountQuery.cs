using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;

/// <summary>
/// ADR 0079 STEG 6 — "min match-count" för Översikts live-notis. Parameterlös: counten är
/// den autentiserade användarens egen (profilen byggs ur <c>ICurrentUser</c> inne i
/// <c>IMatchProfileBuilder</c>). INTE <c>ICapturesRecentSearch</c> — en count är ingen
/// sök-trace, så den fångas aldrig som Senaste sökning och rör aldrig SearchCriteria/
/// FilterHash-identiteten.
/// </summary>
public sealed record GetMyMatchCountQuery : IQuery<MyMatchCountDto>;
