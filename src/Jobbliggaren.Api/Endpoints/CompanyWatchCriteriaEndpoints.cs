using System.Security.Cryptography;
using System.Text.Json;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyWatches.Commands;
using Jobbliggaren.Application.CompanyWatches.Commands.CreateCompanyWatchCriterion;
using Jobbliggaren.Application.CompanyWatches.Commands.DeleteCompanyWatchCriterion;
using Jobbliggaren.Application.CompanyWatches.Commands.UpdateCompanyWatchCriterion;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCriterionAds;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionMatchMagnitude;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionReference;
using Jobbliggaren.Application.CompanyWatches.Queries.GetMyMatchingAdCountForCriterion;
using Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatchCriteria;
using Jobbliggaren.Application.CompanyWatches.Queries.PreviewCriterionMatchMagnitude;
using Jobbliggaren.Application.JobAds.Queries;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #560 PR-3 (CTO Fork G6) — criteria-based company watches: an OWN group under the /me/* "my
/// data" convention, sibling to <c>/api/v1/me/company-watches</c> and deliberately NOT nested
/// under it: <c>CompanyWatchCriterion</c> is a peer aggregate (the A1 seal keeps it strictly apart
/// from the org.nr follow), and nesting would re-conflate at the API boundary exactly what the
/// domain severed. All routes auth-gated; every handler is additionally owner-scoped via
/// <c>ICurrentUser</c> with the ADR 0031 cross-user probe (C-D10 — IDOR is this table's only real
/// attack surface).
/// </summary>
public static class CompanyWatchCriteriaEndpoints
{
    public static void MapCompanyWatchCriteriaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/company-watch-criteria")
            .WithTags("CompanyWatchCriteria")
            .RequireAuthorization();

        // "Mina bevakningar" (criteria) — a light per-user read, hard-capped at MaxPerUser rows →
        // MeListRead, NOT the browse policy (distinct cost profile, distinct bucket; CTO G4 note).
        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListCompanyWatchCriteriaQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // The SCB reference tree the picker renders (CTO Fork G2) — static per deploy, so the
        // taxonomy-endpoint mold applies verbatim: ETag + Cache-Control: private (auth-gated;
        // NEVER public/shared-proxy — Web Cache Deception, MAP-3), 304 on If-None-Match so the
        // picker skips re-fetching ~100 kB per open. No {id} in the route: "reference" cannot
        // collide with the {id:guid}-constrained siblings.
        group.MapGet("/reference", async (
            IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            var tree = await mediator.Send(new GetCriterionReferenceQuery(), ct);
            var etag = ReferenceETag(tree);

            http.Response.Headers.CacheControl = "private, max-age=3600";
            http.Response.Headers.ETag = etag;

            var inm = http.Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(inm) && inm == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            return Results.Ok(tree);
        }).RequireRateLimiting(RateLimitingExtensions.TaxonomyReadPolicy);

