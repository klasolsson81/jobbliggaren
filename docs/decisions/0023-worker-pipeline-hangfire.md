# ADR 0023 — Worker-pipeline-aktivering + Hangfire-infrastruktur

**Datum:** 2026-05-08
**Status:** Accepted
**Kontext:** STEG 9 — Worker-pipeline + Hangfire (BUILD.md §18 Fas 1, unblocker för TD-16 och Fas 2/4-jobb)
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0008 (pipeline-ordning, kompletteras igen), ADR 0010 (Worker-projekt — shell aktiveras nu), ADR 0011 (strongly-typed IDs), ADR 0019 (direct-push-praxis), ADR 0022 (audit log pipeline-behavior — Worker-stub-strategin spec:ad där), BUILD.md §18, BUILD.md §schema (rad 715–727)

## Kontext

STEG 9 aktiverar `JobbPilot.Worker`-projektets composition root och introducerar Hangfire för schemalagda bakgrundsjobb. ADR 0010 lämnade Worker-projektet som tom shell ("aktiveras Fas 1") med no-op `Worker.cs`. ADR 0022 specificerade hur Worker-jobb ska auditeras (system-jobb med `user_id = NULL`) men deferrerade implementationen till "när första Worker-jobbet aktiveras".

Klas valde STEG 9 som unblocker över alternativen:

- **Alt B (Fas 0-deploy-stängning)** — kräver inget från STEG 9, kan göras parallellt
- **Alt C (TD-16 audit-retention-jobb)** — beroende av Hangfire (Alt A)

Alt A låser upp både Alt C och Fas 2 JobTech-sync samt Fas 4 AI-jobb-orchestration. Pre-implementation-validering finns på `docs/reviews/2026-05-08-steg9-dotnet-architect.md` (2 kritiska + 5 viktiga + 8 frågor — alla åtgärdade). Reviews finns på `docs/reviews/2026-05-08-steg9-code-reviewer.md` (Approved, 0 blockers/major) och `docs/reviews/2026-05-08-steg9-security-auditor.md` (Approved, 0 blockers, 2 Major för pre-prod).

Frågorna som avgörs i denna ADR:

1. **Hur aktiveras Worker-pipelinen** utan att dra in HTTP-bagage från `Infrastructure.DependencyInjection`?
2. **Hur registreras pipeline-behaviors** så att de faktiskt körs i runtime (Mediator.SourceGenerator 3.0.2 har en kompilator-begränsning som upptäcktes under implementation)?
3. **Hur konfigureras Hangfire** för Fargate single-vCPU-deployment, schema-isolering, och pre-prod-säkerhet?
4. **Hur modelleras stale-detektering** på `Application`-aggregatet utan att bryta CleanArch eller introducera EF-translation-fel?

## Beslut

Fyra delbeslut i samma ADR — de är tätt sammanvävda och en uppdelning hade gett tre ADR:er som måste läsas tillsammans ändå.

### Delbeslut 1 — Worker-pipeline-aktivering

`Worker.cs` heartbeat-stub tas bort. `Worker/Program.cs` refaktoreras till full DI-host:

```
AddPersistence
+ AddApplication
+ Worker-stubs av audit-portarna (ICurrentUser, ICorrelationIdProvider, IRequestContextProvider)
+ AddMediator
+ AddMediatorPipelineBehaviors
+ AddHangfire
+ AddHangfireServer
+ AddHostedService<RecurringJobRegistrar>
```

Stänger ADR 0010:s "aktiveras Fas 1"-stub. ADR 0022:s deferrerade Worker-stub-strategi implementeras nu enligt delbeslut 3.

### Delbeslut 2 — Infrastructure DI-modulär refaktor

`AddInfrastructure` splittras i tre extensions med tydligt ansvar:

