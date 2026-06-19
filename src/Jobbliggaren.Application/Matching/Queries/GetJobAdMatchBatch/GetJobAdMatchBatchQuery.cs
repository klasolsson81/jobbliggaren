using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;

/// <summary>
/// F4-13 (ADR 0076 Decision 5) — batch-resolver for "which of these ads earn a match
/// tag, and at what grade" for the current user, given their stated match preferences.
/// Hot-path for the /jobb list (≤100 ids/page, validator-capped → ONE query, zero N+1).
/// <para>
/// Deliberately NOT <c>IAuthenticatedRequest</c> (parity <c>GetJobAdStatusBatchQuery</c>):
/// anonymous callers — and authenticated users without a stated occupation — get an
/// empty result (no tags), no 401 friction on the public search page. The FE may call it
/// unconditionally and branch on the empty map.
/// </para>
/// </summary>
public sealed record GetJobAdMatchBatchQuery(IReadOnlyList<Guid> JobAdIds)
    : IQuery<JobAdMatchBatchDto>;
