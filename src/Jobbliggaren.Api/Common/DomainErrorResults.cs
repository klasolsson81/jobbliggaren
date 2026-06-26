using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Api.Common;

/// <summary>
/// Centralises the mapping of a <see cref="DomainError"/> to an HTTP
/// ProblemDetails response for mutation endpoints (TD-84 / #203).
///
/// Previously every endpoint hardcoded <c>statusCode: 400</c> for all Result
/// failures — even not-found ones — which broke REST semantics (RFC 9110
/// §15.5.5). The mapping is a transport/presentation concern and therefore lives
/// in the Api layer, not in Domain (<see cref="DomainError"/> carries only
/// <c>Code</c> + <c>Message</c> and knows nothing about HTTP).
///
/// Not-found is recognised via the <c>.NotFound</c> code suffix, which is exactly
/// the convention produced by <see cref="DomainError.NotFound"/>
/// (<c>"{entity}.NotFound"</c>). Endpoints with richer per-context semantics
/// (e.g. 409/410 for invitation conflicts) keep their own switch mappers and do
/// NOT use this helper. A type-safe kind union (instead of the string convention)
/// is tracked separately as TD-63.
/// </summary>
internal static class DomainErrorResults
{
    internal static IResult ToProblemResult(this DomainError error) =>
        Results.Problem(
            detail: error.Message,
            title: error.Code,
            statusCode: error.Code.EndsWith(".NotFound", StringComparison.Ordinal) ? 404 : 400);
}
