using Hangfire;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.JobAds.Commands.RedactRecruiterPii;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdRequirements;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// Admin-yta för JobAd-källor. Snapshot-trigger-endpointen är avvecklad
/// (ADR 0032 §9-amendment 2026-05-16, senior-cto-advisor X4): den körde
/// snapshot synkront i HTTP-requesten (ALB-timeout). Snapshot körs nu enbart
/// via recurring-jobbet <c>sync-platsbanken-snapshot</c> i Worker (schema
/// 02:00 UTC). Ingen Hangfire-dashboard är exponerad — manuell ad-hoc-körning
/// kräver operatörsåtgärd via AWS (TD-83). TD-73 prod-gating-batch (ADR 0032
/// §8 amendment 2026-05-13) behåller right-to-erasure-endpoint för
/// rekryterar-PII (GDPR Art. 17).
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
        // ~47k upserts. Snapshot körs nu enbart via recurring-jobbet
        // sync-platsbanken-snapshot i Worker (schema 02:00 UTC). Ingen
        // Hangfire-dashboard är exponerad (Worker är headless) — ad-hoc-körning
        // kräver operatörsåtgärd via AWS (TD-83). 410 Gone behålls (i stället
        // för borttagen route) så operatörer med äldre runbook får tydlig
        // anvisning. Admin-auth krävs fortfarande (gruppen RequireAuthorization).
        group.MapPost("/sync/platsbanken", () =>
            Results.Problem(
                title: "Endpointen är avvecklad",
                detail: "Snapshot-import körs av det schemalagda jobbet "
                    + "sync-platsbanken-snapshot (dagligen 02:00 UTC). Ad-hoc-körning "
                    + "kräver operatörsåtgärd via AWS — ingen publik trigger-yta finns.",
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

        // STEG 6 (2026-05-24) — engångs-backfill av ssyk_concept_id för JobAds
        // vars raw_payload saknar occupation-key (pre-2026-05-20-fix). Enqueue:as
        // som Hangfire fire-and-forget mot Worker-processens HangfireServer (samma
        // postgres-storage). Api returnerar 202 Accepted + jobId omedelbart;
        // körningen tar ~2h vid default-throttle. INTE registrerad som cron —
        // engångs-operation, idempotent restart-vänlig via NULL-filter.
        //
        // Concurrency-skydd: Application-jobbet enqueue:as direkt (utan Worker-
        // wrapper-DisableConcurrentExecution) eftersom Api inte refererar Worker-
        // projektet (Clean Arch). Operativ disciplin: Klas triggar endast EN gång
        // per körnings-fönster (UI-knappen är manuell). Vid race blir worst-case
        // dubbla Hangfire-jobs som båda iterar NULL-filtret — UNIQUE-index +
        // UpdateFromSource-idempotens gör race till no-op-overhead, inte korruption.
        // architect-rond 2026-05-24 (sub-decision från CC vid Api-discovery-gap).
        group.MapPost("/backfill-ssyk", (IBackgroundJobClient backgroundJobs) =>
        {
            var jobId = backgroundJobs.Enqueue<BackfillJobAdSsykJob>(
                j => j.RunAsync(CancellationToken.None));
            return Results.Accepted(
                uri: null,
                value: new BackfillSsykResponse(JobId: jobId));
        });

        // Fas B2 (2026-06-08, ADR 0067 Beslut 2) — engångs-backfill av Klass 2-
        // kolumnerna (employment_type_concept_id + worktime_extent_concept_id) för
        // JobAds vars raw_payload saknar dessa keys (alla rader importerade före
        // B2:s JobTechHit-POCO-tillägg → 100% av tabellen tills körningen skett).
        // Samma fire-and-forget-mönster som backfill-ssyk: enqueue:as direkt mot
        // Worker-processens HangfireServer, Api returnerar 202 + jobId omedelbart.
        // Per-ID-refetch re-skriver hela raw_payload → båda Klass 2-kolumnerna
        // populeras. Idempotent restart-vänlig via NULL-filter. Engångs-operation,
        // INTE i RecurringJobRegistrar. Re-ingest-körningen är Klas-GO-grindad
        // (ADR 0067 Beslut 2 — kolumnerna NULL tills körd).
        group.MapPost("/backfill-klass2", (IBackgroundJobClient backgroundJobs) =>
        {
            var jobId = backgroundJobs.Enqueue<BackfillJobAdKlass2Job>(
                j => j.RunAsync(CancellationToken.None));
            return Results.Accepted(
                uri: null,
                value: new BackfillKlass2Response(JobId: jobId));
        });

        // Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) — engångs-backfill av den
        // deterministiska keyword/skill-extraktionen (extracted_terms) för JobAds
        // importerade före F4-4. Till skillnad mot ssyk/Klass2 är detta en LOKAL
        // re-projektion: INGEN JobTech-refetch (title/description finns redan) → ingen
        // throttle, betydligt snabbare. Samma fire-and-forget-mönster: enqueue:as
        // direkt mot Worker-processens HangfireServer, Api returnerar 202 + jobId.
        // Idempotent restart-vänlig via extracted_lexemes IS NULL-filter. Engångs-
        // operation, INTE i RecurringJobRegistrar.
        group.MapPost("/backfill-extraction", (IBackgroundJobClient backgroundJobs) =>
        {
            var jobId = backgroundJobs.Enqueue<BackfillJobAdExtractedTermsJob>(
                j => j.RunAsync(CancellationToken.None));
            return Results.Accepted(
                uri: null,
                value: new BackfillExtractionResponse(JobId: jobId));
        });

        // Fas 4 STEG 4b (F4-4b, ADR 0071/0074/0075) — engångs-re-ingest av
        // arbetsgivar-kraven (must_have/nice_to_have-skills → Requirement-termer) för
        // JobAds vars raw_payload saknar must_have (alla rader importerade före F4-4b:s
        // POCO-expansion → 100% av tabellen tills körningen skett). Till SKILLNAD mot
        // backfill-extraction (lokal re-projektion) går detta via JobTech per-ID-refetch
        // (paritet ssyk/Klass2) — refetch re-skriver raw_payload → must_have landar +
        // ingest-hooken kör full extraktion (Requirement + keyword/skill → SUBSUMERAR
        // backfill-extraction). Samma fire-and-forget-mönster: enqueue:as direkt mot
        // Worker-processens HangfireServer, Api returnerar 202 + jobId. Idempotent
        // restart-vänlig via must_have-nyckel-filtret. Engångs-operation, INTE i
        // RecurringJobRegistrar. Re-ingest-körningen är Klas-GO-grindad (paritet Klass2).
        group.MapPost("/backfill-requirements", (IBackgroundJobClient backgroundJobs) =>
        {
            var jobId = backgroundJobs.Enqueue<BackfillJobAdRequirementsJob>(
                j => j.RunAsync(CancellationToken.None));
            return Results.Accepted(
                uri: null,
                value: new BackfillRequirementsResponse(JobId: jobId));
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

/// <summary>
/// Response-body för POST /api/v1/admin/job-ads/backfill-ssyk.
/// JobId = Hangfire-jobb-id (kan inspekteras via Hangfire-storage eller CloudWatch
/// /aws/ecs/jobbliggaren-dev/worker-loggen för progress/completion).
/// </summary>
public sealed record BackfillSsykResponse(string JobId);

/// <summary>
/// Response-body för POST /api/v1/admin/job-ads/backfill-klass2 (Fas B2).
/// JobId = Hangfire-jobb-id (inspekteras via Hangfire-storage / Worker-loggen
/// för progress/completion).
/// </summary>
public sealed record BackfillKlass2Response(string JobId);

/// <summary>
/// Response-body för POST /api/v1/admin/job-ads/backfill-extraction (F4-4).
/// JobId = Hangfire-jobb-id (inspekteras via Hangfire-storage / Worker-loggen).
/// </summary>
public sealed record BackfillExtractionResponse(string JobId);

/// <summary>
/// Response-body för POST /api/v1/admin/job-ads/backfill-requirements (F4-4b).
/// JobId = Hangfire-jobb-id (inspekteras via Hangfire-storage / Worker-loggen).
/// </summary>
public sealed record BackfillRequirementsResponse(string JobId);
