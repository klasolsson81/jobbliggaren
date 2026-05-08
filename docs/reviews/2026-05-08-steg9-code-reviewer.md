---
review: code-reviewer
fas: STEG 9 (pre-commit)
datum: 2026-05-08
status: Approved with Minors — inga blockers, inga major
relaterade-adr: 0001, 0008, 0009, 0010, 0011, 0019, 0022, 0023 (ny)
relaterad-build: §3.1, §5, §16, §18
upstream-review: docs/reviews/2026-05-08-steg9-dotnet-architect.md
---

# code-reviewer — STEG 9 pre-commit

## Sammanfattning

**Status:** Approved with Minors. Inga blockers. Inga major.

STEG 9-implementationen håller över alla sex granskningsområden (Clean
Architecture, DDD, CQRS, Tests, Conventions, Anti-patterns). Pre-implementation-
rapportens K1, K2, V3, V4, V5, V6, V7 är alla synligt åtgärdade i diff:en. Klas:s
tre tillägg (NOW()-backfill, snäv {Submitted, Acknowledged}, integration smoke-
test) är likaså uppfyllda.

Den smyg-bugg som fångades under Fas 9.7 (Mediator.SourceGenerator 3.0.2
ignorerar `options.PipelineBehaviors` när källan är ett fält i stället för
inline-array-literal) är allvarlig — utan smoke-testet hade STEG 8:s audit-
infrastruktur tyst slutat persistera audit-rader när Worker dispatch:ade
`MarkGhostedCommand`. Fixen via `AddMediatorPipelineBehaviors()` + open-generic
DI är rätt mönster. Architecture-testet `MediatorPipeline_should_have_expected_
behaviors_in_order` plus integration-testet `RunAsync_StaleSubmittedApplication_
TransitionsToGhostedAndWritesAuditEntry` ger nu kombinerad regression-täckning.

Nedan listas mindre åtgärder och svar på de sex specifika frågorna. Inget
blockerar commit + push.

---

## Blockers

Inga.

---

## Major

Inga.

---

## Minor

### M1 — Audit-test verifierar inte correlation-ID-konsistens

**Fil:** `tests/JobbPilot.Worker.IntegrationTests/Jobs/DetectGhostedApplications
JobIntegrationTests.cs:97-122`

**Vad:** `RunAsync_StaleSubmittedApplication_TransitionsToGhostedAndWrites
AuditEntry` verifierar `entry.UserId == null` men assertar inte att
`entry.CorrelationId` faktiskt är icke-tom Guid och unik per scope.

**Föreslås:** Lägg `entry.CorrelationId.ShouldNotBe(Guid.Empty)` plus — om du
seedar två apps i samma run — `entry1.CorrelationId.ShouldBe(entry2.
CorrelationId)` *inom samma jobb-scope* och `entry1.CorrelationId.ShouldNotBe
(otherJobEntry.CorrelationId)` mellan separata jobb-runs. Det stänger K1-fixet
(WorkerCorrelationIdProvider med cached `_id`) regression-mässigt.

**Motivering:** K1-fixet är just det subtila beteende som inte syns i unit-test
men kan tyst bryta correlation-link mellan multipla audit-skrivningar i en
framtida orchestrator. Smoke-test är rätt nivå för det.

**Severity:** Minor (inte blocker — beteendet är korrekt, men oprovat).

### M2 — `WorkerTestFixture.MigrateAsync` migrerar AppDbContext men inte Identity

**Fil:** `tests/JobbPilot.Worker.IntegrationTests/Common/WorkerTestFixture.cs:64`

**Vad:** Fixture kör `AppDbContext.Database.MigrateAsync()` men Worker rör
aldrig Identity, så detta är medvetet och OK. Däremot — om en framtida `audit_
log_entries`-migration får FK till `users`-tabellen via Identity-schemat går
testet sönder utan tydligt felmeddelande.

