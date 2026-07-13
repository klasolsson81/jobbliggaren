# ADR 0035 — System-event audit-pipeline (bypass-port parallell till IAuditTrailEraser)

**Datum:** 2026-05-13
**Status:** Accepted 2026-05-13 (senior-cto-advisor decision per CLAUDE.md §9.6 punkt 5)
**Kontext:** TD-73 prod-gating-batch (ADR 0032 §8 amendment 2026-05-12 punkt 4) — audit-wire α för Hangfire-jobben som orchestrar JobTech-import.
**Beslutsfattare:** senior-cto-advisor 2026-05-13 + dotnet-architect design-skiss
**Relaterad:** ADR 0022 (audit log-pipeline — pipeline-behavior för commands), ADR 0023 (Hangfire-infrastruktur), ADR 0024 (`IAuditTrailEraser` bypass-port — precedens), ADR 0032 (JobTech-integration — konsumenter av denna port), CLAUDE.md §2.1 (Clean Arch), §9.6 (in-block-fix-default)

## Kontext

ADR 0022 etablerar audit-pipelinen som **pipeline-behavior på Mediator-commands** med `IAuditableCommand`-marker. Pipelinen kräver att mutationen går via Mediator för att audit-raden ska skrivas i samma transaction som data-mutationen (`AuditBehavior` lägger till `AuditLogEntry`, `UnitOfWorkBehavior` persisterar atomiskt).

P8c-leveransen (commit `81dfab6`) införde tre Hangfire RecurringJobs (`SyncPlatsbankenStreamJob`, `SyncPlatsbankenSnapshotJob`, `PurgeStaleRawPayloadsJob`). Dessa **orchestrerar** snarare än mutater själva — de itererar items och kallar `mediator.Send(UpsertExternalJobAdCommand)` per item, eller kör `ExecuteUpdateAsync` mot `IAppDbContext`. Jobben själva passerar inte `AuditBehavior` eftersom de inte är `IRequest`/`ICommand`-implementationer.

ADR 0032 §8 amendment 2026-05-12 punkt 4 specificerade ursprungligen `JobAdsSyncedDomainEvent` + `RawPayloadPurgedDomainEvent` som audit-mekanism. Men JobbPilots domain-event-dispatcher finns inte (ADR 0022 alt C avvisat). Att införa en dispatcher enbart för system-event-audit är arkitektonisk över-spec.

GDPR Art. 30 (record of processing) kräver att JobTech-import som behandlings­aktivitet kan redovisas — counts (fetched/added/archived/skipped/errors), källa, tidsinterval. Det är **operational accountability**, parallell till men distinkt från command-mutations-accountability som ADR 0022 täcker.

ADR 0024 etablerade `IAuditTrailEraser` som dedikerad **bypass-port** för Art. 17-anonymisering — direkt-write till audit_log utanför pipeline-behaviorn, men låst via architecture-test till en enda konsument (`HardDeleteAccountsJob`). Det mönstret är direkt återanvändbart för system-events.

Frågan i denna ADR är: **hur skrivs accountability-rader för system-orchestrerade aktiviteter (Hangfire-jobb) utan att bryta ADR 0022:s pipeline-disciplin?**

## Beslut

**Ny bypass-port `ISystemEventAuditor`** i `Application/Common/Auditing/`, parallell till `IAuditTrailEraser`. Konsumenter är system-jobben själva (Hangfire RecurringJobs). Architecture-test låser konsumentlistan så att porten inte sprids till command-handlers.

### 1. Port-yta + typsäker event-hierarki

```csharp
namespace JobbPilot.Application.Common.Auditing;

public interface ISystemEventAuditor
{
    Task RecordAsync(SystemAuditEvent evt, CancellationToken ct);
}

public abstract record SystemAuditEvent(
    string EventType,
    string AggregateType,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    string? Payload);

public sealed record JobAdsSynced(...) : SystemAuditEvent(...);
public sealed record RawPayloadPurged(...) : SystemAuditEvent(...);
public sealed record RecruiterPiiRedacted(...) : SystemAuditEvent(...);
```

Diskriminerad sealed record-hierarki (CLAUDE.md §3.3 — Value Objects = record sealed). Varje konkret event bär typade payload-fält som serialiseras till `audit_log.payload` jsonb-kolumnen.

**EventType-konvention:** `System.<Event>` (t.ex. `System.JobAdsSynced`, `System.RawPayloadPurged`). Diskriminerar från command-audit-events (`Application.Submitted`, `Account.Deleted`).

**AggregateType-konvention:** `System.<Aggregate>` (t.ex. `System.JobAdSync`, `System.RawPayloadPurge`, `System.RecruiterPiiRedaction`). Audit-fältet bär referens-typ för admin-vy-filtrering; system-events har ingen aggregate-root i Domain, men compliance-modellen tillåter det.

