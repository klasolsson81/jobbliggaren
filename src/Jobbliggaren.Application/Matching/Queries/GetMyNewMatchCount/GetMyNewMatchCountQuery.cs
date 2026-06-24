using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — the count of the user's background matches NEW since their last visit
/// (<c>UserJobAdMatch.CreatedAt &gt; JobSeeker.LastSeenMatchesAt</c>). Drives the Oversikt "Nya
/// matchningar"-row (replacing STEG 6's mock "i dag"). Parameterless — the count is the
/// authenticated user's own. NOT <c>ICapturesRecentSearch</c>.
/// </summary>
public sealed record GetMyNewMatchCountQuery : IQuery<MyNewMatchCountDto>;