| Extension | Innehåll | Konsumenter |
|-----------|----------|-------------|
| `AddPersistence` | DbContext, IAppDbContext, IDateTimeProvider | Api + Worker |
| `AddIdentityAndSessions` | Identity, Redis, JWT, ICurrentUser via HTTP | Api only |
| `AddHttpAuditing` | CorrelationIdProvider (HTTP), RequestContextProvider (HTTP) | Api only |

Worker laddar bara `AddPersistence` + egna Worker-stubs. SRP på composition-nivå — Worker drar inte HTTP-bagage som `IHttpContextAccessor`. Renlärig CleanArch.

### Delbeslut 3 — Worker-stub-implementationer av audit-portarna

Per ADR 0022:s deferrerade spec:

| Stub | Lifetime | Beteende |
|------|----------|----------|
| `WorkerSystemUser` | Singleton | `UserId = null`, `IsAuthenticated = false` → audit-rad får `user_id = NULL` |
| `WorkerCorrelationIdProvider` | Scoped | Instans-fält `Guid` (en correlation-ID per Hangfire-job-execution) |
| `WorkerRequestContextProvider` | Scoped | `IpAddress = null`, `UserAgent = null` (GDPR-data-minimering) |

**Viktigt — `WorkerCorrelationIdProvider` är inte HTTP-versionens fallback:** HTTP-versionen genererar `Guid.NewGuid()` per anrop när header saknas. Om Worker använde samma fallback skulle multipla audit-skrivningar inom samma jobb-scope få **olika** correlation-ID:n och inte längre vara länkbara. Worker-versionen skapar istället ett `Guid` per scope-instans — alla audit-rader från samma jobb-execution delar correlation-ID.

### Delbeslut 4 — Hangfire-konfiguration

**Paket:**
- `Hangfire.Core 1.8.23`
- `Hangfire.AspNetCore 1.8.23` (felnamn:at — paketet fungerar med Generic Host eftersom det innehåller IServiceScopeFactory-baserad JobActivator)
- `Hangfire.PostgreSql 1.21.1`

**Konfiguration:**

```csharp
services.AddHangfire(cfg => cfg
    .UsePostgreSqlStorage(opt => opt
        .UseNpgsqlConnection(connectionString)
        .ConfigureOptions(o =>
        {
            o.SchemaName = "hangfire";
            o.PrepareSchemaIfNecessary = true; // FLIPPA TILL FALSE I PROD (TD-17)
        })));

services.AddHangfireServer(opt =>
{
    opt.WorkerCount = 4; // explicit för Fargate single-vCPU container
});
```

- **Schema-isolering:** `SchemaName = "hangfire"` — separat från `public.*` JobbPilot-tabeller
- **`WorkerCount = 4`:** explicit för Fargate single-vCPU container (IO-bunden Mediator-dispatch — 4 är safe upper bound utan thread-starvation)
- **Cron i UTC:** `Cron.Daily(3)` = 03:00 UTC = svensk natt
- **RecurringJob-registrering** via `IHostedService` (`RecurringJobRegistrar`) — idempotent via `AddOrUpdate`
- **Första registrerade jobb:** `detect-ghosted` → `DetectGhostedApplicationsJob.RunAsync(CancellationToken.None)`

### Delbeslut 5 — Pipeline-fix (kritisk upptäckt under implementation)

Initial Fas 9.6-refaktor flyttade pipeline-config till delad konstant `MediatorPipelineBehaviors.InOrder` och satte `options.PipelineBehaviors = MediatorPipelineBehaviors.InOrder` i båda composition roots.

**Problem:** Mediator.SourceGenerator 3.0.2 läser **inte** `options.PipelineBehaviors` från fält-references vid compile-time — den behöver inline-array-literal för att kunna analyseras. Resultat: pipeline-behaviors registrerades aldrig i DI. UoW:s `SaveChangesAsync` anropades aldrig. Audit-rader skrevs inte. Smyg data-loss-bugg som hade tyst brutit STEG 8:s audit-funktionalitet för Api också om inte integration smoke-test fångat det.

