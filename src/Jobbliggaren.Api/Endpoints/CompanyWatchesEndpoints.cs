using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowBrandGroup;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompanyFromJobAd;
using Jobbliggaren.Application.CompanyWatches.Commands.MarkFollowedCompanyAdSeen;
using Jobbliggaren.Application.CompanyWatches.Commands.SetCompanyWatchFilter;
using Jobbliggaren.Application.CompanyWatches.Commands.UnfollowCompany;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusBatch;
using Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) + #455 — follow employers by org.nr, and from a job ad. Route prefix
/// <c>/api/v1/me/company-watches</c> ("my data" convention); the whole group is auth-gated.
///
/// <para>
/// <b>FORK D2 (surrogate resource) + #455 Approach A:</b> the raw org.nr NEVER appears in a URL path
/// (and thus never in an access-log) — a sole-prop org.nr can equal a personnummer, and a URL is logged
/// un-flagged by infra we don't control (ADR 0087 D8(c), CLAUDE.md §5). POST <c>/</c> takes org.nr in the
/// BODY; POST <c>/by-job-ad/{jobAdId}</c> takes the non-PII JobAdId and resolves org.nr server-side;
/// DELETE addresses the watch by its opaque <c>CompanyWatchId</c>. POST <c>/status</c> returns per-ad
/// follow-state (opaque <c>CompanyWatchId</c> + <c>Followable</c>, never org.nr).
/// </para>
/// </summary>
public static class CompanyWatchesEndpoints
{
    /// <summary>#455 follow-state batch request. <c>JobAdIds</c> is a page of ids (validator caps at 100).</summary>
    public sealed record CompanyWatchStatusBatchRequest(IReadOnlyList<Guid> JobAdIds);

    /// <summary>
    /// Bevakning F4a (#803) — the watch's full filter selection. The two geo lists are DISJOINT
    /// JobTech namespaces (municipality vs län concept-ids) and are unioned, never merged: a
    /// whole-län pick travels as a län id so län-only ads still notify. Nullable lists are tolerated
    /// on the wire and normalised to empty (a client that omits an axis means "nothing selected"),
    /// but a null list is never handed to the domain. All three empty = clear the filter.
    /// </summary>
    public sealed record SetWatchFilterRequest(
        IReadOnlyList<string>? Municipalities,
        IReadOnlyList<string>? Regions,
        bool OnlyMatched,
        // #551 PR-B D6 — the remote/distans axis (default false = not selected).
        bool Remote = false);

    public static void MapCompanyWatchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/company-watches")
            .WithTags("CompanyWatches")
            .RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListCompanyWatchesQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapPost("/", async (
            FollowCompanyCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            // Location bears the CompanyWatchId (Guid), never the org.nr (D8(c)).
            return result.IsSuccess
                ? Results.Created($"/api/v1/me/company-watches/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // #311 PR-5 (ADR 0087 D4) — follow a CURATED brand group by slug. The slug travels in the BODY
        // (parity POST /: one follow-request shape; the slug is non-PII public reference data, so the
        // body is a consistency choice, not a D8(c) requirement). Same 201 { id } shape; unfollow rides
        // the existing DELETE /{id}. An unknown slug is a 404 (DomainError.NotFound), a malformed one a
        // 400 (validator).
        group.MapPost("/brand-group", async (
            FollowBrandGroupCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/me/company-watches/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // #455 (ADR 0087 D8(c) Approach A) — follow FROM a job ad. JobAdId (non-PII) in the path; the
        // handler resolves org.nr server-side. Same 201 { id } shape as POST / — the org.nr never wires.
        group.MapPost("/by-job-ad/{jobAdId:guid}", async (
            Guid jobAdId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new FollowCompanyFromJobAdCommand(jobAdId), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/me/company-watches/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // #455 — per-ad follow-state overlay (auth-gated, per-user-private; NOT anon-tolerant like the
        // ADR 0063 status batch). POST carries the id list in the body; response bears no org.nr.
        group.MapPost("/status", async (
            CompanyWatchStatusBatchRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new GetCompanyWatchStatusBatchQuery(body.JobAdIds ?? []), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapDelete("/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UnfollowCompanyCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // Bevakning F4a (#803, CTO Q1=A) — replace this watch's notification filter. ONE route, not a
        // PUT/DELETE pair: the write is a full-replace of the user's selection, and an empty selection
        // is a VALUE of that ("no filter"), which the handler maps to ClearFilter. A separate DELETE
        // would push transport logic into the FE ("which route does an empty form call?") and create an
        // ordering hazard on a rapid clear->set. The watch is addressed by its opaque CompanyWatchId —
        // no org.nr in the path, ever (D8). MeWrite: a deliberate, low-frequency user write.
        group.MapPut("/{id:guid}/filter", async (
            Guid id, SetWatchFilterRequest request, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new SetCompanyWatchFilterCommand(
                    id,
                    request.Municipalities ?? [],
                    request.Regions ?? [],
                    request.OnlyMatched,
                    request.Remote),
                ct);

            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // #453 (ADR 0087 D5-addendum) — mark a followed-company ad SEEN in-app so the follow-digest
        // suppresses the redundant email ("aldrig mejla något jag sett i appen"). JobAdId (non-PII) in
        // the path; the handler stamps only the AUTHENTICATED user's own Pending hits (UserId from
        // ICurrentUser, never the wire — IDOR-safe, §5/§12). 204 on Success (also for a benign no-op:
        // no hit for this ad is indistinguishable from one that existed — never leaks follow-existence).
        // Dedicated FollowSeenMarkPolicy (NOT MeWrite): this AUTO-fires on every ad-detail open, so a
        // shared write-bucket would let it starve the user's deliberate Save/Follow (bulkhead; CTO
        // 2026-07-02 (b)).
        group.MapPost("/ad-hits/{jobAdId:guid}/seen", async (
            Guid jobAdId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new MarkFollowedCompanyAdSeenCommand(jobAdId), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.FollowSeenMarkPolicy);
    }
}
