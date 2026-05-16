using JobbPilot.Application.Common.Authorization;
using JobbPilot.Application.JobAds.Commands.RedactRecruiterPii;
using Mediator;

namespace JobbPilot.Api.Endpoints;

/// <summary>
/// Admin-yta för JobAd-källor. Snapshot-trigger-endpointen är avvecklad
/// (ADR 0032 §9-amendment 2026-05-16, senior-cto-advisor X4): den körde
/// snapshot synkront i HTTP-requesten (ALB-timeout) och dubblerade
/// Hangfire-dashboardens "Trigger now". Snapshot körs nu enbart via
/// recurring-jobbet <c>sync-platsbanken-snapshot</c> i Worker. TD-73
/// prod-gating-batch (ADR 0032 §8 amendment 2026-05-13) behåller
/// right-to-erasure-endpoint för rekryterar-PII (GDPR Art. 17).
/// </summary>
public static class AdminJobAdsEndpoints
{
    public static void MapAdminJobAdsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/job-ads")
            .WithTags("Admin/JobAds")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        // Avvecklad 2026-05-16 (ADR 0032 §9-amendment, senior-cto-advisor X4).
        // Endpointen körde snapshot synkront i requesten → ALB-timeout vid
        // ~47k upserts, och dubblerade Hangfire-dashboardens "Trigger now".
        // Snapshot körs nu enbart via recurring-jobbet sync-platsbanken-stream/
        // -snapshot i Worker. 410 Gone behålls (i stället för borttagen route)
        // så operatörer med äldre runbook får tydlig anvisning. Admin-auth
        // krävs fortfarande (gruppen RequireAuthorization).
        group.MapPost("/sync/platsbanken", () =>
            Results.Problem(
                title: "Endpointen är avvecklad",
                detail: "Manuell snapshot-trigger sker via Hangfire-dashboarden: "
                    + "kör recurring-jobbet sync-platsbanken-snapshot med Trigger now. "
                    + "Snapshot körs annars automatiskt enligt schema (02:00 UTC).",
                statusCode: StatusCodes.Status410Gone));

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
