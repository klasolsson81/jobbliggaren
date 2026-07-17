using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview;
using Jobbliggaren.Application.Matching.Commands.MarkMatchesSeen;
using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;
using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;
using Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;
using Jobbliggaren.Application.Matching.Queries.GetMyMatches;
using Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// F4-13 (ADR 0076 Decision 5, ADR 0063) — page-scoped match-tag batch-overlay on the
/// /jobb list. A dedicated batch port (parity <c>POST /me/job-ad-status</c>), kept in its
/// OWN file (SoC) — match is an orthogonal read concern from saved/applied status.
/// `JobAdDto` / `IJobAdSearchQuery` stay untouched (Decision 5).
/// </summary>
public static class MeJobAdMatchEndpoints
{
    // #300 PR-5a (ADR 0084 §A) — IncludeRelated (default false) is the "Visa relaterade också"
    // toggle on the wire: when true the page overlay broadens the occupation gate to exact ∪
    // related so related-occupation ads earn the Related tag. FE (PR-5b) maps ?relaterade=on.
    public sealed record JobAdMatchBatchRequest(
        IReadOnlyList<Guid> JobAdIds, bool IncludeRelated = false);

    // Epik #526 (ADR 0089) — utkastet för live sök-preview-räknaren i matchnings-setup-modalen.
    // Fyra sökbara facetter (INGA kompetenser — skills är kvalitet, ingen Platsbanken-sökfacett;
    // se GetMatchCountPreviewQuery). Nullable optional-fält → handlern normaliserar null → [].
    public sealed record MatchCountPreviewRequest(
        IReadOnlyList<string>? OccupationGroups,
        IReadOnlyList<string>? Regions,
        IReadOnlyList<string>? Municipalities,
        IReadOnlyList<string>? EmploymentTypes);

