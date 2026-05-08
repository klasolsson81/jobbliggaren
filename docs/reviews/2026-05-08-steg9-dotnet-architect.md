---
review: dotnet-architect
fas: STEG 9 plan-design (pre-implementation)
datum: 2026-05-08
status: 2 kritiska + 5 viktiga + 8 frågor besvarade — alla åtgärdade i justerad plan
relaterade-adr: 0001, 0008, 0009, 0010, 0011, 0022
relaterad-build: §3.1, §5, §7, §16, §18
---

# dotnet-architect — STEG 9 plan-validering

Pre-implementation-validering av STEG 9 (Worker-pipeline-aktivering + Hangfire-setup + första schemalagda jobb `DetectGhostedApplicationsJob`). Förskott från Fas 2-3, Klas valt detta som unblocker före TD-16 och Fas 2 JobTech-sync.

## Sammanfattning

Solid plan, väl grundad i ADR 0010 + 0022. Behöver åtgärdas — **2 kritiska fynd**, **5 viktiga fynd**, **8 specifika frågor besvarade**. De kritiska handlar båda om audit-portarnas livscykel i Worker-kontexten — ett missed-detail som kan orsaka audit-läckage mellan jobs eller silent fail.

---

## Kritiska fynd

### K1 — `WorkerCorrelationIdProvider` impl: cacha i instans-fält

**Fil:** `src/JobbPilot.Worker/Auditing/WorkerCorrelationIdProvider.cs` (planerad)

**Vad:** Plan säger "Scoped — en Guid per DI-scope". Men den nuvarande HTTP-baserade `CorrelationIdProvider` är registrerad som Scoped i Infrastructure men cachar i `HttpContext.Items` (request-bunden via `IHttpContextAccessor`). I Worker måste implementationen själv hålla en cachad Guid i instans-fältet, eftersom det inte finns något ekvivalent till `HttpContext.Items` att stash:a i.

**Varför:** Hangfire skapar en ny `IServiceScope` per job-execution (via `JobActivator`). Om Worker-implementationen genererar `Guid.NewGuid()` *per anrop till `Current`* (som HTTP-versionen gör i fallback-grenen där `ctx is null`) får varje anrop till `correlationIdProvider.Current` inom samma jobb-scope **olika ID:n**. AuditBehavior anropar `Current` en gång per audit-skrivning, så det syns inte direkt — men om någon framtida kod loggar correlation-ID både i LoggingBehavior och AuditBehavior kommer de inte matcha.

**Föreslagen åtgärd:** Worker-impl ska cacha i instans-fält (Scoped DI-livstid garanterar att samma instans läses inom scope):

```csharp
public sealed class WorkerCorrelationIdProvider : ICorrelationIdProvider
{
    private readonly Guid _id = Guid.NewGuid();
    public Guid Current => _id;
}
```

Ingen lock behövs — Scoped-instansen är inte cross-thread under en Hangfire-job-execution.

### K2 — `AddHttpAuditing` får aldrig laddas i Worker

**Fil:** `src/JobbPilot.Infrastructure/Auditing/CorrelationIdProvider.cs:33` + ny `AddHttpAuditing`-metod

**Vad:** Den befintliga HTTP-versionens fallback-gren `if (ctx is null) return Guid.NewGuid();` skapar **ny Guid per anrop** när `HttpContext` saknas. Worker-DI-splittringen ska se till att HTTP-versionen aldrig laddas i Worker — annars triggas just denna fallback.

**Varför:** Plan 9.3 splittrar DI så att `AddIdentityAndSessions` (som registrerar HTTP-versionen) inte körs i Worker. Men `AddHttpAuditing()` enligt plan registrerar `CorrelationIdProvider` (HTTP-versionen). Worker ska **inte** anropa `AddHttpAuditing` — den ska registrera Worker-stubs i sin egen Program.cs istället.

**Föreslagen åtgärd:** Tre saker:

1. Lägg en kommentar i `Infrastructure/DependencyInjection.cs` på `AddHttpAuditing`-metoden: "HTTP-only. Worker registrerar egna implementationer."
2. Lägg ett architecture test som verifierar att Worker-assemblies inte beror på `Microsoft.AspNetCore.Http` (analogt med `Identity_types_should_stay_in_Infrastructure`).
3. Worker/Program.cs registrerar `WorkerCorrelationIdProvider`, `WorkerRequestContextProvider`, `WorkerSystemUser` direkt — **inte** via `AddHttpAuditing`.

---

## Viktiga fynd

### V3 — `IsStale` flyttas från Domain till Specification i Application-lagret

**Fil:** `src/JobbPilot.Domain/Applications/Application.cs` — `IsStale`-metod planerad

