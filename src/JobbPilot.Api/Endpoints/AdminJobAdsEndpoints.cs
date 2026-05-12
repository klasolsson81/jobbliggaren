using JobbPilot.Application.Common.Authorization;
using JobbPilot.Application.JobAds.Commands.SyncPlatsbankenSnapshot;
using Mediator;

namespace JobbPilot.Api.Endpoints;

/// <summary>
/// Admin-yta för JobAd-källor. F2-P8b (ADR 0032 §9 — admin-trigger för synkron
/// snapshot-import som smoke-test innan Hangfire-schedulering (P8c).
/// </summary>
public static class AdminJobAdsEndpoints
{
    public static void MapAdminJobAdsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/job-ads")
            .WithTags("Admin/JobAds")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapPost("/sync/platsbanken", async (
            IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SyncPlatsbankenSnapshotCommand(), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Message,
                    statusCode: 400);
        });
    }
}
