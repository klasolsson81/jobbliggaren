using Hangfire;
using Hangfire.Storage;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.BackgroundJobs.Commands.RequeueFailedJob;
using Jobbliggaren.Application.Admin.BackgroundJobs.Commands.TriggerRecurringJob;
using Jobbliggaren.Application.Common.Authorization;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// Read-only operatörsyta för Hangfire-bakgrundsjobben (#204 / TD-83). Visar
/// status för de schemalagda recurring-jobben (senaste/nästa körning + state)
/// och de senaste misslyckade jobben. Stänger gapet ADR 0032 §9 X4 lämnade
/// ("saknad operatörs-yta för jobb-status/retry/ad-hoc-trigger").
///
/// ARKITEKTUR (Variant B, senior-cto-advisor 2026-06-27): den inbyggda
/// Hangfire-dashboarden monteras INTE. Backend är Bearer-only och medvetet
/// icke-browser-nåbar (ADR 0017/0018, Program.cs ARKITEKTUR-VARNING) — en
/// browser-renderad dashboard skulle bryta trust-modellen, och dess råa
/// job-args/stack-traces är en GDPR-PII-yta vi inte kan fält-kontrollera. I
/// stället: curerade JSON-endpoints bakom den befintliga admin-vägen (samma
/// `RequireAuthorization(Admin)` + BFF cookie→Bearer som /admin/granskning),
/// konsumerade av en civic operatörssida. Se docs/runbooks/hangfire-schema.md §5.
///
/// SÄKERHET (security-auditor must-clear): projektionen ytsätter ALDRIG
/// ExceptionMessage/ExceptionDetails (rå stack trace) eller job-arguments — de
/// kan bära PII (personnummer, e-post, namn, DEK-dekrypterad CV-text). Bara den
/// PII-fria undantags-typens namn ytsätts som felkategori. De 15 recurring-jobben
/// är parameterlösa (`RunAsync(CancellationToken.None)`) → inga arg-payloads att
/// läcka. Svaren cacheas aldrig (`no-store`). Mutationer (trigger/retry) ligger i
/// en separat PR (audit + AdminWritePolicy rate-limit).
///
/// Hangfire-koden bor i composition-roten (Api), inte i Application/Infrastructure
/// (dotnet-architect 2026-06-27 — paritet med <see cref="AdminJobAdsEndpoints"/>
/// som redan injicerar Hangfire-klienten direkt). Reads behöver ingen
/// Mediator-/audit-yta → ingen port; ren framework-introspektion via
/// <see cref="JobStorage"/>.
/// </summary>
public static class AdminBackgroundJobsEndpoints
{
    // Tak på antal misslyckade jobb som returneras. En operatörsyta behöver de
    // senaste felen, inte hela historiken; det totala antalet ytsätts separat så
    // att en trunkering aldrig läses som "noll kvar" (no silent cap).
    private const int MaxFailedJobs = 50;

    public static void MapAdminBackgroundJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/jobs")
            .WithTags("Admin/Jobs")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        // GET /api/v1/admin/jobs/recurring — alla schemalagda recurring-jobb med
        // senaste/nästa körning och senaste state. Sorterad på id för deterministisk
        // visning. Läser Hangfire-storage direkt (samma postgres-schema "hangfire"
        // som Worker:s HangfireServer skriver) via en disponibel storage-connection.
        group.MapGet("/recurring", (JobStorage storage, HttpResponse response) =>
        {
            NoStore(response);

            using var connection = storage.GetConnection();
            var jobs = connection.GetRecurringJobs()
                .Select(AdminBackgroundJobsProjection.ToRecurringDto)
                .OrderBy(j => j.Id, StringComparer.Ordinal)
                .ToList();

            return Results.Ok(jobs);
        });

        // GET /api/v1/admin/jobs/failed — de senaste misslyckade jobben (sanerade)
        // plus det totala antalet i Failed-state. SECURITY: projektionen ytsätter
        // bara id/typnamn/tidpunkt/felkategori — aldrig rå stack trace eller args.
        group.MapGet("/failed", (JobStorage storage, HttpResponse response) =>
        {
            NoStore(response);

            var monitor = storage.GetMonitoringApi();
            var totalCount = monitor.FailedCount();
            // Hangfire.PostgreSql returnerar FailedJobs ordnade failedat DESC
            // (senaste först) per storage-kontrakt — vi re-sorterar därför inte,
            // och "senaste först"-captionen i UI:t är korrekt. (Paritet med den
            // explicita OrderBy:n på /recurring, men ordningen ägs här av storage.)
            var items = monitor.FailedJobs(0, MaxFailedJobs)
                .Select(kvp => AdminBackgroundJobsProjection.ToFailedDto(kvp.Key, kvp.Value))
                .ToList();

            return Results.Ok(new FailedJobsResponse(totalCount, MaxFailedJobs, items));
        });