**Vad:** Plan föreslår domain-metod `IsStale(IDateTimeProvider clock)` på aggregatet. Detta är en **read-only query**, inte en state-mutation eller invariant-skydd.

**Varför:** Aggregat-metoder som inte muterar state och inte skyddar invarianter passar bättre som specifications eller projection. CLAUDE.md §2.2 säger "Ändringar raisar domain events — events är sanningen". `IsStale` är inte en ändring. Att den ligger på aggregatet kräver att orchestratorn **materialiserar hela Application-aggregat** bara för att fråga "är du stale?" — kontraproduktivt mot Plan 9.5 (som vill query stale-IDs i en enda DB-query).

**Föreslagen åtgärd:** Ha **både**:

- En **specification** i Application-lagret som uttrycker stale-villkoret som en `Expression<Func<Application, bool>>` (för EF-translation):

```csharp
// JobbPilot.Application/Applications/Specifications/StaleApplicationSpecification.cs
public static class StaleApplicationSpecification
{
    public static Expression<Func<Application, bool>> Build(DateTimeOffset now) =>
        a => (a.Status == ApplicationStatus.Submitted || a.Status == ApplicationStatus.Acknowledged)
          && a.DeletedAt == null
          && a.LastStatusChangeAt.AddDays(a.GhostedThresholdDays) < now;
}
```

- Behåll *inte* en `IsStale`-metod på aggregatet i Fas 1. Lägg till den först om/när domänen behöver den för en invariant (t.ex. "kan inte transition:a stale-app utan auto-ghosting").

Detta löser också Q4 (EF-query-uttryck) — `AddDays` med int-fält translaterar Npgsql 10 cleant via `DATE_ADD`/interval-arithmetic i SQL.

### V4 — `LastStatusChangeAt` får STRIKT-flippas

**Fil:** `src/JobbPilot.Domain/Applications/Application.cs`

**Vad:** Plan säger NOT NULL + backfill från `updated_at`. Det är riktigt för data — men semantiskt är `last_status_change_at` inte samma sak som `updated_at` (notes/follow-ups bumpar `updated_at` men ska inte resetta ghosted-klockan).

**Varför:** Om `AddNote` eller `AddFollowUp` muterar `UpdatedAt` (kollar koden — det gör den implicit via SaveChanges-konventionen), och backfill sätter `LastStatusChangeAt = UpdatedAt`, så är initial-staten korrekt vid migration. Men *framåt* måste `LastStatusChangeAt` flippas **enbart** i `TransitionTo` och `MarkGhosted` (och `Create` när status sätts till Draft). `AddNote`/`AddFollowUp`/`SoftDelete` får inte röra fältet.

**Föreslagen åtgärd:** Audit av `Application.cs` — lägg till `LastStatusChangeAt = clock.UtcNow` i:

- `Create()` (sätter initialt vid Draft — krävs eftersom NOT NULL)
- `TransitionTo()` (verkligt status-byte)
- `MarkGhosted()` (också status-byte)

**Inte** i `AddFollowUp` eller `AddNote`. Verifiera att existerande tester `Application_AddFollowUp_DoesNotChangeUpdatedAt` finns, eller skriv en — annars regression-risk.

### V5 — Orchestrator-pattern: `AsNoTracking` + cancel-token + progress-log

**Fil:** `src/JobbPilot.Application/Applications/Jobs/GhostedDetection/DetectGhostedApplicationsJob.cs` (planerad)

**Vad:** Plan väljer per-id loop. Detta är **rätt val** för audit-paritet (en audit-rad per app — exakt det ADR 0022 spec:ar) och för isolering (en MarkGhosted-failure rullar inte tillbaka andra). Trade-off (N pipeline-runs) är acceptabel vid förväntad volym ~50–100/dag.

**Varför:** Bekräftar plan, men commit-strategi måste vara explicit:

1. Orchestratorn **får inte hålla DbContext-spårade entities** över loop-iterationer — `AsNoTracking().Where(spec).Select(a => a.Id).ToListAsync()` *sedan* loop. Annars håller långa change-tracker-listor minne i hela scan.
2. Logga progress per N (t.ex. var 25:e) så Worker-loggen inte spammar.
3. Cancel-token-kontroll i loopen så ett pågående scan kan abortas.

**Föreslagen åtgärd:**

```csharp
public async Task RunAsync(CancellationToken ct)
{
    var now = clock.UtcNow;
    var staleIds = await db.Applications
        .AsNoTracking()
        .Where(StaleApplicationSpecification.Build(now))
        .Select(a => a.Id.Value)
        .ToListAsync(ct);

    logger.LogInformation("DetectGhostedApplicationsJob: hittade {Count} stale applications", staleIds.Count);

    var processed = 0;
    foreach (var id in staleIds)
    {
        ct.ThrowIfCancellationRequested();
        var result = await mediator.Send(new MarkGhostedCommand(id), ct);
        processed++;
        if (processed % 25 == 0)
            logger.LogInformation("DetectGhostedApplicationsJob: {Processed}/{Total}", processed, staleIds.Count);
    }
}
```

