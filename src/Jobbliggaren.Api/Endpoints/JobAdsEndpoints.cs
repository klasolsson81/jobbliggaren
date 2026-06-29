using System.Security.Cryptography;
using System.Text.Json;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.CreateJobAd;
using Jobbliggaren.Application.JobAds.Queries.GetFacetCounts;
using Jobbliggaren.Application.JobAds.Queries.GetJobAd;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.Matching.Queries.ResolveSkillLabels;
using Jobbliggaren.Application.Matching.Queries.SearchSkills;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

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
            // F4-14 (ADR 0076) — read-side sort-ytan binds case-insensitivt per
            // namn till ListJobAdsSort (5 rena + MatchDesc = "Sortera efter
            // matchning"). Domän-enumen JobAdSortBy hålls match-ren; query:n
            // härleder SortBy/SortByMatch. Validatorn IsInEnum() stoppar
            // out-of-range numeriska värden med rent 400.
            ListJobAdsSort sortBy = ListJobAdsSort.PublishedAtDesc,
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
            // ADR 0060 amendment 2026-06-12 (Fas E2j) — commit-intent-gate:
            // ?commit=1 vid avsiktlig sökning (Enter/Sök/förslags-val/toolbar)
            // → auto-capture; utelämnad (live-förhandsvisning) → ingen capture.
            // Transient signal-param, ingår EJ i filter-identiteten.
            bool commit = false,
            // ADR 0079 STEG 5 — grad-filtret ("Matchning"-toggeln). Upprepad query-
            // string ?matchGrades=Strong&matchGrades=Good binds till MatchGrade[] per
            // NAMN (enum serialiseras by-name; ej svenska, ej komma-separerat — wire-
            // stabilt, i18n bor aldrig i URL:en). Ogiltigt namn → 400 vid binding;
            // Topp → bunden men avvisas av validatorn (G3-OPT-A). Runtime-kontext —
            // ingår EJ i filter-identiteten/recent-search-hashen.
            MatchGrade[]? matchGrades = null,
            // #300 PR-5a (ADR 0084 §A) — "Visa relaterade också"-toggeln. ?includeRelated=true
            // breddar yrkes-gaten exakt → exakt ∪ related så ett Related-grad-filter / match-sort
            // rankar related-annonser på Related-rungen. Runtime-kontext (paritet matchGrades):
            // ingår EJ i filter-identiteten/recent-search. Default false = beteende-inert. FE
            // (PR-5b) mappar det publika ?relaterade=on hit.
            bool includeRelated = false,
            // #383 (CTO-bind cto-7f3a9c2e1b4d8a6f, Approach B) — status-facetterna
            // (sparade/ansökta/dölj ansökta). Per-användar runtime-kontext (paritet
            // matchGrades/includeRelated): ingår EJ i filter-identiteten/recent-search.
            // savedOnly ∨ appliedOnly = OR (union); appliedOnly ∧ hideApplied → 400
            // (validator-mutex). Default false = ingen status-gallring. ASP.NET bool-
            // binding tar "true" (ej "1"); FE skickar "true". FE mappar de svenska
            // rutt-värdena ?sparade=on/?ansokta=on/?doljAnsokta=on hit.
            bool savedOnly = false,
            bool appliedOnly = false,
            bool hideApplied = false,
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
                    Commit: commit,
                    MatchGrades: matchGrades,
                    IncludeRelated: includeRelated,
                    SavedOnly: savedOnly,
                    AppliedOnly: appliedOnly,
                    HideApplied: hideApplied), ct);
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

        // ADR 0079 STEG 3 PR-C — skill typeahead for the editable match-skill chips' "add"
        // affordance. The skill taxonomy is a flat ~20k-concept vocabulary with no
        // browsable hierarchy (unlike the occupation tree above), so the FE searches
        // server-side. Non-PII reference data (taxonomy labels); a blank/too-short q → [].
        // Cache-Control: private, no-store (varies per keystroke). Rate-limited (mirrors
        // the sibling taxonomy reads).
        group.MapGet("/taxonomy/skills/search", async (
            IMediator mediator, HttpContext http,
            string? q = null, CancellationToken ct = default) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new SearchSkillsQuery(q ?? string.Empty), ct);
            return Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.TaxonomyReadPolicy);

        // ADR 0079 STEG 3 PR-C — reverse-lookup (concept-id → canonical label) for the
        // saved skill chips' cold-load display (the skill analog of /taxonomy/labels): the
        // flat skill vocabulary is never shipped as a tree, so the settings page resolves
        // its stored PreferredSkills concept-ids to names here. Unknown ids drop. Non-PII.
        // Cache-Control: private, no-store (varies per ids). Rate-limited.
        group.MapGet("/taxonomy/skills/labels", async (
            IMediator mediator, HttpContext http,
            string[]? ids = null, CancellationToken ct = default) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new ResolveSkillLabelsQuery(ids ?? []), ct);
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
                : result.Error.ToProblemResult();
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
