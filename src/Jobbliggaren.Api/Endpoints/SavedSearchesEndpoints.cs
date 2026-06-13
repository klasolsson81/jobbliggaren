using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.SavedSearches.Commands.CreateSavedSearch;
using Jobbliggaren.Application.SavedSearches.Commands.DeleteSavedSearch;
using Jobbliggaren.Application.SavedSearches.Commands.UpdateSavedSearch;
using Jobbliggaren.Application.SavedSearches.Queries.GetSavedSearch;
using Jobbliggaren.Application.SavedSearches.Queries.ListSavedSearches;
using Jobbliggaren.Application.SavedSearches.Queries.RunSavedSearch;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class SavedSearchesEndpoints
{
    public static void MapSavedSearchesEndpoints(this IEndpointRouteBuilder app)
    {
        // ADR 0005 / ADR 0039 — JobSeeker-scoped, auth-gated.
        var group = app.MapGroup("/api/v1/saved-searches")
            .WithTags("SavedSearches")
            .RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListSavedSearchesQuery(), ct);
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetSavedSearchQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/", async (
            CreateSavedSearchCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/saved-searches/{result.Value}", new { id = result.Value })
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        });

        group.MapPatch("/{id:guid}", async (
            Guid id, UpdateSavedSearchBody body, IMediator mediator, CancellationToken ct) =>
        {
            var command = new UpdateSavedSearchCommand(
                id, body.Name, body.NotificationEnabled,
                body.Criteria is null
                    ? null
                    : new SavedSearchCriteriaInput(
                        OccupationGroup: body.Criteria.OccupationGroup,
                        Municipality: body.Criteria.Municipality,
                        Region: body.Criteria.Region,
                        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 anställningsform + omfattning.
                        EmploymentType: body.Criteria.EmploymentType,
                        WorktimeExtent: body.Criteria.WorktimeExtent,
                        Q: body.Criteria.Q,
                        SortBy: body.Criteria.SortBy));
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        });

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteSavedSearchCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        });

        // run är den enda wildcard-LIKE-ytan här (samma sök som ListJobAds via
        // JobAdSearch) → samma multi-query-DoS-yta som JobAds GET-list.
        // ListReadPolicy appliceras därför specifikt på run (security-auditor
        // F2-P9-fynd 2026-05-13). GET-list/by-id är JobSeeker-scopade till en
        // handfull rader (ingen wildcard-LIKE) → ingen motsvarande DoS-yta,
        // medveten asymmetri mot JobAdsEndpoints (ej generell paritet).
        group.MapPost("/{id:guid}/run", async (
            Guid id, IMediator mediator, int page = 1, int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new RunSavedSearchQuery(id, page, pageSize), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.ListReadPolicy);
    }

    private sealed record UpdateSavedSearchBody(
        string? Name,
        bool? NotificationEnabled,
        UpdateSavedSearchCriteriaBody? Criteria);

    // ADR 0042 Beslut B — multi-värde-listor (JSON-array).
    // ADR 0067 Fas C2 (CTO-dom (e)/(f)): Ssyk UTGICK — OccupationGroup +
    // Municipality ersätter. Gammal klient som skickar "ssyk" får fältet
    // tyst ignorerat (System.Text.Json default) → SearchCriteria.Empty-400
    // om inget annat kriterium (fail-säkert, ingen tyst halvspara).
    private sealed record UpdateSavedSearchCriteriaBody(
        IReadOnlyList<string>? OccupationGroup,
        IReadOnlyList<string>? Municipality,
        IReadOnlyList<string>? Region,
        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 anställningsform + omfattning.
        IReadOnlyList<string>? EmploymentType,
        IReadOnlyList<string>? WorktimeExtent,
        string? Q,
        JobAdSortBy SortBy);
}
