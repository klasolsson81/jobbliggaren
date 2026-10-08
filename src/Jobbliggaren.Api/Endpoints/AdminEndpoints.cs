using System.Globalization;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountOverview;
using Jobbliggaren.Application.Admin.Queries.GetAuditLogEntries;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class AdminEndpoints
{
    public const string SampledAtHeader = "X-Admin-Sampled-At";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/overview/accounts", async (IMediator mediator, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new GetAccountOverviewQuery(), ct);
            response.Headers[SampledAtHeader] = result.SampledAt.ToString("O", CultureInfo.InvariantCulture);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapGet("/audit-log", async (
            IMediator mediator,
            HttpResponse response,
            IDateTimeProvider clock,
            int page = 1,
            int pageSize = 50,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            Guid? userId = null,
            string? eventType = null,
            string? aggregateType = null,
            CancellationToken ct = default) =>
        {
            response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(
                new GetAuditLogEntriesQuery(page, pageSize, from, to, userId, eventType, aggregateType),
                ct);
            response.Headers[SampledAtHeader] = clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            return Results.Ok(result);
        });
    }
}
