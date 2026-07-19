using Hangfire;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// Admin-yta för CV-artefakter. #664 (#479 Low, GDPR Art. 5(1)(c)/25) — engångs-backfill som re-maskar
/// personnummer som ligger PLAINTEXT i <c>parsed_resumes.source_file_name</c> på rader importerade FÖRE
/// #465 (som lade maskningen på <c>ParsedResume.Create</c>). dryRun default TRUE — KÖRNINGEN ÄR
/// KLAS-GRINDAD (STOPP-5): den destruktiva körningen skriver över plaintext-personnummer IRREVERSIBELT —
/// Klas ser dry-run-deltat FÖRE accept. Samma fire-and-forget-mönster som
/// <see cref="AdminCompanyWatchesEndpoints"/>:s orgnr-token-backfill; idempotent via form (en redan
/// maskad rad är en no-op). Engångs-operation, INTE i RecurringJobRegistrar. Admin-auth krävs
/// (gruppen RequireAuthorization).
/// </summary>
public static class AdminResumesEndpoints
{
    public static void MapAdminResumesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/resumes")
            .WithTags("Admin/Resumes")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapPost("/backfill-source-filename-mask",
            (IBackgroundJobClient backgroundJobs, bool dryRun = true) =>
        {
            var jobId = backgroundJobs.Enqueue<BackfillParsedResumeSourceFileNameMaskJob>(
                j => j.RunAsync(dryRun, CancellationToken.None));
            return Results.Accepted(
                uri: null,
                value: new BackfillParsedResumeSourceFileNameMaskResponse(JobId: jobId, DryRun: dryRun));
        });
    }
}

public sealed record BackfillParsedResumeSourceFileNameMaskResponse(string JobId, bool DryRun);
