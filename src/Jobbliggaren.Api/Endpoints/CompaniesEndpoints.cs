using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Companies.Queries.LookupCompany;
using Jobbliggaren.Application.CompanyRegister.Queries.GetCompanySearchMagnitude;
using Jobbliggaren.Application.CompanyRegister.Queries.SearchCompanies;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;
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

        // #560 company-search wave — the general register search (/foretag/sok). POST-as-read,
        // NOT GET: the org.nr term travels in the BODY per the group's D8(c) rule (a user-typed
        // term can be personnummer-adjacent before the normalizer refuses it, and a URL/query
        // lands in infra logs we don't control — the /lookup + /status precedent). TWO mediator
        // sends composed (§2.3, the criterion-browse precedent): the page (whose
        // PagedResult.TotalCount is a PAGINATION quantity, capped — never a magnitude) and the
        // honest magnitude (own count, own ceiling, "10 000+" when saturated). Input errors 400
        // in ValidationBehavior — both validators transport ONE Application-side normalizer,
        // deliberately not named here (the carrier-name guard keeps every raw-carrier type out
        // of this project's source, prose included). Rides CompanyBrowsePolicy (CTO F1): same
        // register, same cost class as the criterion browse — one bulkhead budget, no upstream
        // call to protect (unlike /lookup).
        group.MapPost("/search", async (
            CompanySearchRequest body, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";

            // Bare responses by contract: input errors 400 in ValidationBehavior (both
            // validators transport the SAME single normalizer), and an empty page/zero
            // magnitude is an honest answer — no Result, no not-found.
            var page = await mediator.Send(
                new SearchCompaniesQuery(
                    body.SniCodes, body.MunicipalityCodes, body.Name, body.OrganizationNumber,
                    body.Page, body.PageSize),
                ct);

            // The magnitude is computed ONLY for a filtered search, and the skip is the point
            // rather than an optimisation. Unfiltered, the honest answer is the whole active
            // register - 743 654 rows, measured 2026-08-01 - which the ceiling can only render as
            // "10 000+", a number that understates the register by two orders of magnitude while
            // being technically true. Klas ruled (2026-08-01): show the exact number if it is
            // free, otherwise show NO number - never the saturated one.
            //
            // It is not free. An exact count is an index-only scan over ix_company_register_status:
            // 26 ms with the visibility map set, but 438 ms without it, and `autovacuum_count` on
            // this table is ZERO - it has never run, because the table is written by one periodic
            // bulk job and the SCB refresher ANALYZEs but does not VACUUM. So the cheap case is
            // not the case that persists, and the expensive one would land on every unfiltered
            // page load.
            //
            // Skipping also removes the ~1 ms ceilinged count we DID pay for a headline that is
            // now a plain heading, so the unfiltered path costs strictly less than before.
            var hasFilter =
                body.SniCodes is { Count: > 0 }
                || body.MunicipalityCodes is { Count: > 0 }
                || !string.IsNullOrWhiteSpace(body.Name)
                || !string.IsNullOrWhiteSpace(body.OrganizationNumber);

            var magnitude = hasFilter
                ? await mediator.Send(
                    new GetCompanySearchMagnitudeQuery(
                        body.SniCodes, body.MunicipalityCodes, body.Name, body.OrganizationNumber),
                    ct)
                : null;

            return Results.Ok(new CompanySearchResponse(page, magnitude));
        }).RequireRateLimiting(RateLimitingExtensions.CompanyBrowsePolicy);
    }

    /// <summary>
    /// Search request — the org.nr term travels in the body per D8(c) (never path/query). All
    /// axes optional; an absent axis means "do not filter on it" (browse-all is legal).
    /// </summary>
    public sealed record CompanySearchRequest(
        IReadOnlyList<string?>? SniCodes = null,
        IReadOnlyList<string?>? MunicipalityCodes = null,
        string? Name = null,
        string? OrganizationNumber = null,
        int Page = 1,
        int PageSize = 20)
    {
        /// <summary>
        /// REDACTED (#883): the client-supplied org.nr term can be personnummer-shaped (that is
        /// exactly what the normalizer refuses) and a record's compiler-generated
        /// <c>ToString()</c> would print it. Pinned by <c>OrgNrRecordLoggingGuardTests</c>.
        /// </summary>
        public override string ToString() =>
            $"CompanySearchRequest(sni: {SniCodes?.Count ?? 0}, kommun: {MunicipalityCodes?.Count ?? 0}, "
            + $"name: {(string.IsNullOrWhiteSpace(Name) ? "no" : "yes")}, org.nr redacted, "
            + $"page {Page}/{PageSize})";
    }

    /// <summary>
    /// The composed search response: the page and the honest magnitude side by side, so the FE
    /// can never mistake the pagination count for the magnitude (the criterion-browse precedent).
    ///
    /// <para><see cref="Magnitude"/> is NULL for an unfiltered browse, and that is a contract, not
    /// a degradation: the unfiltered headline carries no number at all (see the skip above). A
    /// filtered search always carries one.</para>
    /// </summary>
    public sealed record CompanySearchResponse(
        PagedResult<CompanyBrowseDto> Companies,
        CompanySearchMagnitudeDto? Magnitude);
}