### V6 — `Hangfire.PostgreSql` schema-isolering

**Fil:** `src/JobbPilot.Worker/Program.cs` (planerad)

**Vad:** Hangfire.PostgreSql kör automatisk schema-creation i default `hangfire`-schema vid `UsePostgreSqlStorage(...)`. Detta händer **utanför** EF Migrations.

**Varför:** Två concerns:

1. **Permission-konflikt:** I prod ska Worker-DB-användaren ha rättighet att skapa schema + tabeller på första start. Lösbart men ska dokumenteras.
2. **Schema-isolering:** Hangfire-tabellerna ska **inte** ligga i `public` (default) — använd dedikerat schema (`hangfire`) för att undvika konflikter med JobbPilot-tabeller.

**Föreslagen åtgärd:**

```csharp
.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString),
    new PostgreSqlStorageOptions
    {
        SchemaName = "hangfire",
        PrepareSchemaIfNecessary = true,  // dev/test
        // I prod: false + kör manuell migration via runbook
    })
```

Lägg dessutom in en runbook-stub `docs/runbooks/hangfire-schema.md` (skriv-skiss kan deferras till deploy-fasen).

### V7 — `Cron.Daily(3)` är UTC

**Fil:** `src/JobbPilot.Worker/Program.cs` / RecurringJobRegistrar (planerad)

**Vad:** Hangfire kör cron i **UTC** by default. `Cron.Daily(3)` betyder 03:00 UTC = 04:00 svensk vintertid / 05:00 sommartid.

**Varför:** Inte ett bug men en explicit-design-fråga. Klockslaget för "låg trafik" kan motivera UTC-val, men det ska inte vara implicit.

**Föreslagen åtgärd:** Behåll UTC och dokumentera i kommentar. `CancellationToken.None` är OK — Hangfire injicerar inte CT på recurring-job-uttryck (känt kvirk; använd `JobCancellationToken.Null.ShutdownToken` om shutdown-respect behövs).

---

## Svar på de 8 specifika frågorna

**Q1 — `IsStale`-placering:** Specification i Application-lagret (se V3). Aggregatet behåller inte `IsStale`. EF-översättning + isolerad query-prestanda + DDD-renlighet vinner alla tre.

**Q2 — `SetGhostedThresholdDays`:** **Deferra** i Fas 1. Hårdkoda default=21 i `Application.Create(...)`-konstruktorn. Per-app-override har ingen UI än, ingen command, ingen event. Lägg till private-set-fältet med EF-mapping så schema-fältet finns; bygg setter-metoden + command när UI-feature kommer. Matchar JobbPilots "build-only-what's-needed-but-don't-block-future"-mönster.

**Q3 — Orchestrator-pattern:** Per-id loop är rätt val. Audit-paritet är det avgörande argumentet — en audit-rad per ghosted Application matchar ADR 0022 1:1, och en single batch-command hade krävt special-case-audit-extraktion. Skala N=50–100 är trivial.

**Q4 — EF-query-uttryck:** Använd `LastStatusChangeAt.AddDays(GhostedThresholdDays) < now` (DateTimeOffset.AddDays(int)) — Npgsql 10 translaterar detta till `last_status_change_at + (ghosted_threshold_days * INTERVAL '1 day')`. Verifierat mönster i Npgsql-docs. **Inte** `TimeSpan.FromDays(...)` — onödig indirektion. Plan B (client-side filter) är *bara* acceptabel om EF-translation visar sig brista.

**Q5 — Worker DI-pattern:** Sekvens är korrekt men ofullständig. Korrekt ordning:

```csharp
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddSingleton<ICurrentUser, WorkerSystemUser>();
builder.Services.AddScoped<ICorrelationIdProvider, WorkerCorrelationIdProvider>();
builder.Services.AddScoped<IRequestContextProvider, WorkerRequestContextProvider>();
builder.Services.AddMediator(...);
builder.Services.AddHangfire(c => c.UsePostgreSqlStorage(...));
builder.Services.AddHangfireServer();
builder.Services.AddHostedService<RecurringJobRegistrar>();
```

`IHttpContextAccessor`-stub behövs **INTE** om DI-splittring är ren (Worker registrerar inte HTTP-impls). `AddLogging` är auto-registrerat av `Host.CreateApplicationBuilder`. **`WorkerSystemUser` kan vara Singleton** — konstant state.

