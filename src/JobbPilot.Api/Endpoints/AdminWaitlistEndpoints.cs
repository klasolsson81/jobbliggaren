using JobbPilot.Application.Common.Authorization;
using JobbPilot.Application.Waitlist.Commands.ApproveWaitlistEntry;
using JobbPilot.Application.Waitlist.Commands.RejectWaitlistEntry;
using JobbPilot.Application.Waitlist.Queries.ListWaitlistEntries;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Api.Endpoints;

public static class AdminWaitlistEndpoints
{
    public static void MapAdminWaitlistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/waitlist")
            .WithTags("Admin/Waitlist")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/", async (
            IMediator mediator,
            string? status = null,
            CancellationToken ct = default) =>
        {
            var items = await mediator.Send(new ListWaitlistEntriesQuery(status), ct);
            return Results.Ok(items);
        });

        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            ApproveWaitlistPayload? payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ApproveWaitlistEntryCommand(id, payload?.ValidForDays), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToErrorResult(result.Error);
        });

        group.MapPost("/{id:guid}/reject", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RejectWaitlistEntryCommand(id), ct);
            return result.IsSuccess ? Results.NoContent() : ToErrorResult(result.Error);
        });
    }

    /// <summary>Body-payload för approve (valfri validForDays).</summary>
    public sealed record ApproveWaitlistPayload(int? ValidForDays);

    private static IResult ToErrorResult(DomainError error) => error.Code switch
    {
        "WaitlistEntry.NotFound" => Results.Problem(detail: error.Message, title: error.Code, statusCode: 404),
        "WaitlistEntry.NotPending" => Results.Problem(detail: error.Message, title: error.Code, statusCode: 409),
        _ => Results.Problem(detail: error.Message, title: error.Code, statusCode: 400),
    };
}