### 2. AggregateId via Hangfire jobId (Q1 CTO-decision)

`AuditLogEntry.Create` förbjuder `Guid.Empty`-AggregateId (invariant). För system-events används **per-run-Guid genererad i jobbet**, propagerad via `SystemAuditEvent.AggregateId`. När Hangfire ger oss en jobId per execution återanvänds den (parsas till Guid via deterministisk hash om Hangfire-jobId-format är string, eller direkt om Guid).

**Motivering (Evans 2003 + Martin 2017 kap. 8 OCP):**

- AggregateId representerar identitet för en *processed unit*. Sync-run är unit:en.
- OCP-vinst: framtida `JobAdsSyncStarted`-event (Fas 4) kan dela AggregateId med befintlig completed-event utan schema-ändring → naturlig korrelation.

Avvisat: deterministisk hash av `(source, jobType, startedAt-minute)` förvirrar accountability-läsning (samma AggregateId över multiple runs ger illusion av dedupering). `Guid.NewGuid()` per call är YAGNI-likvärdigt men förkastar Hangfire-jobId som vi får gratis.

### 3. `AuditLogEntry.CreateSystemEvent` — ny factory, bevarar invariant

`AuditLogEntry` får ny static factory `CreateSystemEvent(...)` som tar `payload` (string?, serialized JSON) som ytterligare argument. Befintlig `Create(...)` är oförändrad för command-audit. Invarianten `aggregateId != Guid.Empty` bevaras i båda — system-events MÅSTE ha non-Empty AggregateId.

```csharp
public static AuditLogEntry CreateSystemEvent(
    DateTimeOffset occurredAt,
    Guid correlationId,
    string eventType,
    string aggregateType,
    Guid aggregateId,        // Guid.Empty fortfarande förbjudet
    string? payload)         // serialized JSON
```

`user_id`, `ip_address`, `user_agent`, `impersonated_by` = `null` per design (system har ingen request-context).

### 4. `audit_log.payload` jsonb-kolumn aktiveras (ADR 0022-komplettering)

ADR 0022 §"Audit-radens innehåll" deklarerade `payload` som "reserverad, förblir null i Fas 1". Denna ADR aktiverar kolumnen för **system-events** i Fas 2. Command-audit (`IAuditableCommand`) skriver fortfarande `payload = null` — PII-saneringskravet för command-payloads (CV-text, etc.) är oförändrat och defereras till Fas 4 enligt ADR 0022.

**Schema-impact:** Ny migration `AddAuditLogPayload` lägger `payload jsonb NULL` på `audit_log` (partitionerad parent — propageras automatiskt till alla partitions per PostgreSQL native partitioning-semantik). EF-config utökas med `.HasColumnType("jsonb")`.

**Payload-strategi:** System-events serialiseras via `System.Text.Json` med runtime-type-dispatch (`JsonSerializer.Serialize(evt, evt.GetType())`). Lagras som `string?` i Domain-property `AuditLogEntry.Payload` — inte `JsonDocument?` (bryter entity-immutability och kräver IDisposable-lifetime-hantering i Domain).

### 5. Implementation: `SystemEventAuditor` (Infrastructure)

```csharp
namespace JobbPilot.Infrastructure.Auditing;

internal sealed class SystemEventAuditor(
    IAppDbContext db,
    ICorrelationIdProvider correlationIdProvider) : ISystemEventAuditor
{
    public async Task RecordAsync(SystemAuditEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(evt, evt.GetType());

        // Idempotens-skydd vid Hangfire-retry (R-Risk1):
        // Om audit-rad redan finns för denna jobRun, skip.
        var alreadyRecorded = await db.AuditLogEntries
            .AnyAsync(a => a.EventType == evt.EventType
                        && a.AggregateId == evt.AggregateId, ct);
        if (alreadyRecorded) return;

        var entry = AuditLogEntry.CreateSystemEvent(
            occurredAt: evt.OccurredAt,
            correlationId: correlationIdProvider.Current,
            eventType: evt.EventType,
            aggregateType: evt.AggregateType,
            aggregateId: evt.AggregateId,
            payload: payload);

        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
```

DI-lifetime: **Scoped** (matchar `IAppDbContext`).

Insert via `.Add` är idiomatic EF — skillnad mot `IAuditTrailEraser` som använder `ExecuteSqlAsync` (mutation av existerande rader, write-only-bypass). Insert är vanlig persistence utan Mediator-wrapper.

### 6. Best-effort-semantik vid system-event-audit (R-Risk1 CTO-decision)

System-jobben kör per-item Mediator-commands (`UpsertExternalJobAdCommand` med egen UoW per item). Audit-eventet skrivs **efter** alla items processade, i en separat `SystemEventAuditor.RecordAsync` → egen `SaveChangesAsync`. **Inte atomic med jobb-arbetet.**

