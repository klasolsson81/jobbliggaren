using Hangfire;
using Jobbliggaren.Api.Common;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
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
/// kräver operatörsåtgärd via AWS (TD-83).
///
/// <para>
/// <b>#842:</b> the right-to-erasure route for recruiter PII is
/// <see cref="EraseRecruiterAdsCommand"/> (ADR 0106 Tier B); PR1's 501 containment is lifted here.
/// It removes the whole ad record and blocks its re-import.
/// </para>
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

        // GDPR Art. 17 — recruiter-PII erasure (#842, ADR 0106 Tier B). PR1's 501 is lifted.
        //
        // The route keeps its address (/redact-recruiter-pii) so ADR 0024's cascade registry and
        // older runbooks still land somewhere real. The CONTRACT (EraseRecruiterAdsCommand /
        // EraseRecruiterAdsResponse):
        //
        //   * dryRun: true   → what WOULD be erased. Writes nothing. `matches` carries the ADS
        //                      themselves (id, title, company, matchedChannel, matchedExcerpt) plus
        //                      `matchedRecentSearchTerms` — the operator reviews those, not a count.
        //   * dryRun: false  → requires `confirmedJobAdIds`: the LIST of ids the operator reviewed
        //                      in the dry run. NOT a count (a count cannot be reviewed — see
        //                      EraseRecruiterAdsCommand.ConfirmedJobAdIds). Only confirmed ads are
        //                      erased; a confirmed id that no longer matches refuses the WHOLE
        //                      request with 409 and destroys nothing. That is what makes the dry run
        //                      mandatory in CODE rather than in a runbook sentence.
        //   * the reply      → an explicit `outcome` (NoMatchInSearchableSurfaces | DryRun |
        //                      AdsErased | CascadeErasedOnly | NothingErased), per-surface `matched`
        //                      vs `erased` counts whose GAP is itself a disclosure, `erasedExternalIds`,
        //                      and `couldNotSearch` — required on EVERY outcome, naming the
        //                      DEK-encrypted columns we hold and cannot scan. Art. 12(3) asks what we
        //                      DID, and a bare rowsAffected cannot say.
        //
        // Rejected requests are audited too (IAuditableCommand.AuditFailures) — a refused rights
        // request that leaves no trace is its own Art. 12(3) exposure.
        //
        // Admin auth applies at the group level, and AdminAuthorizationBehavior re-checks it on
        // IAdminRequest (defense in depth).
        group.MapPost("/redact-recruiter-pii", async (
            EraseRecruiterAdsRequest request, IMediator mediator, CancellationToken ct) =>
        {
            // RequestId is minted here, not accepted from the caller: it is the audit row's
            // aggregate id, and a client-supplied one would let two different requests collide in
            // the accountability record.
            var command = new EraseRecruiterAdsCommand(
                RequestId: Guid.NewGuid(),
                Identifier: request.Identifier,
                DryRun: request.DryRun,
                ConfirmedJobAdIds: request.ConfirmedJobAdIds);

            var result = await mediator.Send(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemResult();
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
/// Request-body för POST /api/v1/admin/job-ads/redact-recruiter-pii (GDPR Art. 17, #842).
/// </summary>
/// <param name="Identifier">
/// The recruiter's email, phone number OR name — one free-text field, no type discriminator. See
/// <see cref="EraseRecruiterAdsCommand"/> for why there is no discriminator.
/// </param>
/// <param name="DryRun">
/// True ⇒ report what would be erased and write nothing. <b>Run this first. The API enforces it.</b>
/// The response carries the ads themselves, because reviewing them IS the control.
/// </param>
/// <param name="ConfirmedJobAdIds">
/// Required when <paramref name="DryRun"/> is false: the ids of the ads the operator actually
/// <b>reviewed</b> in the dry run. <b>Not a count</b> — see
/// <see cref="EraseRecruiterAdsCommand.ConfirmedJobAdIds"/>. Anything he did not confirm is not
/// erased, and any confirmed ad that no longer matches refuses the whole request with 409 (ingest
/// runs every ten minutes, so the set genuinely moves).
/// </param>
public sealed record EraseRecruiterAdsRequest(
    string Identifier,
    bool DryRun,
    IReadOnlyList<Guid>? ConfirmedJobAdIds);

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