**Föreslås:** Lägg en kommentar i `WorkerTestFixture.InitializeAsync`:
"Worker ska aldrig referera Identity-tabeller — `user_id` i audit-rader är
ogranskad Guid (FK saknas medvetet per ADR 0022)."

**Severity:** Minor (defensiv kommentar, inget kodfel).

### M3 — `RecurringJobRegistrar` har inte felhantering vid Hangfire-storage-init

**Fil:** `src/JobbPilot.Worker/Hosting/RecurringJobRegistrar.cs:18-26`

**Vad:** Om `IRecurringJobManager.AddOrUpdate` kastar (t.ex. Postgres ej
tillgänglig vid host-start, schema-creation misslyckas) propageras exception
till `IHost.RunAsync` och Worker exit:ar med non-zero code. Det är delvis önskat
(fail-fast) men loggas inte explicit — Fargate-deploy kan tappa exception-
detalj i container-restart-loopen.

**Föreslås:** `try/catch` med `LoggerMessage`-source-gen-loggning av
exception innan rethrow:

```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    try
    {
        manager.AddOrUpdate<DetectGhostedApplicationsJob>(...);
    }
    catch (Exception ex)
    {
        LogRegistrationFailed(_logger, ex);
        throw;
    }
    return Task.CompletedTask;
}
```

Inte blockerande för commit — Hangfire själv loggar storage-init-fel via egen
ILogger. Men explicit logg-rad gör det tydligt vid Fargate-bootstrap.

**Severity:** Minor (defensivt — inte felaktigt nu).

### M4 — `MediatorPipelineBehaviors`-fil saknar ADR 0008-länk i namespace-rot

**Fil:** `src/JobbPilot.Application/Common/MediatorPipelineBehaviors.cs:1-22`

**Vad:** XML-doc är välskriven och refererar ADR 0008 + ADR 0022, men ADR 0023
(där Mediator.SourceGenerator-bugen + DI-mönstret dokumenteras) är inte
omnämnd. När ADR 0023 commit:as ska XML-doc kompletteras.

**Föreslås:** Efter att ADR 0023 är skriven (Fas 9.8), lägg till en mening:
"Mediator.SourceGenerator 3.0.2 läser inte `options.PipelineBehaviors` från
fält-references vid compile-time — se ADR 0023 för rationale."

**Severity:** Minor (cross-reference, inte kodfel).

### M5 — `DetectGhostedApplicationsJob` saknar max-batch-size-guard

**Fil:** `src/JobbPilot.Application/Applications/Jobs/GhostedDetection/Detect
GhostedApplicationsJob.cs:42-58`

**Vad:** Klas:s fråga 5 — om systemet plötsligt har 10 000 stale apps en dag
(backfill-misstag, JobTech-import som bumpar 10k apps till Submitted samtidigt)
kommer jobbet köra ~10 000 sekvensiella `mediator.Send` mot Postgres innan det
tar slut. Det är inte direkt farligt (idempotent, isolerat per app, daglig
kadens) men kan förvåna under första prod-deploy.

**Föreslås:** Acceptabelt att deferra till tech-debt. Lägg en TD-post:
"Max-batch-size-guard i `DetectGhostedApplicationsJob` — abort + larma-logg om
`staleIds.Count > 1000` (eller konfigurerbar threshold). Förhindrar runaway-
processing vid datakorruption eller bulk-import-incidenter." Aktivera när
första bulk-scenario testas i Fas 2.

Att inte ha guarden nu är OK för Fas 1-volym (50–100/dag, default 21-dagars
window). Min poäng är att det ska vara *medvetet* deferrat, inte glömt.

**Severity:** Minor (defensivt mönster, deferrat med spårning).

### M6 — `WorkerSystemUser` returnerar `null` för `Email` — verifiera att audit accepterar

**Fil:** `src/JobbPilot.Worker/Auditing/WorkerSystemUser.cs:21`

**Vad:** `WorkerSystemUser.Email => null` är korrekt för system-jobb. Men
om en framtida `IAuthenticatedRequest`-impl eller pipeline-behavior antar
icke-null `Email` när `IsAuthenticated == false` blir det NRE i Worker.

