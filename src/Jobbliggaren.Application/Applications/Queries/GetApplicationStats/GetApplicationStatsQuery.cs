using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Applications.Queries.GetApplicationStats;

/// <summary>
/// Application-statistics query (issue #313). No parameters — the metric set and
/// the rolling 12-month window are fixed (senior-cto-advisor bind 2026-06-29), so
/// there is no client-supplied input to validate (hence no validator). Read-only,
/// JobSeeker-scoped in the handler; <see cref="IAuthenticatedRequest"/> requires
/// an authenticated caller (parity with <c>GetPipelineQuery</c>).
/// </summary>
public sealed record GetApplicationStatsQuery
    : IQuery<ApplicationStatsDto>, IAuthenticatedRequest;