**Mitigering:**

1. **Hangfire automatic retry** vid audit-write-failure. Idempotens-checken (steg 5) gör retry safe — duplicate audit-rader undviks via AggregateId-lookup.
2. **Critical-log + CloudWatch-alarm** efter retry-exhaustion.
3. Detta är **inte** Art. 5(2)-brott — Art. 5(2) säger "be able to demonstrate compliance", inte "perfekt rad per händelse". Sanitizer + retention är de primära GDPR-mekanismerna; system-event-audit är operationell observability + Art. 30 record-of-processing.

**Motivering (Fowler 2018 "Patterns of Distributed Systems" + Saltzer/Schroeder fail-safe defaults):** Best-effort med observability är rätt fail-safe. Distributed transaction över Hangfire-scope + audit-write är över-spec för F2-volym. Avvisat: outbox-mönster (skapar ny infra-yta utan motiverad vinst).

### 7. Architecture-test mirror:ar `IAuditTrailEraser`-pattern

```csharp
[Fact]
public void ISystemEventAuditor_should_only_be_referenced_by_system_jobs()
{
    var allowedConsumers = new[]
    {
        "SyncPlatsbankenStreamJob",
        "SyncPlatsbankenSnapshotJob",
        "PurgeStaleRawPayloadsJob"
    };

    var result = Types.InAssembly(ApplicationAssembly)
        .That()
        .HaveDependencyOn("JobbPilot.Application.Common.Auditing.ISystemEventAuditor")
        .Should()
        .HaveNameMatching(string.Join("|", allowedConsumers))
        .GetResult();

    result.IsSuccessful.ShouldBeTrue();
}
```

**Not:** `RedactRecruiterPiiCommandHandler` (borttaget 2026-07-13, #842 — det var en
Art. 17-raderingsväg som strukturellt inte kunde radera något) var INTE konsument av
denna port — det fick sin audit-rad via `IAuditableCommand` + `AuditBehavior` (Mediator-
pipeline) per ADR 0022. Principen står kvar och gäller övriga admin-kommandon: bara
orchestrator-jobben utanför Mediator-pipelinen
konsumerar `ISystemEventAuditor`.

Konsumentlistan är låst. Ny konsument kräver explicit utökning av denna test — vilket triggar code-review.

### 8. Audit-yta för admin-triggad snapshot (M3 CTO-decision)

`SyncPlatsbankenSnapshotCommand` (admin-trigger för smoke-test) implementerar **inte** `IAuditableCommand`. System-eventet `System.JobAdsSynced` är *sanningen* för sync-runs oavsett om triggern är admin-curl eller cron-job. En audit-rad per snapshot-run, inte två.

**Motivering (Martin 2017 kap. 7 SRP + kap. 13 CCP):** Audit-källan ska vara där fakta lever (job-impl). Command är dispatch-yta.

## Konsekvenser

### Positiva

- **TD-73 punkt 4 (a) — audit-wire α — stängs.** Sync-runs och purge-runs är auditerade per GDPR Art. 30.
- **Pipeline-disciplin bevaras.** ADR 0022:s `IAuditableCommand`-pipeline är orörd; bypass-porten är arkitekt-låst via arch-test (CCP).
- **OCP-väg framåt:** framtida system-events (BedrockCostBudgetTriggered, RetentionJobCompleted, etc.) använder samma port. Schema-aktivering av `payload` är generell, inte unik för JobTech-events.
- **Idempotens vid retry** löser R-Risk1 utan distributed transaction.
- **Audit-bypass-disciplin spegelvänd** mot `IAuditTrailEraser` (ADR 0024 D3) — samma kvalitets-anchor.

### Negativa

- **Best-effort vid audit-failure.** Vid Hangfire-retry-exhaustion kan en sync-run sakna audit-rad. Mitigerat via Critical-log → CloudWatch-alarm. Acceptabelt enligt CTO-decision (system-event-audit är observability + Art. 30, inte per-mutation-accountability).
- **Två bypass-portar nu:** `IAuditTrailEraser` (mutation) + `ISystemEventAuditor` (insert). Båda låsta via arch-test. Disciplinen håller men ytan har vuxit.
- **`audit_log.payload` aktiveras tidigare än Fas 4.** ADR 0022:s Fas 4-deferral var för *command-audit-payload* (CV-text, PII-saner-behov). System-event-payload är counts + tidsstämplar — ingen PII. Tidig aktivering har ingen GDPR-impact, bara EF-mapping + migration.

### Mitigering

