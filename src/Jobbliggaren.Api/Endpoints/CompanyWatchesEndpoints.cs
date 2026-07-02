using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompanyFromJobAd;
using Jobbliggaren.Application.CompanyWatches.Commands.MarkFollowedCompanyAdSeen;
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
