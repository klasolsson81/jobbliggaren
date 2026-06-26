using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.RecentJobSearches.Commands.DeleteRecentSearch;
using Jobbliggaren.Application.RecentJobSearches.Queries.ListRecentSearches;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// ADR 0060 — RecentJobSearches (auto-fångade sökningar per JobSeeker).
/// Route-prefix <c>/api/v1/me/recent-searches</c> följer "my data"-konvention
/// från <see cref="MeEndpoints"/>. Auto-capture sker via pipeline-behavior på
/// ListJobAds-flöden; dessa endpoints exponerar bara läs + radera.
/// </summary>
public static class RecentSearchesEndpoints
{
    public static void MapRecentSearchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/recent-searches")
            .WithTags("RecentSearches")
            .RequireAuthorization();

        // ?includeCount=false skippar per-row JobAds-COUNT (cap=20 N+1) för
        // lättviktiga konsumenter — driver /oversikt-Sammanfattningens
        // "Senaste sökning"-rad utan att triggra slow ListJobAds-COUNT.
        // F6 P5 P4 svans-PR4 (2026-05-24, Klas perf-feedback /oversikt 7-10s).
        group.MapGet("/", async (
            IMediator mediator,
            CancellationToken ct,
            bool includeCount = true) =>
        {
            var result = await mediator.Send(new ListRecentSearchesQuery(includeCount), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteRecentSearchCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }
}
