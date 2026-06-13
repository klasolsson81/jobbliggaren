using Jobbliggaren.Application.Admin.Queries.GetAuditLogEntries;
using Jobbliggaren.Application.Common.Authorization;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/audit-log", async (
            IMediator mediator,
            int page = 1,
            int pageSize = 50,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            Guid? userId = null,
            string? eventType = null,
            string? aggregateType = null,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(
                new GetAuditLogEntriesQuery(page, pageSize, from, to, userId, eventType, aggregateType),
                ct);
            return Results.Ok(result);
        });
    }
}