**Fix:** Ny `AddMediatorPipelineBehaviors()`-extension i `JobbPilot.Application.Common.MediatorPipelineBehaviors`. Registrerar behaviors som **open-generic DI-services**:

```csharp
public static IServiceCollection AddMediatorPipelineBehaviors(this IServiceCollection services)
{
    // Ordning per ADR 0008 + ADR 0022 (5 behaviors):
    // Logging → Validation → Authorization → UnitOfWork → Audit → Handler
    services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
    services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));
    services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
    return services;
}
```

Mediator runtime hämtar dem via `GetServices<IPipelineBehavior<TRequest, TResponse>>()` — fungerar pålitligt och i registrerings-ordning. Anropas explicit av både `Api/Program.cs` och `Worker/Program.cs` efter `AddMediator(...)`.

**Regression-skydd dual-coverage:**

- Architecture test `MediatorPipeline_should_have_expected_behaviors_in_order` cementerar att array-ordningen inte ändras tyst
- Integration smoke-test `DetectGhostedApplicationsJobIntegrationTests` verifierar att audit-rad faktiskt skrivs (failar om någon backar fixen)

Detta är en utvidgning av ADR 0008/0022:s pipeline-spec — pipeline-ordningen är oförändrad (5 behaviors), men registrerings-mekanismen är annorlunda.

### Delbeslut 6 — Stale-detektering på Application-aggregatet

Två nya properties på `Application`-aggregatet (matchar BUILD.md §schema rad 715–727):

- `LastStatusChangeAt` — `DateTimeOffset`, NOT NULL
- `GhostedThresholdDays` — `int`, NOT NULL, default 21

**Mutations-regler för `LastStatusChangeAt`** (V4 från arch-rapport):

| Metod | Sätter LastStatusChangeAt |
|-------|---------------------------|
| `Create()` | Ja |
| `TransitionTo(...)` | Ja |
| `MarkGhosted()` | Ja |
| `AddFollowUp(...)` | Nej |
| `AddNote(...)` | Nej |
| `SoftDelete()` | Nej |

Endast genuina status-mutationer flippar fältet. Per-app-override av `GhostedThresholdDays` har **ingen** `SetGhostedThresholdDays`-metod i Fas 1 (Q2 från arch-rapport — deferreras till Fas 3 när per-app-override-feature får UI).

**Specification i Application-lagret** (V3 från arch-rapport — flytta från Domain till Application eftersom det är read-query, inte invariant-skydd). `StaleApplicationSpecification` är tvådelad:

- `CandidateStatusFilter()` — SQL-filter (`Status ∈ {Submitted, Acknowledged}`), EF-översättbart, utnyttjar partial-index
- `IsStaleNow(lastStatusChangeAt, ghostedThresholdDays, now)` — client-side check eftersom Npgsql 10 inte tillförlitligt översätter `DateTimeOffset.AddDays(int kolumn)`

**Definition of stale (Fas 1):** Snäv `{Submitted, Acknowledged}`. Definition: "transient-states där företaget förväntas svara. Intervju-states (`InterviewScheduled`, `Interviewing`) betraktas active oavsett kalendertid." TD-post för utökning till intervju-states aktiveras vid första rapporterade fall av "intervju ställdes in, app fastnade".

### Delbeslut 7 — Migration `AddApplicationStaleDetectionFields`

- Ny kolumn `last_status_change_at` (`timestamptz`, NOT NULL) — backfill **`NOW()`** (inte `updated_at`). Att backfilla med `updated_at` hade gjort gamla apps omedelbart Ghosted-flaggade vid första cron-körning. Med `NOW()` får befintliga apps 21-dagars-fönster räknat från migrationsdatum.
- Ny kolumn `ghosted_threshold_days` (`int`, NOT NULL, default 21).
- Partial index `ix_applications_stale_detection` på `(last_status_change_at)` med `WHERE status IN ('Submitted', 'Acknowledged') AND deleted_at IS NULL` — minimal index-storlek + index-only scan-möjlighet.