- Architecture test `ISystemEventAuditor_should_only_be_referenced_by_system_jobs_and_redact_handler` förhindrar tyst regression
- Idempotens-check via `(EventType, AggregateId)`-lookup innan insert — safe mot Hangfire-retry
- Integration-test (Testcontainers) per Hangfire-job verifierar audit-paritet
- Migration `AddAuditLogPayload` är icke-destruktiv: lägger nullable kolumn på partitionerad parent → alla befintliga och framtida partitions ärver

## Alternativ övervägda

### Alt A — `JobAdsSyncedDomainEvent` + domain-event-dispatcher

Avvisat. Skulle kräva att JobbPilot etablerar event-dispatcher-infrastruktur (`SaveChangesAsync`-hook som plockar `AggregateRoot.DomainEvents`, Mediator.Publish-anrop, transactional outbox). Det är ett separat arkitekturbeslut (ADR 0022 alt C-deferral) — onödig scope-svällning för TD-73-stängning.

### Alt B — Sidoeffekt i `UpsertExternalJobAdCommandHandler` (per-item audit)

Avvisat. Ger N audit-rader per sync-run istället för 1 (audit-spam). Bryter ADR 0024 D4-precedens (`DeleteAccountCommand` skriver en `Account.Deleted`-rad oavsett cascade-omfattning). En sync-run är *en* handling.

### Alt C — Logg-baserad audit via Serilog/CloudWatch + manual archive-pipeline

Avvisat. CloudWatch-logs har 30d retention (per ADR 0024 D7) — kortare än 90d audit-retention. Att splita audit mellan två kanaler (DB-tabell för command-audit, CloudWatch för system-audit) bryter CCP och gör admin-läs-yta i Fas 6 komplexare.

### Alt D — Generic `IAuditWriter` som dyker upp överallt

Avvisat. Bryter audit-bypass-disciplinen från ADR 0024 D3. `IAuditTrailEraser` är dedikerad till sin uppgift (anonymisering), `ISystemEventAuditor` är dedikerad till sin (system-events). En generic port skulle inbjuda till bypass i normala handler-flöden.

## Implementationsstatus

| Komponent | Status |
|---|---|
| `ISystemEventAuditor`-port + `SystemAuditEvent`-hierarki (Application) | Planerad — denna ADR-batch |
| `SystemEventAuditor`-impl (Infrastructure) | Planerad |
| `AuditLogEntry.Payload` + `CreateSystemEvent`-factory (Domain) | Planerad |
| `AuditLogEntryConfiguration.Payload` jsonb-mapping | Planerad |
| EF-migration `AddAuditLogPayload` | Planerad |
| Audit-wire i `SyncPlatsbankenStreamJob` + `SyncPlatsbankenSnapshotJob` + `PurgeStaleRawPayloadsJob` | Planerad |
| Architecture-test `ISystemEventAuditor_should_only_be_referenced_by_system_jobs_and_redact_handler` | Planerad |
| Unit-tester (Domain + Application) | Planerad |
| Integration-tester (Testcontainers) | Planerad |
| Cross-ref från ADR 0032 §8 punkt 4 till denna ADR | Planerad (amendment) |

## Validation

- Domain.UnitTests: `AuditLogEntry_CreateSystemEvent_*` (invariant, payload, null user_id)
- Application.UnitTests: `SystemEventAuditor_RecordAsync_SerializesPayload`, `_IsIdempotentOnRetry`, `_CallsSaveChanges`
- Architecture.Tests: `ISystemEventAuditor_should_only_be_referenced_by_system_jobs_and_redact_handler` + parallell-test `SystemAuditEvent_records_must_have_NonEmpty_AggregateId`
- Api.IntegrationTests (Testcontainers): `SyncPlatsbankenStreamJob_WritesAuditRow_AfterCompletion`, `PurgeStaleRawPayloadsJob_WritesAuditRow_OnlyWhenRowsAffected_NotEmpty`

## Referenser

- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP), kap. 8 (OCP), kap. 13 (CCP)
- Eric Evans, *Domain-Driven Design* (2003) — aggregate identity
- Martin Fowler, *Patterns of Distributed Systems* (2018) — best-effort + observability
- Saltzer/Schroeder, *The Protection of Information in Computer Systems* (CACM 1975) — fail-safe defaults
- GDPR Art. 5(2), Art. 30
- ADR 0022 (audit pipeline-behavior), ADR 0023 (Hangfire-infrastruktur), ADR 0024 (`IAuditTrailEraser` bypass-port precedens), ADR 0032 (JobTech-integration §8 amendment 2026-05-12 punkt 4)
- CLAUDE.md §2.1, §3.3, §9.6, §9.7

## Status

**Accepted** 2026-05-13. Omvärderas vid:

- Fas 4 — när AI-jobb-audit börjar lagras (mer system-event-volym, payload-storlek växer)
- Fas 6 — när admin-läs-yta över audit_log införs (filtrering per EventType `System.*` behövs)
