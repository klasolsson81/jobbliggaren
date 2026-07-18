using Hangfire;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// Admin-yta för företagsbevakningar. #544 (ADR 0090 D5) — engångs-backfill som tokeniserar
/// befintliga PLAINTEXT personnummer-formade <c>company_watches.organization_number</c>-rader till
/// HMAC vid vila (enskild-firma-följningar skapade via #455 före ändringen). dryRun default TRUE —
/// KÖRNINGEN ÄR KLAS-GRINDAD (STOPP-5, security-auditor B5): den destruktiva körningen skriver över
/// plaintext-personnummer IRREVERSIBELT (peppern är permanent/icke-roterbar, R1) — Klas ser
/// dry-run-deltat FÖRE accept. Samma fire-and-forget-mönster som <c>backfill-contact-scrub</c>;
/// idempotent via form (en redan tokeniserad rad är en fixpunkt). Engångs-operation, INTE i
/// RecurringJobRegistrar. Admin-auth krävs (gruppen RequireAuthorization).
/// </summary>
public static class AdminCompanyWatchesEndpoints
{
    public static void MapAdminCompanyWatchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/company-watches")
            .WithTags("Admin/CompanyWatches")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapPost("/backfill-orgnr-token",
            (IBackgroundJobClient backgroundJobs, bool dryRun = true) =>
        {
            var jobId = backgroundJobs.Enqueue<BackfillCompanyWatchOrgNrTokenJob>(
                j => j.RunAsync(dryRun, CancellationToken.None));
            return Results.Accepted(
                uri: null,
                value: new BackfillCompanyWatchOrgNrTokenResponse(JobId: jobId, DryRun: dryRun));
        });
    }
}

public sealed record BackfillCompanyWatchOrgNrTokenResponse(string JobId, bool DryRun);