### Delbeslut 8 — Orchestrator-pattern för `DetectGhostedApplicationsJob`

`DetectGhostedApplicationsJob` placeras i Application-lagret (`Applications/Jobs/GhostedDetection/`). Konstruktor:

```csharp
public DetectGhostedApplicationsJob(
    IAppDbContext db,
    IMediator mediator,
    IDateTimeProvider clock,
    ILogger<DetectGhostedApplicationsJob> log)
```

`RunAsync(CancellationToken ct)`:

1. Materialisera kandidat-set via SQL-snävning (Status-filter via `CandidateStatusFilter`)
2. Filtrera client-side per `IsStaleNow(...)` (per-app-threshold)
3. Loop med `mediator.Send(new MarkGhostedCommand(id), ct)` per stale-id

**Audit-paritet:** en audit-rad per ghosted application via `AuditBehavior` — matchar ADR 0022 1:1 (en aggregate-id per audit-rad). Per-id loop framför single batch-command motiverat av audit-modell + isolering (en handler-failure rullar inte tillbaka andra). Skala N=50–100/dag är trivial.

`AsNoTracking` + cancel-token-check + progress-log var 25:e (V5 från arch-rapport). Idempotent — kan köras flera gånger utan biverkningar (`MarkGhosted`-handler är idempotent: skip om Status inte är `Submitted`/`Acknowledged`).

### Delbeslut 9 — Test-strategi: integration smoke-test

