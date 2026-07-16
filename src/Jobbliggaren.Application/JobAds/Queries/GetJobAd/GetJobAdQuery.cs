using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobAd;

/// <summary>
/// Ad detail by id. Returns <c>Result</c> rather than a nullable DTO because "missing" and
/// "erased" are different answers and only one of them is true (#842 — 404 vs 410; see
/// <see cref="GetJobAdQueryHandler"/>).
/// </summary>
public sealed record GetJobAdQuery(Guid Id) : IQuery<Result<JobAdDetailDto>>;
