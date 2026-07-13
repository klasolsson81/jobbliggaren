using Hangfire;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdRequirements;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk;

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
/// <b>#842 (2026-07-13):</b> the right-to-erasure route for recruiter PII is
/// <b>disabled and returns 501</b>. It never erased anything (it probed a jsonb key
/// the ingest sanitizer guarantees is absent) while reporting success. The working
/// contract is ADR 0106: minimise at ingest (Tier A), remove the whole ad record on
/// request (Tier B).
/// </para>
/// </summary>
public static partial class AdminJobAdsEndpoints
{
    // No identifier, no request body — an Art. 17 request is itself about a person, and the
    // one thing we must not do while failing to erase her address is write it to a log sink.
    [LoggerMessage(
        EventId = 8420,
        Level = LogLevel.Warning,
        Message = "Art. 17 recruiter-PII erasure was attempted, but no erasure path exists (#842). "
            + "The request was refused with 501 and NOTHING was erased. A real erasure request is "
            + "likely in flight: escalate to the data controller per docs/runbooks/recruiter-pii-erasure.md.")]
    private static partial void LogErasureAttemptedWithNoPathAvailable(ILogger logger);

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

        // GDPR Art. 17 (#842) — CONTAINMENT, 2026-07-13.
        //
        // This route used to claim to erase recruiter PII. It could not. It probed
        // raw_payload for {"employer":{"contact_email":…}} — a key the ingest
        // sanitizer's default-deny allowlist guarantees is absent, and which the wire
        // POCO cannot even emit. Measured against the real corpus: 0 of 93 469
        // ingested ads carry that key. rowsAffected = 0 was its only possible outcome,
        // while the recruiter's address sat in job_ads.description in plaintext and
        // was full-text searchable. The route returned 200 OK regardless, and the
        // runbook instructed the operator to confirm erasure to the data subject.
        //
        // A mechanism that reports success while erasing nothing is worse than no
        // mechanism (Art. 12(3)): it manufactures a false statement to a data subject.
        // Until the real erasure path ships (ADR 0106 Tier B — whole-record removal),
        // this route FAILS LOUD. The route is kept rather than deleted so the Art. 17
        // cascade registry in ADR 0024 does not silently dangle, and so an operator
        // with an older runbook is told the truth instead of being served a lie.
        //
        // 501 stays endpoint-local — no new ErrorKind (CLAUDE.md §3, same precedent as
        // the endpoint-local 401). Admin auth still applies (group-level policy).
        //
        // The route runs NO Mediator pipeline, so it writes NO audit row — deliberately, and
        // this is load-bearing. An erasure that does not happen must not leave a record saying
        // it did: the old route's Admin.RecruiterPiiRedacted row is exactly what the old runbook
        // told an operator to read back to the recruiter as proof. (A test pins this — keeping
        // the 501 while running the old pipeline behind it would satisfy every other assertion
        // in that file while writing a false Art. 30 record.)
        //
        // A hit here is still a signal worth having: it means someone is following the old
        // procedure, which means a REAL Art. 17 request is in flight. So: a Warning, carrying no
        // identifier. The lambda binds no request body, so the recruiter's address is never read,
        // validated, logged or persisted (CLAUDE.md §5 — no PII in logs).
        group.MapPost("/redact-recruiter-pii", (ILoggerFactory loggerFactory) =>
        {
            LogErasureAttemptedWithNoPathAvailable(
                loggerFactory.CreateLogger(typeof(AdminJobAdsEndpoints)));

            return Results.Problem(
                title: "Ingen raderingsväg finns ännu",
                detail: "Den automatiska raderingen av rekryterarens kontaktuppgifter "
                    + "var verkningslös och är avstängd (issue #842). Den sökte efter ett "
                    + "fält som aldrig sparas, och rapporterade samtidigt att raderingen "
                    + "var genomförd. En begäran om radering enligt artikel 17 hanteras "
                    + "tills vidare manuellt: eskalera till dataskyddsansvarig. Se "
                    + "docs/runbooks/recruiter-pii-erasure.md.",
                statusCode: StatusCodes.Status501NotImplemented);
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