Manuellt smoke-test omdefinierades till **integration smoke-test** (Klas tillägg #3):

- Nytt projekt `JobbPilot.Worker.IntegrationTests` med Testcontainers Postgres
- `DetectGhostedApplicationsJobIntegrationTests` med 6 test-fall: happy + 5 negativa
- `[Trait("Category", "SmokeTest")]` — körs **inte** i default `dotnet test`
- Kommando: `dotnet test --filter "Category=SmokeTest"`
- CC äger smoke-testet helt; Klas läser pass/fail
- **Rationale:** manuellt smoke-test hade missat pipeline-bug-fyndet. Automatiserat smoke-test fångar regressionen permanent.

## Konsekvenser

### Positiva

- **Worker-shell aktiverat** — ADR 0010 stänger sin "aktiveras Fas 1"-stub
- **Hangfire-infrastruktur landad** — Alt C (TD-16) + Fas 2 JobTech-sync + Fas 4 AI-jobb är nu unblockable
- **DI-modulär refaktor:** SRP på composition-nivå, Worker drar inte HTTP-bagage
- **Pipeline-bug-fix hård-låst** — regression-skyddat via arch-test + integration smoke-test
- **Audit-paritet i Worker-context fungerar** — verifierat: `user_id = NULL`, `correlation_id` korrekt sammanlänkad inom jobb-scope
- **BUILD.md §schema följs** — `LastStatusChangeAt` + `GhostedThresholdDays` matchar rad 715–727
- **Idempotens** på både `RecurringJobRegistrar` (`AddOrUpdate`) och `MarkGhosted`-handler — säkert att köra om vid omstart

### Negativa

- **Mediator.SourceGenerator's `options.PipelineBehaviors`-API är inte tillförlitligt** → vi använder DI-registrering istället. Mindre elegant men robust. Spårbar via arch-test om någon framtida bidragsgivare försöker återgå till array-konstanten.
- **Worker.csproj drar in Hangfire transitivt** → Architecture.Tests-projektet drar också in det (eftersom det refererar Worker). Inga assembly-konflikter (verifierat).
- **Hangfire-storage delar samma DB-användare** som JobbPilot.Application (lateral access-yta) → MIN-4 (TD-17) deferreras till prod-deploy.
- **`PrepareSchemaIfNecessary = true` i dev/test** → MAJ-1 (TD-17) ska flippas till `false` i prod via konfig-overlay.
- **Per-app `GhostedThresholdDays`-override saknar UI** i Fas 1 → kolumn finns men kan bara sättas via DB. Acceptabelt — feature konkretiseras i Fas 3.

### Mitigering

- Architecture test `MediatorPipeline_should_have_expected_behaviors_in_order` förhindrar tyst regression av pipeline-ordning
- Integration smoke-test `DetectGhostedApplicationsJobIntegrationTests` förhindrar tyst regression av pipeline-DI-registrering
- TD-17 i `docs/tech-debt.md` listar alla pre-prod-deploy-krav (5 punkter — se nedan)
- Worker-architecture-tester (5 nya) förhindrar att HTTP-bagage smyger in i Worker-projektet

## GDPR-policy

**Inga nya PII-ytor introduceras av denna ADR.**

- `LastStatusChangeAt` är timing-data, inte PII
- `GhostedThresholdDays` är konfig-värde, inte PII
- Audit-rader för Worker-jobb får `user_id = NULL` (system-jobb), `ip_address = NULL`, `user_agent = NULL` per ADR 0022 + GDPR Art. 5(1)(c) (data-minimering — null är minst möjliga data)
- Migration-backfill (`NOW()`) körs inom DB-transaktion — ingen extern PII-exponering

`WorkerRequestContextProvider` returnerar konsekvent `null` för IP och User-Agent. Detta är **avsiktligt** — system-jobb har ingen "request context" att representera, och att lagra fiktiv IP (`127.0.0.1`) eller User-Agent (`HangfireWorker/1.0`) hade gett false-positives i incident-response-analyser.

## Alternativ övervägda

### Alt B — Fas 0-deploy-stängning först

Pause STEG 9 för deploy-arbete. Avvisat — TD-16 är kvarvarande Fas 1 prod-blocker, och TD-16 kräver Hangfire (Alt A). Att göra deploy-stängning först hade gett samma resultat senare med extra kontextväxling.

### Alt C — TD-16 retention-jobb direkt

Bygg retention-jobbet utan generell Hangfire-infrastruktur. Avvisat — Hangfire-infrastruktur är förutsättning för **alla** schemalagda jobb (Fas 2 JobTech-sync, Fas 4 AI-jobb, Fas 4 retention). Att bygga TD-16 utan Hangfire-grunden hade krävt ad-hoc scheduling och sedan migration när "riktig" Hangfire införs. Alt A unblocker.

### Alt α — DI sista-vinner-override för Worker

Worker registrerar `AddInfrastructure` (full HTTP-stack) och överskriver audit-portar efter via `services.RemoveAll<...>().AddSingleton<...>()`. Avvisat — arkitekturell lukt (dolt tillstånd, registrerings-ordnings-känslig). Modulär split (delbeslut 2) är tydligare.

### Alt γ — Återanvänd Infrastructure-impls som-de-är

`HttpCorrelationIdProvider` etc. är defensiva nog att fungera utan `HttpContext` (returnerar fallback-värden). Avvisat — HTTP-baserade impls i Worker är fel hem. `HttpCorrelationIdProvider`:s fallback genererar `Guid.NewGuid()` per anrop, vilket bryter correlation-ID-länkning över multipla audit-skrivningar i samma jobb-scope (se delbeslut 3).

### Alt δ — Domain-metod `IsStale` på Application-aggregatet

Lägg `bool IsStale(DateTimeOffset now)` på `Application`-entity:n. Avvisat — det är ren read-query, inte invariant-skydd. Specification i Application-lagret (V3) håller Domain rent från read-concerns.

### Alt ε — Single batch-command `MarkGhostedBatch(IList<Guid>)`

En command som markerar alla stale apps på en gång. Avvisat — hade krävt special-case audit-extraktion (`ExtractAggregateId` returnerar bara en Guid) eller bryter audit-modellen 1:1. Per-id loop matchar audit-modell rakt av. Skala N=50–100/dag är trivial — ingen perf-vinst med batch.

### Alt ζ — EF-translation av `AddDays(int kolumn)`

Plan A var att skriva specification helt SQL-side: `WHERE last_status_change_at + (ghosted_threshold_days || ' days')::interval < NOW()`. Avvisat — Npgsql 10 översätter inte tillförlitligt `DateTimeOffset.AddDays(int kolumn)` (testat — translation-error). Plan B (SQL-snävning på Status + client-side filter på threshold) är acceptabel för Fas 1-volym (< 10k aktiva apps).

## Implementation

**Domain:**
- `src/JobbPilot.Domain/Applications/Application.cs` (modifierad — `LastStatusChangeAt`, `GhostedThresholdDays`-properties + mutations-regler)

**Application:**
- `src/JobbPilot.Application/Common/MediatorPipelineBehaviors.cs` (ny — `AddMediatorPipelineBehaviors()`-extension, pipeline-fix)
- `src/JobbPilot.Application/Applications/Specifications/StaleApplicationSpecification.cs` (ny — `CandidateStatusFilter` + `IsStaleNow`)
- `src/JobbPilot.Application/Applications/Jobs/GhostedDetection/DetectGhostedApplicationsJob.cs` (ny — orchestrator)

**Infrastructure:**
- `src/JobbPilot.Infrastructure/DependencyInjection.cs` (refaktorerad — `AddPersistence` / `AddIdentityAndSessions` / `AddHttpAuditing`)
- `src/JobbPilot.Infrastructure/Persistence/Configurations/ApplicationConfiguration.cs` (modifierad — `LastStatusChangeAt` + `GhostedThresholdDays` + partial index)
- `src/JobbPilot.Infrastructure/Persistence/Migrations/20260508093139_AddApplicationStaleDetectionFields.cs` (ny)

**Api:**
- `src/JobbPilot.Api/Program.cs` (modifierad — använder `AddMediatorPipelineBehaviors()` istället för `options.PipelineBehaviors`-array)

**Worker:**
- `src/JobbPilot.Worker/Program.cs` (full refaktor — DI-host)
- `src/JobbPilot.Worker/Auditing/WorkerSystemUser.cs` (ny stub — `ICurrentUser`)
- `src/JobbPilot.Worker/Auditing/WorkerCorrelationIdProvider.cs` (ny stub — `ICorrelationIdProvider`)
- `src/JobbPilot.Worker/Auditing/WorkerRequestContextProvider.cs` (ny stub — `IRequestContextProvider`)
- `src/JobbPilot.Worker/Hosting/RecurringJobRegistrar.cs` (ny `IHostedService` — `AddOrUpdate` av `detect-ghosted`)
- `src/JobbPilot.Worker/JobbPilot.Worker.csproj` (Hangfire-paket)
- `src/JobbPilot.Worker/Worker.cs` (heartbeat-stub: **borttagen**)

**Paket-pinning (Directory.Packages.props):**
- `Hangfire.Core 1.8.23`
- `Hangfire.AspNetCore 1.8.23`
- `Hangfire.PostgreSql 1.21.1`
- `Newtonsoft.Json 13.0.3` (transitiv CVE-pinning — CVE-2024-21907 / GHSA-5crp-9r3c-p9vr — DoS via StackOverflow vid djupt nestade JSON. Hangfire drar in vulnerable 11.0.1 transitivt. Pinning sker via `<PackageVersion>` + `CentralPackageTransitivePinningEnabled=true` (redan aktiverat))

**Tester:**
- `tests/JobbPilot.Architecture.Tests/WorkerLayerTests.cs` (5 nya arch-tester — Worker drar inte HTTP-bagage, pipeline-ordning, etc.)
- `tests/JobbPilot.Domain.UnitTests/Applications/ApplicationTests.cs` (utökad med 9 nya tester — mutations-regler för `LastStatusChangeAt`)
- `tests/JobbPilot.Application.UnitTests/Applications/Jobs/GhostedDetection/DetectGhostedApplicationsJobTests.cs` (ny, 12 tester)
- `tests/JobbPilot.Worker.IntegrationTests/` (nytt projekt — 6 smoke-tester med Testcontainers Postgres, `[Trait("Category", "SmokeTest")]`)

**Test-status efter STEG 9:** 451 backend-tester gröna (157 Domain + 169 Application + 15 Architecture + 104 Api Integration + 6 Worker SmokeTest). 65 Vitest + 19 E2E oförändrat.

### Pre-prod-deploy-krav (TD-17 — spårat i `docs/tech-debt.md`)

1. Hangfire `PrepareSchemaIfNecessary` flippa till `false` + konfig-overlay (MAJ-1 från security-auditor)
2. Hangfire-schema-runbook med GRANT-modell (separat migrations-user vs runtime-user)
3. Eventuell dashboard-yta får aldrig exponeras utan custom `IDashboardAuthorizationFilter` (MAJ-2)
4. Splittra `ConnectionStrings`: `Postgres` (Worker-app) + `HangfireStorage` (least-privilege Hangfire-user) (MIN-4)
5. Defensiv runbook-anteckning: första 21 dagar efter prod-deploy är "kalibrerings-fas" — Klas följer Hangfire-dashboard för anomaliska volymer (MIN-3)

## Status

**Accepted** för Fas 1. Omvärderas vid:

- **Fas 2** — när Worker-jobb-yta växer (JobTech-sync, follow-up-reminders): bekräfta att DI-modulär split fortfarande räcker, eventuellt extrahera `AddWorkerStubs`-extension om fler stubs tillkommer
- **Fas 4** — när AI-jobb-orchestration introduceras + TD-16 retention-jobb byggs: bekräfta att `WorkerCount = 4` räcker eller om jobb-prioritets-köer behövs (`Hangfire.Server.Queues`)

Pre-prod-deploy-krav (TD-17) ska adresseras innan Fas 1 går till prod. ADR 0008 + ADR 0022 kompletteras implicit av denna ADR — pipeline-ordningen är oförändrad (5 behaviors), men registrerings-mekanismen är `AddMediatorPipelineBehaviors()`-extension istället för `options.PipelineBehaviors`-array.

---

## Amendment 2026-05-24 — Worker får Redis-write-port (ADR 0064)

> **Amendment 2026-05-24** (dotnet-architect-dom agentId `a9446dac40e8fef02`, Klas-GO för verbatim-text per memory `feedback_klas_can_override_adr_verbatim_source`):
>
> ADR 0064 introducerar `IDistributedCache`-skrivport i Worker (`RedisLandingStatsCache` via `RefreshLandingStatsJob`, cron `*/5 * * * *`). Invariantens kärna (HTTP-fri Worker, ingen ASP.NET Core, ingen Identity-cookie/auth-yta) är **opåverkad**. Outgoing-portar (System.Net.Http per ADR 0032 + StackExchange.Redis per ADR 0064) är fortsatt OK; det är HTTP-server-bagaget som förbjuds.
>
> **Operativ konsekvens:** Worker-task-def kräver `ConnectionStrings__Redis`-secret-injektion i `worker_secrets`-blocket (Terraform `environments/dev/main.tf` rad 328-335). Tidigare kommentar "Worker använder INTE Redis" är **superseded** av denna amendment.
>
> **Fail-loud-paritet:** Worker `Program.cs` Redis-DI kastar `InvalidOperationException` vid saknad `ConnectionStrings:Redis` (paritet Api `Infrastructure/DependencyInjection.cs:438-440`) — ingen tyst localhost-fallback. Incident 2026-05-24 (Worker-restart efter v0.2.60-dev-deploy producerade `RedisConnectionException` mot localhost var 5:e min eftersom `worker_secrets` saknade Redis-injektion) motiverar denna disciplin.