**Q6 — Hangfire-paket:** Du behöver `Hangfire.AspNetCore` **trots** Worker är Generic Host. Paketet är felnamngivet — det innehåller `IServiceScopeFactory`-baserad `JobActivator` (`AspNetCoreJobActivator`) som fungerar med vilken `IServiceCollection`-DI som helst. Alternativet är att skriva egen `JobActivator` mot `IServiceProvider` — onödigt. Använd `Hangfire.AspNetCore` + dokumentera namnförvirringen i kommentar.

**Q7 — RecurringJob-registrering:** **Inte** efter `host.Build()` direkt. **Använd `IHostedService`**:

```csharp
public sealed class RecurringJobRegistrar(IRecurringJobManager mgr) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        mgr.AddOrUpdate<DetectGhostedApplicationsJob>(
            "detect-ghosted",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(3));
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Idempotens via `AddOrUpdate` är OK. Hosted service-mönstret är standard i .NET-Hangfire-deploys.

**Q8 — Saknat scope-kritiskt:**

| Concern | Status |
|---|---|
| **Hangfire migrations** | Lös via `SchemaName = "hangfire"` + `PrepareSchemaIfNecessary` (V6). EF Migrations rör inte hangfire-schema. |
| **Concurrency-skydd** | Hangfire har distributed lock på recurring-job-execution. MarkGhosted är dessutom idempotent (handler returnerar `Result.Success()` om Status redan Ghosted). Säkert. |
| **Health-check-endpoint** | Worker är inte HTTP-server — inget endpoint behövs. För Fargate health: `IHostApplicationLifetime.ApplicationStarted`-signal. Defereras till Fas 0-deploy-task. |
| **Logging/observability i Worker** | Auto-konfig via `Host.CreateApplicationBuilder`. **Serilog-bootstrap** ska läggas till explicit för att matcha Api:s konfig — annars hamnar Worker-loggar i default Console-sink, inte Seq. |
| **`DateTimeOffset` vs `DateTimeProvider.UtcNow`** | Verifierat — `IDateTimeProvider.UtcNow` returnerar `DateTimeOffset`. Konsistent. |

**Saknat som ej nämndes men värt överväga:**

- **Seq i Worker:** `UseSerilog` med samma konfig som Api.
- **Hangfire-server-options:** `WorkerCount = Environment.ProcessorCount` är default men i Fargate-container blir det 1. Sätt explicit `WorkerCount = 4` för IO-bundna jobs.
- **`MarkGhostedCommand` saknar `IAuthenticatedRequest`:** Bekräftat — XML-doc säger detta är medvetet. Worker-`ICurrentUser` returnerar `IsAuthenticated = false`, AuthorizationBehavior släpper igenom. Korrekt.

---

## Referenser

- CLAUDE.md §2.1 — Clean Architecture lager-regler
- CLAUDE.md §2.2 — DDD: events är sanningen, aggregat skyddar invarianter
- CLAUDE.md §2.3 — Pipeline-ordning (utökad till 5 i ADR 0022)
- CLAUDE.md §3.6 — `.AsNoTracking()` default
- CLAUDE.md §5.1 — `DateTime.UtcNow` förbjudet
- BUILD.md §schema rad 715–727 — `applications`-tabellen
- BUILD.md §3.1 — Hangfire 1.8.x, EF Core 10, Mediator 3.x
- ADR 0001, 0008, 0009, 0010, 0011, 0022

---

## Klas:s tillägg (post-validering)

Tre tillägg bestämda av Klas efter denna rapport:

1. **Backfill (Fas 9.2):** `last_status_change_at = NOW()` vid migration — *inte* `updated_at`-backfill (skulle göra gamla apps omedelbart Ghosted-flaggade vid första cron). Befintliga apps får 21-dagars-fönster räknat från migrationsdatum. Dokumenteras i migration-kommentar + ADR 0023.
2. **Status-set (Fas 9.1 + StaleApplicationSpecification):** Behåll snäv {Submitted, Acknowledged} för Fas 1. Dokumenteras explicit i ADR 0023 — "Definition of stale: transient-states där företaget förväntas svara. Intervju-states betraktas active oavsett kalendertid." TD-post för utökning till InterviewScheduled när första fall rapporteras.
3. **Fas 9.7 omdefinieras** — från manuellt smoke-test till integration-test:
   - `JobbPilot.Worker.IntegrationTests` (nytt projekt)
   - `DetectGhostedApplicationsJobIntegrationTests` med Testcontainers Postgres
   - `[Trait("Category", "SmokeTest")]` — kör via `dotnet test --filter "Category=SmokeTest"`
   - CC äger smoke-testet helt; Klas läser pass/fail
   - Fallback: `scripts/smoke-test-ghost-detection.sh` om Testcontainers blir för mycket scope
