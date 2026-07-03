using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;

/// <summary>
/// #444 — "min ansökningshistorik per arbetsgivare". Parameterless and owner-scoped: returns the
/// signed-in user's OWN submitted applications grouped by employer org.nr (never another user's —
/// IDOR-safe via <c>ICurrentUser</c>, enforced by <see cref="IAuthenticatedRequest"/>). Not
/// paginated: a single user's application history is bounded (parity <c>ListCompanyWatchesQuery</c> /
/// <c>ListSavedSearchesQuery</c>). org.nr is surfaced under the personnummer guard — see
/// <see cref="EmployerApplicationHistoryDto"/>.
/// </summary>
public sealed record GetEmployerApplicationHistoryQuery
    : IQuery<IReadOnlyList<EmployerApplicationHistoryDto>>, IAuthenticatedRequest;