**Föreslås:** Inget kodfel just nu — men ett architecture-test som verifierar
"`AuthorizationBehavior` släpper igenom commands som inte implementerar
`IAuthenticatedRequest` även när `ICurrentUser.IsAuthenticated == false`" hade
varit värdefullt. Sannolikt redan testat i `Application.UnitTests` —
verifiera om så och annars deferra till tech-debt.

**Severity:** Minor (förebyggande, ej brutet beteende).

### M7 — `JobbPilot.sln`: x64/x86-platforms tillagda

**Fil:** `JobbPilot.sln` (diff:ad)

**Vad:** sln-filen har fått ~80 nya `Debug|x64`/`Release|x86`-rader. Det är
sannolikt automatiskt tillagt av Rider/VS när `JobbPilot.Worker.IntegrationTests`
lades till — inte medvetet val.

**Föreslås:** Inget krav — bara FYI. Om CI bygger på `Any CPU` triggas inget
fel. Men det "rotar" sln-filen och gör framtida diff:ar bullriga. Klas kan
välja att städa innan commit eller låta vara.

**Severity:** Minor (kosmetiskt).

---

## Bra gjort

- **Pre-implementation-rapportens åtgärder är synligt på plats.** Specifikt:
  - K1 (`WorkerCorrelationIdProvider._id` cache) — `private readonly Guid _id =
    Guid.NewGuid();` på rad 20.
  - K2 (Worker laddar inte `AddHttpAuditing`) — Worker/Program.cs registrerar
    direkt mot stub-typer; `WorkerLayerTests.Worker_should_not_depend_on_
    AspNetCore_Http_or_Identity` cementerar det.
  - V3 (StaleApplicationSpecification i Application-lagret, inte aggregat-
    metod) — implementerat utan `IsStale`-metod på `Application.cs`.
  - V4 (LastStatusChangeAt flippas STRIKT i Create/TransitionTo/MarkGhosted) —
    verifieras av 9 unit-tests inkl. `Application_AddFollowUp_DoesNot
    UpdateLastStatusChangeAt`, `Application_AddNote_DoesNotUpdate
    LastStatusChangeAt`, `Application_SoftDelete_DoesNotUpdateLast
    StatusChangeAt`.
  - V5 (AsNoTracking + select-projection + cancel-token + progress-log) —
    samtliga närvarande i `DetectGhostedApplicationsJob`.
  - V6 (Hangfire schema=hangfire, PrepareSchemaIfNecessary) — exakt så.
  - V7 (UTC + dokumenterat) — kommentar-block i `RecurringJobRegistrar`.

- **Klas:s tre tillägg är uppfyllda:**
  - Migration backfillar `last_status_change_at = NOW()` med tydlig motivering
    i kommentar varför `updated_at` *inte* används.
  - Status-set i specification snäv {Submitted, Acknowledged} med XML-doc-länk
    till ADR 0023-rationale.
  - Fas 9.7 är integration-test (Testcontainers Postgres) med Trait-
    kategorisering, inte manuellt smoke-test.

- **Mediator-pipeline-bug-fix är konceptuellt korrekt.** `AddScoped(typeof
  (IPipelineBehavior<,>), behaviorType)` är det enda mönstret Mediator runtime
  faktiskt använder för pipeline-resolution (via `GetServices<>()`).
  Registrerings-ordningen i `MediatorPipelineBehaviors.InOrder` matchar
  exekverings-ordningen (Logging först, AuditBehavior innerst), och delningen
  mellan Api + Worker via samma extension-metod garanterar non-drift.

- **Architecture-test-täckning för Worker-lagret är ny och rätt nivå.**
  Fem tester som var och en täcker en distinkt invariant: AspNetCore-isolation,
  job-bo-i-Application-lagret, stub-implementerar-portar, registrar-bo-i-
  Worker-assembly, pipeline-konstant-stabilitet. Sista är direkt regression-
  värn mot framtida revertering till `options.PipelineBehaviors`-mönstret
  (även om det inte fångar frånvaro av `AddMediatorPipelineBehaviors()`-anrop
  — se Q1-svar nedan).

