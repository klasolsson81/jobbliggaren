using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #444/#446 (ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1) — the signed-in user's OWN application
/// history, the backbone of the Företag-hub's history surface. Route prefix
/// <c>/api/v1/me/application-history</c> ("my data" convention); the whole group is auth-gated (these
/// reads are application-history profiling — Art. 6(1)(b), so they carry the same GDPR posture as #444
/// and never fall back to the anonymous-tolerant status-batch shape).
///
/// <para>
/// <b>GET history, not POST-as-read (senior-cto-advisor 2026-07-03).</b> The D8(c) POST-body convention
/// (<c>CompanyWatchesEndpoints</c>) exists to keep a personnummer-shaped org.nr out of URLs/access-
/// logs when a REQUEST carries one. The history read carries NO org.nr in the request — it returns the
/// caller's full grouped history — so the convention's rationale is absent and a safe, idempotent GET
/// is the honest verb, a direct mirror of <c>GET /me/company-watches</c>. The response surfaces only
/// masked (sole-prop-nulled) or public legal-entity org.nr (ADR 0087 D8(a)); response bodies are not
/// access-logged.
/// </para>
///
/// <para>
/// <b>#446 counts overlay — POST (a batch of JobAdIds).</b> The /jobb card badge "Du har X tidigare
/// ansökningar till detta företag" needs a per-JobAdId count for one list page. It is a batch overlay
/// (parity <c>POST /me/job-ad-status</c> / <c>POST /me/job-ad-match-tags</c>): POST because the request
/// carries a list body, but it is a pure read that persists nothing. The request carries only JobAdIds
/// (non-PII) and the response only <c>int</c> counts — no org.nr in either direction.
/// </para>
/// </summary>
public static class ApplicationHistoryEndpoints
{
    public sealed record EmployerApplicationCountBatchRequest(IReadOnlyList<Guid> JobAdIds);

    public static void MapApplicationHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/application-history")
            .WithTags("ApplicationHistory")
            .RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetEmployerApplicationHistoryQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // #446 — the /jobb card badge overlay. Auth-gated (group) + per-user read bucket (parity #444 /
        // match-detail): this is application-history profiling, never the anonymous-tolerant status batch.
        group.MapPost("/counts", async (
            EmployerApplicationCountBatchRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new GetEmployerApplicationCountBatchQuery(body.JobAdIds ?? []), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);
    }
}
