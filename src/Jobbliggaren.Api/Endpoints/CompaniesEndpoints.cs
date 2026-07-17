using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Companies.Queries.LookupCompany;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #454 (ADR 0088 D7) — the company surface: registry-backed lookups about COMPANIES (SCB subject),
/// distinct from the ad corpus (<c>/job-ads</c>) and the user's owned watches
/// (<c>/me/company-watches</c>). Route prefix <c>/api/v1/companies</c>; the whole group is
/// auth-gated. Natural future home for #448's deferred företagsdetalj endpoints.
///
/// <para>
/// <b>org.nr in BODY, never URL (ADR 0087 D8(c), CLAUDE.md §5):</b> a sole-prop org.nr can equal a
/// personnummer, and a URL path/query is logged un-flagged by infra we don't control — so the lookup
/// is a POST-as-read with the org.nr in the JSON body (precedent: <c>POST /me/company-watches/status</c>).
/// The handler additionally REFUSES personnummer-shaped input before any registry transmission
/// (ADR 0088 D4); <c>notFound</c>/<c>unavailable</c> are 200-with-status (never-500 civic
/// degradation), and only a malformed org.nr / refused input is a 400 via the Result mapper.
/// </para>
/// </summary>
public static class CompaniesEndpoints
{
    /// <summary>Lookup request — the org.nr travels in the body per D8(c) (never path/query).</summary>
    public sealed record CompanyLookupRequest(string OrganizationNumber)
    {
        /// <summary>
        /// REDACTED (#883). The org.nr is client-supplied and IS the entire payload; a record's
        /// compiler-generated <c>ToString()</c> would write it into a log for a plain <c>{X}</c> MEL
        /// placeholder, and it can be a sole-prop personnummer (ADR 0087 D8(c); CLAUDE.md §5). Only the
        /// type name is safe to keep; pinned by <c>OrgNrRecordLoggingGuardTests</c>.
        /// </summary>
        public override string ToString() => "CompanyLookupRequest(org.nr redacted)";
    }

    public static void MapCompaniesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/companies")
            .WithTags("Companies")
            .RequireAuthorization();

        // Dedicated CompanyLookupPolicy (ADR 0088 D7): every miss is a potential upstream SCB call
        // once the real adapter activates — never shared budget with light local reads (bulkhead).
        // Cache-Control: private, no-store — the response varies per user (matching count, follow
        // state) and must never land in a shared proxy cache.
        group.MapPost("/lookup", async (
            CompanyLookupRequest body, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(
                new LookupCompanyQuery(body.OrganizationNumber ?? string.Empty), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.CompanyLookupPolicy);
    }
}