- **Smoke-test täcker rätt yta.** EF-translation av specification mot Npgsql,
  Mediator-pipeline med 5 behaviors, atomisk UoW-persistens (Status flip +
  audit-rad), Worker-stub-paritet (`user_id == null`). Sex deterministiska
  scenarier inkl. soft-deleted-filter via `IgnoreQueryFilters` — exakt det yta
  som Fas 9.6-buggen exponerade.

- **DDD-disciplin:** Aggregat-properties har `private set`, inga public
  setters. State-mutationer (TransitionTo, MarkGhosted) är invariant-skyddade
  och raisar domain events. `LastStatusChangeAt` muteras *inte* utanför
  aggregatets metoder. Specification i Application-lagret tar `Expression<Func<
  Application, bool>>` — ingen domän-typ läcker via API-yta.

- **CQRS:** Inga MediatR-imports någonstans. `[Handler]`-mönstret bibehållet i
  `MarkGhostedCommandHandler` (omodifierad). Orchestrator-jobbet är *inte* en
  handler — det är en pure orchestrator som dispatch:ar via mediator, vilket
  matchar single-responsibility-principen.

- **Conventions:** Nullable enabled, file-scoped namespaces, primary
  constructors där det hjälper, `Async`-suffix, `CancellationToken` propagerad
  hela vägen, `IDateTimeProvider` överallt — inga `DateTime.UtcNow`-direktanrop.
  `LoggerMessage`-source-gen för structured logging i `DetectGhosted
  ApplicationsJob`.

- **Newtonsoft.Json 13.0.3 CVE-pinning är välmotiverad** med komplett kommentar
  i `Directory.Packages.props` (CVE-ID, GHSA-ID, sårbarhetsbeskrivning,
  rationale för pinning). Detta är pedagogiskt skrivet och håller framtida
  uppgraderings-rationale.

