using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #444 (ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1) — the signed-in user's OWN application
/// history grouped by employer, the backbone of the Företag-hub's history surface. Route prefix
/// <c>/api/v1/me/application-history</c> ("my data" convention); the whole group is auth-gated.
///
/// <para>
/// <b>GET, not POST-as-read (senior-cto-advisor 2026-07-03).</b> The D8(c) POST-body convention
/// (<c>CompanyWatchesEndpoints</c>) exists to keep a personnummer-shaped org.nr out of URLs/access-
/// logs when a REQUEST carries one. This read carries NO org.nr in the request — it returns the
/// caller's full grouped history — so the convention's rationale is absent and a safe, idempotent GET
/// is the honest verb, a direct mirror of <c>GET /me/company-watches</c>. The response surfaces only
/// masked (sole-prop-nulled) or public legal-entity org.nr (ADR 0087 D8(a)); response bodies are not
/// access-logged.
/// </para>
/// </summary>
public static class ApplicationHistoryEndpoints
{
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
    }
}
