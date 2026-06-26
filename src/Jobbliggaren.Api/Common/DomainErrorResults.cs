using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Api.Common;

/// <summary>
/// Centralises the mapping of a <see cref="DomainError"/> to an HTTP ProblemDetails response for
/// mutation endpoints — the single place the Result-side error→status rule is expressed (TD-63
/// kind-union; #203 / TD-84).
///
/// <para>The status is chosen from <see cref="DomainError.Kind"/> (the discriminator stamped by the
/// factories), NOT from the error <c>Code</c> string: Validation→400, NotFound→404, Conflict→409,
/// Gone→410. This replaced the earlier <c>Code.EndsWith(".NotFound")</c> heuristic (a magic-string,
/// CLAUDE.md §5) — which could only express 400/404 and mis-mapped both non-conventional not-found
/// codes (e.g. <c>"Auth.JobSeekerNotFound"</c>) and every Conflict/Gone error to 400.</para>
///
/// <para>The mapping is a transport/presentation concern and therefore lives in the Api layer, not
/// in Domain (<see cref="DomainError"/> carries <c>Code</c> + <c>Message</c> + <c>Kind</c> and knows
/// nothing about HTTP). This is the Result-side idiom (CLAUDE.md §3: expected failures → Result);
/// the parallel exception-side idiom (<c>NotFoundException</c>→404, <c>DomainException</c>→400) stays
/// mapped by the API middleware. The two coexist deliberately.</para>
///
/// <para>The switch is exhaustive over today's <see cref="ErrorKind"/> set; the <c>_ =&gt; 500</c>
/// arm is a fail-loud guard so that adding a future kind without a mapper case surfaces as a 500
/// here instead of silently defaulting to the 400 floor. A genuinely auth-only status that the
/// kind-union does not model (401) stays an endpoint-local concern (see <c>AuthEndpoints</c>).</para>
/// </summary>
internal static class DomainErrorResults
{
    internal static IResult ToProblemResult(this DomainError error) =>
        Results.Problem(
            detail: error.Message,
            title: error.Code,
            statusCode: error.Kind switch
            {
                ErrorKind.Validation => StatusCodes.Status400BadRequest,
                ErrorKind.NotFound => StatusCodes.Status404NotFound,
                ErrorKind.Conflict => StatusCodes.Status409Conflict,
                ErrorKind.Gone => StatusCodes.Status410Gone,
                _ => StatusCodes.Status500InternalServerError,
            });
}