    public static void MapMeJobAdMatchEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous-tolerant (parity the status batch, ADR 0063 §Kontext): the handler
        // returns an empty map without a UserId, and for an authenticated user without a
        // stated occupation — no 401 friction on the public search page. NOT
        // `.RequireAuthorization()`-gated. Own dual-partition rate-limit policy
        // (JobAdMatchBatchPolicy) — a separate bucket from the status batch so the two
        // overlays a page render fires do not share a budget (bulkhead, Nygard); the ip:
        // fallback is the load-bearing DoS guard for the anonymous surface.
        app.MapPost("/api/v1/me/job-ad-match-tags", async (
                JobAdMatchBatchRequest body, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(
                    new GetJobAdMatchBatchQuery(body.JobAdIds ?? [], body.IncludeRelated), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireRateLimiting(RateLimitingExtensions.JobAdMatchBatchPolicy);

        // F4-16 (ADR 0076 Amendment (b) §5, ADR 0053 Beslut 5 amendment) — single-ad match
        // detail for the job modal (grade + matched/missing per dimension). UNLIKE the
        // anonymous-tolerant batch overlay above, this is `.RequireAuthorization()`-gated:
        // the modal is auth-gated and the FE only fetches this for a logged-in user (the
        // guest modal renders no match section), so there is no anonymous DoS surface to
        // protect — a user-partitioned read bucket (MeListReadPolicy) suffices, and the
        // DEK-reading handler is never reached anonymously. The handler still returns null
        // for an absent UserId as defence-in-depth. A 200 with `null` body means "no match
        // section" (the FE renders nothing); a missing ad → 404 (NotFoundException).
        // #885: an ERASED ad → 410 Gone with the same neutral body GET /api/v1/job-ads/{id} emits
        // for that ad — this surface only decorates a page that has already decided, so it must
        // not confirm the row's existence after that page said Gone. The status comes from the
        // handler's DomainError.Kind through the central mapper (CLAUDE.md §3 — never a
        // per-endpoint Code-string match).
        app.MapGet("/api/v1/me/job-ad-match-tags/{jobAdId:guid}", async (
                Guid jobAdId, IMediator mediator,
                // #300 PR-5a (ADR 0084 §A) — optional ?includeRelated=true grades a related-
                // occupation ad as Related in the modal (consistent with the page overlay toggle).
                // Optional trailing param → ct gets = default so the optional ordering is valid
                // (parity JobAdsEndpoints list). FE PR-5b maps the public ?relaterade=on.
                bool includeRelated = false, CancellationToken ct = default) =>
            {
                var result = await mediator.Send(
                    new GetJobAdMatchDetailQuery(jobAdId, includeRelated), ct);
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : result.Error.ToProblemResult();
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // ADR 0079 STEG 6 — Översikts live-notis-siffra ("Det finns X jobb som matchar din
        // profil"). Per-användar grad-filtrerad count (Bra + Stark) över hela den aktiva
        // korpusen, DEK-fri, ingen Worker. Auth-gated (parity match-detail ovan): notisen
        // visas bara för inloggad användare med angivet yrke; en ny användare utan yrke får
        // honest 0 (SSYK-gate i handlern). MeListReadPolicy (per-användar read-bucket). 200
        // { count: int }; aldrig en fejkad mock-siffra.
        app.MapGet("/api/v1/me/match-count", async (
                IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetMyMatchCountQuery(), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // Epik #526 (ADR 0089) — LIVE sök-preview-räknare för matchnings-setup-modalen: hur många
        // aktiva annonser matchar ett UTKAST av sök-facetter (yrke/ort/anställningsform) medan
        // användaren fyller i, debouncat ~400 ms klient-side. Ren sök-count (IJobAdSearchQuery
        // .CountAsync — ingen grad, ingen profil, ingen per-användar-data), per konstruktion lika
        // med den länkade /jobb-sökningens TotalCount. POST (ej GET): utkastet är en komplex body
        // (flera listor), men semantiskt en what-if-beräkning som INTE persisterar något. Auth-
        // gated: räknar bara den publika korpusen → auth är ren abuse-/DoS-grind (ingen
        // cross-user-läcka möjlig). Egen bucket (MatchCountPreviewPolicy) — får inte dela budget
        // med MeListRead som /oversikt redan fläktar ut. 200 { count: int }.
        app.MapPost("/api/v1/me/match-count-preview", async (
                MatchCountPreviewRequest body, IMediator mediator, CancellationToken ct) =>
            {
                // Named args: Regions/Municipalities ligger bredvid varandra i signaturen
                // (samma tyst-fel-fälla som JobAdFilterCriteria tvingar named args för).
                var result = await mediator.Send(new GetMatchCountPreviewQuery(
                    OccupationGroups: body.OccupationGroups ?? [],
                    Regions: body.Regions ?? [],
                    Municipalities: body.Municipalities ?? [],
                    EmploymentTypes: body.EmploymentTypes ?? []), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MatchCountPreviewPolicy);

        // ADR 0080 Vag 4 PR-5 — Översikts "Nya matchningar"-räknare (bakgrundsmatchningar nya
        // sedan senaste besök, UserJobAdMatch.CreatedAt > LastSeenMatchesAt). Ersätter STEG 6:s
        // mock "i dag"-rad. Auth-gated, MeListRead. 200 { count }.
        // #273: the count is intentionally UNCAPPED (true new-match total) and may exceed the
        // 50-row cap of GET /me/matches — see GetMyNewMatchCountQueryHandler for the contract.
        app.MapGet("/api/v1/me/new-match-count", async (
                IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetMyNewMatchCountQuery(), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // ADR 0080 Vag 4 PR-5 — den dedikerade "Mina matchningar"-vyn: användarens persisterade
        // bakgrundsmatchningar (Good/Strong/Top) joinade till annonsens publika detaljer + IsNew.
        // Auth-gated, MeListRead. 200 [ MatchListItemDto ].
        app.MapGet("/api/v1/me/matches", async (
                IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetMyMatchesQuery(), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // ADR 0080 Vag 4 PR-5 — markera matchningarna sedda (avancera last_seen_matches_at).
        // Anropas av FE när den dedikerade vyn ÖPPNAS (Klas-val: "views the matches surface").
        // Body { seenThrough } = max CreatedAt av matchningarna FE faktiskt visade (#477 Low —
        // vattenmärket sätts dit, inte klock-nu). Nullbar/tom body (tom lista / deploy-skew från
        // äldre FE) → handlern faller tillbaka på klock-nu. Auth-gated, MeWrite. 204 / 400.
        app.MapPost("/api/v1/me/matches/seen", async (
                MarkMatchesSeenRequest? body, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new MarkMatchesSeenCommand(body?.SeenThrough), ct);
                return result.IsSuccess
                    ? Results.NoContent()
                    : result.Error.ToProblemResult();
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }
}

/// <summary>
/// Body for <c>POST /api/v1/me/matches/seen</c> (#477 Low). <c>SeenThrough</c> = the max
/// <c>CreatedAt</c> of the matches the FE actually rendered; nullable so an empty body (empty
/// match list / deploy-skew) is allowed and falls back to clock-now in the handler.
/// </summary>
internal sealed record MarkMatchesSeenRequest(DateTimeOffset? SeenThrough);
