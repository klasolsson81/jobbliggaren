using System.Security.Cryptography;
using System.Text.Json;
using JobbPilot.Api.RateLimiting;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.CreateJobAd;
using JobbPilot.Application.JobAds.Queries.GetFacetCounts;
using JobbPilot.Application.JobAds.Queries.GetJobAd;
using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Application.JobAds.Queries.SuggestJobAdTerms;
using JobbPilot.Domain.JobAds;
using Mediator;

namespace JobbPilot.Api.Endpoints;

public static class JobAdsEndpoints
{
    public static void MapJobAdsEndpoints(this IEndpointRouteBuilder app)
    {
        // ADR 0005: JobAd-listning/sökning är auth-gated i Fas 2-start. Anonym
        // publik katalog kan låsas upp senare via separat ADR efter mätning av
        // JobTech-proxy-kostnad och bot-trafik.
        var group = app.MapGroup("/api/v1/job-ads")
            .WithTags("JobAds")
            .RequireAuthorization();

        // GET routes (list + by-id) skyddas med ListReadPolicy mot multi-query-
        // DoS från komprometterat konto via wildcard-LIKE-pattern (?q=%term%).
        // POST nedan har inte denna policy — admin-flöde med egen yta.
        // Per CTO-rond 2026-05-13 F2-P9 + security-auditor Major-fynd.
        group.MapGet("/", async (
            IMediator mediator,
            int page = 1,
            int pageSize = 20,
            JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc,
            // ADR 0042 Beslut B — multi: upprepad query-string binds av
            // ASP.NET Core minimal API till string[].
            // ADR 0067 — dimensioner: ?occupationGroup= (ssyk-level-4/
            // yrkesgrupp, primärt yrke-filter) + ?municipality= (kommun) +
            // ?region=. Fas C2 (CTO-dom (e)): ?ssyk=-paramen BORTTAGEN —
            // obunden query-param ignoreras (200 OK, inget filter) tills
            // Fas E byter FE-picker till ?occupationGroup=.
            string[]? occupationGroup = null,
            string[]? municipality = null,
            string[]? region = null,
            // ADR 0067 Beslut 6 (Fas B2) — Klass 2: ?employmentType= (anställnings-
            // form) + ?worktimeExtent= (omfattning). Ortogonala IN-filter; upprepad
            // query-string binds till string[] (samma som dims ovan).
            string[]? employmentType = null,
            string[]? worktimeExtent = null,
            string? q = null,
            // ADR 0042 Beslut E — "ny sedan"-fönster (runtime-kontext).
            DateTimeOffset? since = null,
            // ADR 0060 amendment 2026-06-12 (Fas E2j) — commit-intent-gate:
            // ?commit=1 vid avsiktlig sökning (Enter/Sök/förslags-val/toolbar)
            // → auto-capture; utelämnad (live-förhandsvisning) → ingen capture.
            // Transient signal-param, ingår EJ i filter-identiteten.
            bool commit = false,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(
                new ListJobAdsQuery(
                    page, pageSize, sortBy,
                    OccupationGroup: occupationGroup,
                    Municipality: municipality,
                    Region: region,
                    EmploymentType: employmentType,
                    WorktimeExtent: worktimeExtent,
                    Q: q,
                    Since: since,
                    Commit: commit), ct);
            return Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.ListReadPolicy);

        // ADR 0042 Beslut C — typeahead C1 (lokal job_ads.Title ILIKE-prefix).
        // Egen SuggestPolicy (typeahead = 1 req/keystroke; least common
        // mechanism). Auth-gated via gruppen. DoS-floor (min prefix ≥2 +
        // Limit-cap) i ListJobAds/SuggestJobAdTermsQueryValidator.
        group.MapGet("/suggest", async (
            IMediator mediator,
            string prefix,
            int limit = 10,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new SuggestJobAdTermsQuery(prefix, limit), ct);
            return Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.SuggestPolicy);

        // ADR 0067 Beslut 4 (Fas E2c) — per-option facet-counts: concept-id →
        // antal aktiva annonser för EN dimension, med den facetterade
        // dimensionens listor exkluderade ur WHERE (ort-facetterna exkluderar
        // HELA ort-dimensionen — CTO VAL 4). Rå dict (ingen Total — talet ägs
        // av list-svarets PagedResult.TotalCount, SPOT). Egen FacetCountsPolicy
        // (30/10s/user — least common mechanism; facet-burst får inte svälta
        // list-RSC-refetcharna, CTO VAL 1 E2c). Cache-Control: private,
        // no-store (dynamiskt per filter + korpus + auth). dimension binds
        // case-insensitivt per namn; validatorn IsInEnum() stoppar numeriska
        // out-of-range-värden (?dimension=7) med rent 400.
        group.MapGet("/facet-counts", async (
            IMediator mediator, HttpContext http,
            FacetDimension dimension,
            string[]? occupationGroup = null,
            string[]? municipality = null,
            string[]? region = null,
            // ADR 0067 Beslut 6 (Fas B2) — Klass 2-filterkontext för facetten.
            string[]? employmentType = null,
            string[]? worktimeExtent = null,
            string? q = null,
            CancellationToken ct = default) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(
                new GetFacetCountsQuery(
                    dimension,
                    OccupationGroup: occupationGroup,
                    Municipality: municipality,
                    Region: region,
                    EmploymentType: employmentType,
                    WorktimeExtent: worktimeExtent,
                    Q: q), ct);
            return Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.FacetCountsPolicy);

        // ADR 0043 — picker-träd (Län + Yrkesområde→Yrke). concept-id
        // försvinner ur UI (Anticorruption Layer). Statisk referensdata →
        // ETag + Cache-Control: private (auth-gated; ALDRIG public/shared-
        // proxy — Web Cache Deception, MAP-3). 304 vid If-None-Match-match
        // så frontend slipper re-hämta ~300 KB per render.
        group.MapGet("/taxonomy", async (
            IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            var tree = await mediator.Send(new GetTaxonomyTreeQuery(), ct);
            var etag = TaxonomyETag(tree);

            http.Response.Headers.CacheControl = "private, max-age=3600";
            http.Response.Headers.ETag = etag;

            var inm = http.Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(inm) && inm == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            return Results.Ok(tree);
        })
        .RequireRateLimiting(RateLimitingExtensions.TaxonomyReadPolicy);

        // ADR 0043 — reverse-lookup (concept-id → namn) för redan-sparade
        // sökningar/valda chips. Okänt id → "Okänd kod (<id>)" (graceful,
        // aldrig 500). Cap i ResolveTaxonomyLabelsQueryValidator (= domänens
        // MaxConceptIds ×4 efter C1, ADR 0067 — fyra filter-dimensioner).
        // Cache-Control: private (varierar per ids, auth).
        group.MapGet("/taxonomy/labels", async (
            IMediator mediator, HttpContext http,
            string[]? ids = null, CancellationToken ct = default) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(
                new ResolveTaxonomyLabelsQuery(ids ?? []), ct);
            return Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.TaxonomyReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetJobAdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.ListReadPolicy);

        group.MapPost("/", async (
            CreateJobAdCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/job-ads/{result.Value}", new { id = result.Value })
                : Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Message,
                    statusCode: 400);
        });
    }

    // Deterministisk svag ETag = SHA256 över den logiska trädets JSON.
    // Trädet är invariant per deploy/snapshot-version → samma ETag tills
    // snapshot regenereras (då innehållet, och därmed hashen, ändras).
    // Svag (W/) — semantisk likvärdighet, inte byte-för-byte (serialiserings-
    // option-drift ska inte trigga onödig re-hämtning).
    private static string TaxonomyETag(TaxonomyTreeDto tree)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tree);
        var hash = SHA256.HashData(bytes);
        return $"W/\"{Convert.ToHexString(hash)[..32]}\"";
    }
}
