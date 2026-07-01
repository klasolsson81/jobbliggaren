using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusBatch;

/// <summary>
/// #311 #455 (ADR 0087 D8(c); ADR 0063 batch-overlay precedent) — batch-resolver for "which of these
/// ads' employers does the current user follow, and are they followable". Consumed by the /jobb detail
/// footer today (single-ad) and shaped for the /jobb list overlay when the card affordance lands
/// (the deferred card-affordance TD). Max 100 ids per call (validator, parity ADR 0063).
///
/// <para>
/// <b>Auth-gated (NOT anon-tolerant, unlike <c>GetJobAdStatusBatchQuery</c>):</b> follow-state is
/// per-user-private, so this is an <see cref="IAuthenticatedRequest"/> and the endpoint requires auth
/// (anon → 401). The handler still returns an empty result defensively when no user is present.
/// </para>
/// </summary>
public sealed record GetCompanyWatchStatusBatchQuery(IReadOnlyList<Guid> JobAdIds)
    : IQuery<CompanyWatchStatusBatchDto>, IAuthenticatedRequest;
