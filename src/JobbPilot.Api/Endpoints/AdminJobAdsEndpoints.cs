using JobbPilot.Application.Common.Authorization;
using JobbPilot.Application.JobAds.Commands.RedactRecruiterPii;
using JobbPilot.Application.JobAds.Commands.SyncPlatsbankenSnapshot;
using Mediator;

namespace JobbPilot.Api.Endpoints;

/// <summary>
/// Admin-yta för JobAd-källor. F2-P8b (ADR 0032 §9 — admin-trigger för synkron
/// snapshot-import som smoke-test innan Hangfire-schedulering (P8c)). TD-73
/// prod-gating-batch (ADR 0032 §8 amendment 2026-05-13) lägger till
/// right-to-erasure-endpoint för rekryterar-PII (GDPR Art. 17).
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

        // GDPR Art. 17 right-to-erasure för rekryterar-PII i raw_payload
        // (ADR 0032 §8 amendment 2026-05-13). Email-only — Name defererad till
        // TD-75. Aggregerad audit-rad per request via IAuditableCommand.
        group.MapPost("/redact-recruiter-pii", async (
            RedactRecruiterPiiRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new RedactRecruiterPiiCommand(request.Identifier, request.Type);
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Ok(new RedactRecruiterPiiResponse(
                    RequestId: command.RequestId,
                    RowsAffected: result.Value))
                : Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Message,
                    statusCode: 400);
        });
    }
}

/// <summary>
/// Request-body för POST /api/v1/admin/job-ads/redact-recruiter-pii.
/// </summary>
public sealed record RedactRecruiterPiiRequest(
    string Identifier,
    RecruiterIdentifierType Type);

/// <summary>
/// Response-body för POST /api/v1/admin/job-ads/redact-recruiter-pii.
/// RowsAffected = antal JobAds där raw_payload null:ades.
/// RequestId = aggregateId för audit-raden (kan användas vid uppföljning).
/// </summary>
public sealed record RedactRecruiterPiiResponse(
    Guid RequestId,
    int RowsAffected);