- **Migrations-backfill-strategin är genomtänkt.** Att välja `NOW()` framför
  `updated_at`-kopia är icke-trivialt och rätt val — kommentaren i
  migration-filen förklarar varför med konkret negativt scenario ("alla
  ansökningar med updated_at äldre än 21 dagar skulle omedelbart klassas
  Ghosted vid första cron-körning"). Add-column-as-nullable → backfill →
  ALTER-to-NOT-NULL är standardmönstret för obligatoriska kolumner på
  befintlig tabell — korrekt utfört.

---

## Svar på Klas:s 6 frågor

### Q1 — `AddMediatorPipelineBehaviors`-mönstret + compile-time-skydd

**Mönstret är korrekt.** Open-generic DI-registrering av `IPipelineBehavior
<,>` är det enda sättet Mediator runtime faktiskt resolvar pipeline — det
står även i Mediator-källan att `options.PipelineBehaviors` används som hint
för source-generator vid compile-time (för att veta vilka behaviors som ska
finnas i den genererade typen) — och om det inte translaterar (som i 3.0.2
med fält-references) tystnar pipelinen utan diagnostik.

**Compile-time-skydd:** Du har redan ett *strukturellt* skydd via
`WorkerLayerTests.MediatorPipeline_should_have_expected_behaviors_in_order`
— men det testet verifierar bara att `MediatorPipelineBehaviors.InOrder`-
listan är intakt. Det fångar inte bortglömt anrop till
`AddMediatorPipelineBehaviors()` i en framtida composition root (t.ex. ny
Worker-host eller test-fixture).

**Föreslagen tilläggs-test (deferra som tech-debt eller lägg in nu —
trivial):**

```csharp
[Fact]
public void Api_DI_should_register_all_expected_pipeline_behaviors()
{
    using var factory = new WebApplicationFactory<Program>();
    var sp = factory.Services;
    var behaviors = sp.GetServices<IPipelineBehavior<MarkGhostedCommand, Result>>().ToList();
    behaviors.Count.ShouldBe(MediatorPipelineBehaviors.InOrder.Length);
}
```

Detta + motsvarande för Worker-test-fixture stänger DI-resolution-regression-
risken. Detta är *inte* blocker för STEG 9 — testet i smoke-suite redan
verifierar end-to-end (audit-rad skrivs ⇒ pipeline körs). Men explicit DI-test
hade varit billigare diagnostik vid framtida regression.

**Rekommendation:** Lägg in som tech-debt-post (TD-17?), inte i STEG 9-scope.

### Q2 — `MediatorPipelineBehaviors`-fil-placering

**Application/Common/ är korrekt** — *inte* Application/Common/Behaviors/.

Rationale: `MediatorPipelineBehaviors`-typen är en *cross-cutting registrerings-
konstant* — den orchestrerar relationen mellan flera behaviors. Att flytta in
den under `Behaviors/`-mappen blandar registrerings-yta med behavior-impls
(SRP-brott på namespace-nivå). Mappen `Common/` är där delade Application-
infrastruktur-typer bor (`MediatorPipelineBehaviors`, framtida
`ApplicationServiceCollectionExtensions` etc.). Behaviors/-mappen är
*implementations*-mapp.

Argument *för* `Behaviors/`-placering hade varit kohesion (alla pipeline-
relaterade typer på ett ställe). Men typen importerar både
`Common.Behaviors.*` och `Common.Auditing.AuditBehavior` — den crossar bara
två sub-mappar och hör inte hemma i någondera.

**Verdikt:** Behåll i Application/Common/. Lägg eventuellt en kort
namespace-doc-kommentar i en `_namespace.md` eller XML-doc som förklarar
namespace-konventionen — defereras till tech-debt.

### Q3 — Hangfire-paket i Worker.csproj och assembly-konflikter

**Inga assembly-konflikter.** Verifierat:

1. Inget annat projekt drar in Hangfire (verifierat via grep).
2. `Hangfire.AspNetCore` namnet är vilseledande — paketet innehåller bara
   `IServiceScopeFactory`-baserad `JobActivator`, inga ASP.NET Core-runtime-
   klasser. Det laddar inte `Microsoft.AspNetCore.Hosting` eller similar.
3. Worker.IntegrationTests refererar Worker → drar in Hangfire transitivt,
   ja. Men `WorkerLayerTests` kör inte i samma assembly (det är
   `JobbPilot.Architecture.Tests`-assembly), så architecture-test-yta för
   "Worker should not depend on AspNetCore" verifierar fortfarande Worker-
   produktions-assemblyn — inte Worker.IntegrationTests.

**Verifiera följande.** `WorkerLayerTests.Worker_should_not_depend_on_
AspNetCore_Http` testar `typeof(WorkerSystemUser).Assembly` — det är
JobbPilot.Worker.dll. Hangfire.AspNetCore-paketet drar inte in
`Microsoft.AspNetCore.Http`-namespacet (det drar in
`Microsoft.AspNetCore.Builder`-typen `IApplicationBuilder` för optional
dashboard-extension-metoder, men dessa är inte refererade av Worker-koden).

Om architecture-testet passerar i CI = ingen läckage. Om det failar i
framtiden vid Hangfire-uppgradering är det rätt signal: Hangfire-paketets
yta har växt, undersök innan uppgradering.

**Verdikt:** OK som det är. Lägg eventuellt en
`Hangfire.AspNetCore`-namnkommentar i Directory.Packages.props (Klas
nämner missnamnet — explicit doc hjälper framtida läsare).

### Q4 — Worker.cs borttagning och dolda referenser

**Inga dolda referenser hittade.** Verifierat:

- `grep -rn "JobbPilot.Worker.Worker"` — inga träffar (förutom .csproj-filen
  som inte refererar typen, bara projektet).
- `grep -rn "AddHostedService<Worker>"` — inga träffar.
- Ingen test-fil i tests/ refererar Worker-typen.

Borttagning är säker. Hangfire-server (`AddHangfireServer`) ersätter
heartbeat-funktionaliteten — Worker-host kör Hangfire som långkörande
hosted-service.

### Q5 — Per-id `mediator.Send` och max-batch-size-guard

**Acceptabelt val för Fas 1.** Audit-paritet och isolering är de avgörande
argumenten — exakt det som arch-rapport V5/Q3 sade. Per-id-loop matchar
ADR 0022:s 1:1-relation mellan command och audit-rad.

**Max-batch-guard** — se M5. Min rekommendation: deferra som tech-debt med
en konkret threshold (1000) och larma-mekanism. Inte blocker för commit.

**Operativt skydd som finns:** `cancellationToken.ThrowIfCancellation
Requested()` i loopen → om Worker tas ner under en runaway-scan kan
container-shutdown abortas korrekt. Hangfire-job:et kommer sedan retrya enligt
default-retry-policy (10 försök med exponential backoff) — också idempotent.

### Q6 — `Newtonsoft.Json` 13.0.3 och framtida CVE-uppgraderings-mönster

**13.0.3 är rätt val just nu.** Det är senaste stabila version (13.0.3 från
2023-03 är fortfarande aktuell — Newtonsoft.Json är i underhållsläge sedan
System.Text.Json blev default i .NET).

**Framtida CVE-uppgraderings-mönster:**

Eftersom JobbPilot inte använder Newtonsoft direkt, är pinningen *bara* för
att tillfredsställa `TreatWarningsAsErrors` när `dotnet list package
--vulnerable` rapporterar transitiva träffar. Mönstret framåt:

1. När ny Newtonsoft.Json-CVE rapporteras: kolla om `Hangfire.PostgreSql`
   eller andra transitiva-konsumenter har uppgraderat till fixed version.
2. Om ja: ta bort `Newtonsoft.Json`-pinning från `Directory.Packages.props`
   helt — låt transitiva paketet sätta versionen.
3. Om nej: bumpa pinningen till fixed version, dokumentera CVE-ID i
   kommentaren bredvid version-numret.

**Föreslagen åtgärd:** Lägg ett kommentar-block med uppgraderings-pattern.
Defereras till tech-debt (TD-18?) — inte STEG 9-scope. Den nuvarande
kommentaren är redan tydlig om *varför* pinningen finns.

---

## Sammanfattande beslut

**Status:** Approved with Minors. Direct-push godkänd när Klas:s manuella
diff-granskning + `git diff`-paste-verifiering (CLAUDE.md §9.4) är klar.

**Minors:** 7 stycken, samtliga deferable till tech-debt eller defensiva
förbättringar. Inga blockerar commit + push.

**Tech-debt-poster att skapa post-commit:**
- TD: M1 — correlation-ID-konsistens-test i smoke-suite
- TD: M5 — max-batch-size-guard i orchestrator
- TD: M6 — architecture-test för AuthorizationBehavior + non-authenticated commands
- TD: Q1 — DI-resolution-test för pipeline-behaviors (Api + Worker)
- TD: Q6 — Newtonsoft.Json-uppgraderings-pattern dokumenterad

**Bra gjorts som förstärker:**
- K1/K2 + V3-V7 + Klas:s tre tillägg är alla synligt på plats
- Mediator-pipeline-buggen är fångad av smoke-test + architecture-test (regression-värn)
- Arkitektur-disciplinen håller (Worker isolerad från HTTP-bagage)
- DDD-disciplinen håller (LastStatusChangeAt strikt-flippad)
- CQRS-disciplinen håller (orchestrator separerad från handlers, inga MediatR-imports)
- Test-pyramid balanserad (157 unit + 169 application + 15 architecture + 6 integration smoke + 104 api integration)

**Klar för commit.** Granskat enligt CLAUDE.md §2 (Clean Arch + DDD + CQRS),
§3 (.NET-standarder), §5 (anti-patterns), och §6.3 (granskningsspärrar
ersätter PR-flöde).
