using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetMyMatches;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — the dedicated "Mina matchningar" view: the authenticated user's
/// background matches (most recent first), each with its job-ad details + grade + an IsNew flag.
/// Parameterless — owner-scoped, capped at the most recent matches (the view shows recent matches;
/// the full corpus is reachable via the /jobb grade-filter). NOT <c>ICapturesRecentSearch</c>.
/// </summary>
public sealed record GetMyMatchesQuery : IQuery<IReadOnlyList<MatchListItemDto>>;