        // POST /api/v1/admin/jobs/recurring/{id}/trigger — ad-hoc-kör ett schemalagt
        // jobb nu. Går genom Mediator (audit via IAuditableCommand). Validatorn
        // släpper bara igenom ett id ur den slutna RecurringJobIds-allowlisten
        // (fan-out/RCE-skydd) → annars 400. AdminWritePolicy rate-limit (TD-52/98).
        group.MapPost("/recurring/{id}/trigger", async (
            string id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new TriggerRecurringJobCommand(id), ct);
            return result.IsSuccess
                ? Results.Ok(new TriggerJobResponse(result.Value))
                : result.Error.ToProblemResult();
        })
        .RequireRateLimiting(RateLimitingExtensions.AdminWritePolicy);

        // POST /api/v1/admin/jobs/failed/{jobId}/retry — kör om ett misslyckat jobb.
        // Handlern verifierar via porten att jobbet finns och är i Failed-state
        // (saknas → 404, finns men ej Failed → 409); en avvisad omkörning ger ingen
        // audit-rad. AdminWritePolicy rate-limit.
        group.MapPost("/failed/{jobId}/retry", async (
            string jobId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new RequeueFailedJobCommand(jobId), ct);
            return result.IsSuccess
                ? Results.Ok(new RequeueJobResponse(result.Value))
                : result.Error.ToProblemResult();
        })
        .RequireRateLimiting(RateLimitingExtensions.AdminWritePolicy);
    }

    // Operatörsdata ska aldrig cacheas av reverse-proxy eller browser
    // (security-auditor must-clear; hangfire-schema.md §5 p.5).
    private static void NoStore(HttpResponse response) =>
        response.Headers.CacheControl = "no-store, private";
}

/// <summary>
/// Curerad, PII-fri projektion av Hangfires monitoring-DTO:er till Api-DTO:er.
/// Ren och stateless så att säkerhets-invarianten (ingen rå stack trace / inga
/// job-args ytsätts) kan bevisas i en enhetstest utan Hangfire-server.
/// </summary>
public static class AdminBackgroundJobsProjection
{
    private const string UnknownJobType = "(okänt jobb)";
    private const string UnknownError = "(okänt fel)";

    public static RecurringJobStatusDto ToRecurringDto(RecurringJobDto job) => new(
        job.Id,
        job.Cron,
        ToUtcOffset(job.LastExecution),
        job.LastJobState,
        ToUtcOffset(job.NextExecution));

    public static FailedJobStatusDto ToFailedDto(
        string jobId, Hangfire.Storage.Monitoring.FailedJobDto job) => new(
            jobId,
            job.Job?.Type.Name ?? UnknownJobType,
            ToUtcOffset(job.FailedAt),
            ToErrorCategory(job.ExceptionType));

    // SECURITY (GDPR Art. 5, CLAUDE.md §5 personnummer-guard): bara den PII-fria
    // CLR-typnamnet på undantaget ytsätts — ALDRIG ExceptionMessage/ExceptionDetails
    // (den råa stack tracen / serialiserade argumenten kan bära PII). Default-deny:
    // saknas typnamn returneras en generisk etikett, aldrig något annat fält.
    private static string ToErrorCategory(string? exceptionType)
    {
        if (string.IsNullOrWhiteSpace(exceptionType))
            return UnknownError;

        var lastDot = exceptionType.LastIndexOf('.');
        return lastDot >= 0 && lastDot < exceptionType.Length - 1
            ? exceptionType[(lastDot + 1)..]
            : exceptionType;
    }

    // Hangfire lagrar UTC-DateTime (Kind kan vara Unspecified vid storage-roundtrip).
    // Stämpla UTC explicit så serialiseringen ger entydig ISO-8601 med offset.
    private static DateTimeOffset? ToUtcOffset(DateTime? value) =>
        value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : null;
}

/// <summary>
/// Status för ett schemalagt recurring-jobb. <c>LastJobState</c> är Hangfires
/// state-namn ("Succeeded" | "Failed" | "Processing" | null).
/// </summary>
public sealed record RecurringJobStatusDto(
    string Id,
    string? Cron,
    DateTimeOffset? LastExecution,
    string? LastJobState,
    DateTimeOffset? NextExecution);

/// <summary>
/// Sanerad vy av ett misslyckat jobb. <c>ErrorCategory</c> = undantagets typnamn
/// (PII-fritt). Bär ALDRIG rå exception-message, stack trace eller job-arguments.
/// </summary>
public sealed record FailedJobStatusDto(
    string JobId,
    string JobType,
    DateTimeOffset? FailedAt,
    string ErrorCategory);

/// <summary>
/// Svar för GET /api/v1/admin/jobs/failed. <c>TotalCount</c> = totalt antal jobb i
/// Failed-state; <c>Returned</c> = taket som faktiskt returneras (de senaste).
/// </summary>
public sealed record FailedJobsResponse(
    long TotalCount,
    int Returned,
    IReadOnlyList<FailedJobStatusDto> Items);

/// <summary>
/// Svar för POST /api/v1/admin/jobs/recurring/{id}/trigger. <c>JobId</c> = det
/// triggade recurring-jobbets id (echo).
/// </summary>
public sealed record TriggerJobResponse(string JobId);

/// <summary>
/// Svar för POST /api/v1/admin/jobs/failed/{jobId}/retry. <c>Requeued</c> = true
/// när jobbet köades om (annars mappas felet till 404/409 via ToProblemResult).
/// </summary>
public sealed record RequeueJobResponse(bool Requeued);