        // Create — the command binds straight from the body (CreateSavedSearchCommand parity):
        // { "criteria": { "sniCodes": [...], "municipalityCodes": [...] }, "label": "..." }.
        // Codes are LEAVES ONLY (the picker expands groups FE-side, Fork B1/G2). The pipeline
        // validator carries C-D12 (raw caps first) + per-axis existence; MaxPerUser is enforced
        // in the handler (C-D11) → 409 via the central kind-mapper.
        group.MapPost("/", async (
            CreateCompanyWatchCriterionCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Created(
                    $"/api/v1/me/company-watch-criteria/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // The register browse (the criterion "run", mirroring SavedSearch's /{id}/run split) —
        // TWO mediator sends composed into one response (§2.3: complex flows compose from
        // handlers): the page (whose PagedResult.TotalCount is a PAGINATION quantity, capped at
        // 2000 — never a magnitude) and the honest magnitude (Fork G3: own count, own ceiling,
        // exact under 10 000, "10 000+" when saturated). null → 404 for unknown AND cross-user
        // ids alike (never an existence oracle). The magnitude re-check catches the race where
        // the criterion is deleted between the two sends.
        group.MapGet("/{id:guid}/companies", async (
            Guid id, IMediator mediator, int page = 1, int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var companies = await mediator.Send(new BrowseCompaniesQuery(id, page, pageSize), ct);
            if (companies is null)
                return Results.NotFound();

            var magnitude = await mediator.Send(new GetCriterionMatchMagnitudeQuery(id), ct);
            if (magnitude is null)
                return Results.NotFound();

            return Results.Ok(new CompanyBrowseResponse(companies, magnitude));
        }).RequireRateLimiting(RateLimitingExtensions.CompanyBrowsePolicy);

        // #1559 — the criterion's ACTIVE ads (the companies it matches, and what they are hiring
        // for), mirroring the /companies split above: TWO mediator sends composed into one response
        // (§2.3). The page's PagedResult.TotalCount is a PAGINATION quantity and is
        // never rendered; the honest headline is the magnitude, with its OWN ceiling
        // (CriterionAdMagnitudeDto.Ceiling — the ad question, not the company one). null → 404 for
        // unknown AND cross-user ids alike. The magnitude re-check catches the race where the
        // criterion is deleted between the two sends.
        //
        // #1656 (b) — `onlyMatching` pages the ads that match the CALLER instead of the whole set.
        // The personal count rides along as `Matching` and is null when the caller did not ask: ADR
        // 0120's own corollary, and the reason the member is nullable rather than absent (the wire
        // shape must not vary with the filter). It is also what tells the surface which arm it is
        // in, since the filter is INERT (unfiltered list) for an unassessable caller and for a
        // criterion too broad to grade.
        group.MapGet("/{id:guid}/ads", async (
            Guid id, IMediator mediator, int page = 1, int pageSize = 20,
            bool onlyMatching = false, CancellationToken ct = default) =>
        {
            var ads = await mediator.Send(
                new BrowseCriterionAdsQuery(id, page, pageSize, onlyMatching), ct);
            if (ads is null)
                return Results.NotFound();

            var magnitude = await mediator.Send(new GetCriterionAdMagnitudeQuery(id), ct);
            if (magnitude is null)
                return Results.NotFound();

            MyMatchingAdCountDto? matching = null;
            if (onlyMatching)
            {
                matching = await mediator.Send(new GetMyMatchingAdCountForCriterionQuery(id), ct);
                if (matching is null)
                    return Results.NotFound();
            }

            return Results.Ok(new CriterionAdBrowseResponse(ads, magnitude, matching));
        }).RequireRateLimiting(RateLimitingExtensions.CompanyBrowsePolicy);

        // #1559 — the ad magnitude ALONE, for the criterion detail page, which renders the number and
        // links to /ads without reading a single ad. Deliberately not served by calling /ads with a
        // one-row page: that is the "count-only caller passes PageSize: 1 and reads TotalCount"
        // mechanism the browse port's own contract REVOKES, because under the pagination cap it
        // reports MaxPage x 1 instead of the truth. Same browse cost profile as its siblings, so the
        // same rate-limit bucket.
        //
        // (The port is named in that contract's docblock, not here: OrganizationNumberSurfacingGuard
        // fails the build on an Api source that NAMES a raw-org.nr-producing port, because naming one
        // is how an endpoint would inject it and bypass the handler's masking. The rule is a plain
        // name scan, so it catches a mention in prose too — deliberately, since a guard that tries to
        // tell prose from code is a guard with a hole in it.)
        //
        // #1656 (b) — it now COMPOSES a second send and answers BOTH ad questions at once: how many
        // active ads the criterion has, and how many of them match the caller. Two questions, two
        // DTOs, two doctrines (the first saturates at its ceiling, the second is exact or absent),
        // one response — the §2.3 composition this endpoint family already uses twice above.
        //
        // Deliberately NOT a fifth route. The four routes on this group share ONE rate-limit bucket
        // whose derivation is dated and security-auditor-owned; its own text says an ADDITIONAL call
        // on these pages spends a token off the same allowance and that the margin is bought back by
        // removing a call, not by raising the limit. Composing costs the detail page nothing.
        group.MapGet("/{id:guid}/ad-count", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var magnitude = await mediator.Send(new GetCriterionAdMagnitudeQuery(id), ct);
            if (magnitude is null)
                return Results.NotFound();

            var matching = await mediator.Send(new GetMyMatchingAdCountForCriterionQuery(id), ct);
            if (matching is null)
                return Results.NotFound();

            return Results.Ok(new CriterionAdCountResponse(magnitude, matching));
        }).RequireRateLimiting(RateLimitingExtensions.CompanyBrowsePolicy);

        // The picker's live magnitude preview over an UNSAVED criterion (Fork G3's second
        // consumer). POST-as-read (the /companies/lookup precedent — the predicate is a body,
        // never a URL). Same shared validation as create; Result carries the Domain's Validation
        // errors → 400 via the central mapper.
        group.MapPost("/preview-count", async (
            PreviewCriterionMatchMagnitudeQuery query, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(query, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.CriterionCountPreviewPolicy);

        // PATCH partial (Fork G6, UpdateSavedSearch parity): present Criteria = full predicate
        // replacement (the spec cannot be partially valid); present Label = rename (blank clears);
        // absent members untouched.
        group.MapPatch("/{id:guid}", async (
            Guid id, UpdateCriterionBody body, IMediator mediator, CancellationToken ct) =>
        {
            var command = new UpdateCompanyWatchCriterionCommand(
                id,
                body.Label,
                body.Criteria is null
                    ? null
                    : new CompanyWatchCriteriaInput(
                        body.Criteria.SniCodes, body.Criteria.MunicipalityCodes));
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // HARD delete (C-D8/G1 — the #782 template). Repeat delete → 404 (the row is gone),
        // unlike the idempotent soft-delete siblings.
        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteCompanyWatchCriterionCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }

    /// <summary>
    /// The composed browse response: the page and the honest magnitude, side by side — so the FE
    /// can never mistake the pagination count for the magnitude (they arrive as different members
    /// with different documented meanings).
    /// </summary>
    private sealed record CompanyBrowseResponse(
        PagedResult<CompanyBrowseDto> Companies,
        CriterionMatchMagnitudeDto Magnitude);

    /// <summary>
    /// #1559 — the composed AD browse response, the exact sibling of <see cref="CompanyBrowseResponse"/>
    /// and for the same reason: the page and the honest magnitude arrive as different members with
    /// different documented meanings, so the FE cannot mistake the capped pagination count for the
    /// number it renders.
    /// </summary>
    private sealed record CriterionAdBrowseResponse(
        PagedResult<JobAdDto> Ads,
        CriterionAdMagnitudeDto Magnitude,
        MyMatchingAdCountDto? Matching);

    /// <summary>
    /// #1656 (b) — the criterion's two AD numbers side by side: how many active ads exist, and how
    /// many of them match the caller. Separate members because they are separate questions with
    /// separate honesty rules — <see cref="CriterionAdMagnitudeDto"/> saturates and renders "10 000+",
    /// <see cref="MyMatchingAdCountDto"/> is exact or absent and never carries a "+". One shared type
    /// would let a surface render one where it means the other.
    /// </summary>
    private sealed record CriterionAdCountResponse(
        CriterionAdMagnitudeDto Ads,
        MyMatchingAdCountDto Matching);

    private sealed record UpdateCriterionBody(string? Label, UpdateCriterionCriteriaBody? Criteria);

    private sealed record UpdateCriterionCriteriaBody(
        IReadOnlyList<string>? SniCodes,
        IReadOnlyList<string>? MunicipalityCodes);

    // Deterministic weak ETag = SHA256 over the logical tree's JSON (the TaxonomyETag mold —
    // invariant per deploy since the datasets are embedded; W/ = semantic equivalence, so a
    // serializer-option drift never forces a spurious refetch).
    private static string ReferenceETag(CriterionReferenceDto tree)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tree);
        var hash = SHA256.HashData(bytes);
        return $"W/\"{Convert.ToHexString(hash)[..32]}\"";
    }
}
