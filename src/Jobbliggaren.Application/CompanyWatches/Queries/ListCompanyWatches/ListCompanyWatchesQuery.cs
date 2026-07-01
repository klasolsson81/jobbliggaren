using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches;

/// <summary>
/// "Mina bevakade arbetsgivare". UserId-scoped, active follows only. Not paginated — a user
/// follows a handful of employers (KISS, mirrors ListSavedSearchesQuery). Org.nr is surfaced under
/// the personnummer guard (FORK C1 / D8(c)) — see <see cref="CompanyWatchDto"/>.
/// </summary>
public sealed record ListCompanyWatchesQuery
    : IQuery<IReadOnlyList<CompanyWatchDto>>, IAuthenticatedRequest;
