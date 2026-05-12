# ADR 0031 — Failed cross-user access detection: strukturerad loggning + CloudWatch-aggregat

**Datum:** 2026-05-12
**Status:** Accepted
**Kontext:** TD-67 (audit-trail för failed cross-user-access-attempts, GDPR Art. 32 anomaly-detection)
**Beslutsfattare:** Klas Olsson (via senior-cto-advisor 2026-05-12)
**Relaterad:** ADR 0022 (audit log-pipeline — bevaras oförändrad), OWASP API1:2023 (BOLA)

## Kontext

Cross-user-isolation-tester (TD-12, TD-66) bevakar att user B inte kan se eller mutera A:s data — alla cross-user-anrop returnerar 404. Men 404-händelserna loggas inte i `audit_log`-tabellen: ADR 0022 specar uttryckligen "audit skrivs endast på success, failed-attempts retrofittas i Fas 6".

Det skapar en **blind fläck för BOLA-enumeration-attack** (OWASP API1:2023): en angripare som genererar 100-tals 404 mot andra users IDs syns inte i `audit_log`. GDPR Art. 32 kräver "lämpliga tekniska åtgärder" — detektering av åtkomstförsök är en legitim ops-signal som bör finnas tillgänglig för anomaly-detection och alerting.

Frågan är **hur failed-access-attempts ska detekteras och loggas utan att**:

1. **Bryta atomicitet** — UoW rullar tillbaka vid failure, en separat SaveChanges för audit-failure-rad skapar två-fas-skrivnings-problemet.
2. **Avslöja existens** via timing eller respons-skillnader — 404 från handler ska INTE skilja sig externt mellan "okänt id" och "tillhör annan user".
3. **Bryta ADR 0022:s immutabilitet** — pipeline-strategin "audit endast på success" är låst tills Fas 6.
4. **Skapa pattern-läckage** — query-handlers (read-side) hör inte hemma med ny audit-write-logik.

## Beslut

**Hybrid: strukturerad ILogger-event + CloudWatch metric filter + SNS-alarm. Ingen `audit_log`-tabellrad. Ingen ny pipeline-behavior. Ingen supersession av ADR 0022.**

### Komponenter

1. **`IFailedAccessLogger`-port** (`Application/Common/Auditing/`) med signatur:

   ```csharp
   void LogCrossUserAttempt(
       string aggregateType,
       Guid requestedAggregateId,
       Guid requestingUserId,
       string operation);
   ```

2. **`FailedAccessLogger`-impl** (`Infrastructure/Auditing/`) — `ILogger<T>`-baserad med `LoggerMessage`-source-generator. Strukturerade fält med fasta property-namn: `event_name=failed_access_attempt`, `aggregate_type`, `requested_aggregate_id`, `requesting_user_id`, `operation`. EventId 4001, Level Warning.

3. **Handler-modifikation** i berörda Application + Resume ownership-skyddade handlers: vid ownership-mismatch (`Where(a => a.Id == id && a.JobSeekerId == jobSeekerId)` matchar inte) görs en extra existens-check (`Where(a => a.Id == id).AnyAsync()`). Om aggregatet finns men ägs av annan user → `failedAccessLogger.LogCrossUserAttempt(...)`. Okänt id (existens-check returnerar false) loggas INTE.

4. **CloudWatch-aggregat** (TD-68, separat Terraform-leverans): metric filter på `event_name=failed_access_attempt` grupperad på `requesting_user_id`, alarm vid >20 events/min/user → SNS topic `secops-anomaly`.

### Distinktion "okänt id" vs "tillhör annan"

Differentieringen sker **i error-path** (handler vet redan att första queryn matchade inte). Extra existens-query körs synkront före `throw NotFoundException` / `return null`. Klienten ser identisk 404 oavsett — inga särskiljande headers, body eller status-code.

Timing-läckage finns men är ofarligt: en angripare som skiljer mellan "okänt id" och "tillhör annan" via ~1-2ms latency-skillnad lär sig endast information de redan har via brute-force-enumeration på Guid-space (2^122 entropy, operationellt orealistiskt). `LoggerMessage`-source-gen är synchronous (loggar i anropande tråd före retur) — vi accepterar denna overhead i error-path som proportionellt skydd (BOLA-detection > 1-2ms extra latency vid 404).

### Pipeline-yta

**Ingen.** `AuditBehavior` (ADR 0022) förändras inte. Inga nya pipeline-behaviors. Inga ändringar i UoW. Logger-anropet är ett rakt service-anrop från handler — samma pattern som `IAuthAuditLogger` används idag.

## Konsekvenser

### Positiva

- **ADR 0022 bevarad immutable** — pipeline-strategin "audit endast på success" stannar låst tills Fas 6.
- **SoC bevarad** — audit_log är compliance-artefakt (90d retention, partitionerad), failed-access är ops-signal (CloudWatch retention, anomaly-detection). Olika livscykler, olika konsumenter, olika moduler.
- **Ingen atomicitets-utmaning** — logger är fire-and-forget, ingen DB-write som kan tappa bort.
- **Trivial scope-utvidgning** — nya ownership-skyddade endpoints får detektering "gratis" genom att handler kallar `IFailedAccessLogger.LogCrossUserAttempt(...)`.
- **CloudWatch-pattern är bevisat** — metric filter + SNS-alarm är standard AWS-mönster, kostnadsfritt under fritt-tier.
- **Stänger TD-67.**

### Negativa

- **Ingen DB-baserad audit-trail för failed access.** Accountability för avvisade åtkomstförsök bevisas via CloudWatch log retention (90 dagar default i prod), inte `audit_log`-tabellen.
- **Anomaly-detection bor i AWS-konfiguration, inte i appen.** Terraform-changeset krävs separat (TD-68). Apparbetet (logger-port + handler-anrop) levereras isolerat — utan TD-68 finns signaler i CloudWatch men inga alarm.
- **Extra existens-query per failed-access** — en extra DB-rundtur i error-path. Inte i hot path (404 är ovanligt vid normalt flöde), men mätbar overhead vid BOLA-attack.
- **Per-handler implementation** — nya handlers måste komma ihåg att kalla `IFailedAccessLogger`. Regression-risk vid utveckling.

### Mitigering

- **Code-reviewer + integration-tester** verifierar att alla ownership-skyddade handlers kallar logger-porten vid mismatch (regression-skydd).
- **TD-68** registreras direkt som genuint TD (kriterium 2 — saknad Terraform-infrastruktur).
- **Fas 6 omprövning** — när impersonation/admin-actions införs och ADR 0022 ändå måste omarbetas, failed-access kan migreras till `audit_log` om det då bedöms motiverat. ADR 0031 supersedas i sådant fall.

## Alternativ övervägda

### Alt A — Utöka AuditBehavior för Result.Failure-paths

Avvisat. (1) Bryter ADR 0022:s explicita success-only-kontrakt utan supersession. (2) Tvingar handler att differentiera via egen error-kod (`Auth.CrossUserAccessDenied`) — det är information som idag medvetet maskeras. (3) Täcker inte query-handlers (returnerar `null`, går inte genom IAuditableCommand).

### Alt B — Ny FailedAccessAuditBehavior

Avvisat. Samma differentierings-problem som A. Ny pipeline-yta för ett koncept som inte är audit. Bryter CCP (Martin 2017 kap. 13).

### Alt C — Domain Event AccessDeniedAttempted

Avvisat. ADR 0022 dokumenterar redan att event-dispatcher saknas — att införa den för TD-67 är scope-skred. Dessutom: "access denied" är inte en domän-händelse (Evans 2003) — det är cross-cutting infrastructure-concern.

### Alt D — Middleware på 404-respons

Avvisat. Middleware vet inte VARFÖR 404 (okänt id vs ownership-mismatch). Audit av alla 404 ger noise-spam från legitima typo-URL:er.

### Alt E — Inline ILogger utan port-abstraktion

Avvisat. Bryter testbarhet — verifikation av att rätt event loggas vid ownership-mismatch kräver att inspektera log-output (bräckligt). Port-abstraktion ger NSubstitute-mock i unit-tester.

### Alt F ren — bara CloudWatch utan port

Förstärkt till hybrid genom att introducera porten. Rent F kräver att handlers kallar `ILogger<T>` direkt med magic strings — bräckligt, missas vid nya handlers.

## Implementation

### Application

- `src/JobbPilot.Application/Common/Auditing/IFailedAccessLogger.cs` (ny port)

### Infrastructure

- `src/JobbPilot.Infrastructure/Auditing/FailedAccessLogger.cs` (ny impl, `ILogger<T>`-baserad, `LoggerMessage`-source-gen)
- `src/JobbPilot.Infrastructure/DependencyInjection.cs` (registrera som singleton i `AddPersistence`)

### Handler-modifikationer

Application-aggregat (ownership-check via `JobSeekerId == jobSeekerId`):

- `GetApplicationByIdQueryHandler`
- `TransitionToCommandHandler`
- `AddFollowUpCommandHandler`
- `AddNoteCommandHandler`

Resume-aggregat:

- `GetResumeByIdQueryHandler`
- `RenameResumeCommandHandler`
- `UpdateMasterContentCommandHandler`
- `DeleteResumeCommandHandler`
- `DeleteResumeVersionCommandHandler` (om version-ID-isolation-check görs på resume-nivå)

**Pattern per handler:**

```csharp
var aggregate = await db.Applications
    .Where(a => a.Id == id && a.JobSeekerId == jobSeekerId)
    .FirstOrDefaultAsync(ct);

if (aggregate is null)
{
    // Ownership-check failed. Skilj "okänt id" från "tillhör annan" för audit.
    var exists = await db.Applications
        .AsNoTracking()
        .AnyAsync(a => a.Id == id, ct);
    if (exists)
    {
        failedAccessLogger.LogCrossUserAttempt(
            aggregateType: "Application",
            requestedAggregateId: id,
            requestingUserId: currentUser.UserId!.Value,
            operation: "GetApplicationById");
    }
    return null; // eller Result.Failure(NotFound)
}
```

### Tester

- **Unit-test (Application):** verifiera att `IFailedAccessLogger.LogCrossUserAttempt` anropas vid ownership-mismatch i minst en query-handler och en command-handler. NSubstitute-mock + `Received(1)`-assertion.
- **Unit-test (Infrastructure):** verifiera att `FailedAccessLogger` producerar strukturerad log-event med rätt property-namn (via test-loggerprovider).
- **Integration-test (utökar TD-66 isolation-suite):** verifiera att cross-user-anrop genererar log-event (capture via `ITestOutputHelper` eller ILoggerProvider-stub).

### TD-68 (lyft som genuin TD per CTO-rekommendation)

CloudWatch metric filter + SNS-alarm. Terraform-leverans i `infra/terraform/environments/{dev,prod}/`. Separat lager, separat review-cykel (security-auditor + Klas-godkännande för deploy). Severity: Minor. Signal-värdet finns även utan alarm via manuell CloudWatch Insights-query.

## Validering

- code-reviewer verifierar pattern-konsekvens över alla handlers.
- security-auditor verifierar att timing/existens-läckage inte introduceras (fire-and-forget-pattern, identisk klient-response).
- Integration-test bevakar att log-event faktiskt produceras vid cross-user (regression-skydd).

## Relaterade beslut

- **ADR 0022** — Audit log-strategi för success-mutationer. Bevaras oförändrad; failed-access hanteras separat per detta beslut.
- **ADR 0028** — Admin authorization marker-interface. När admin-impersonation införs i Fas 6 kan denna ADR omprövas: failed-access-policy under impersonation kan behöva audit_log-rader istället för bara ops-signal.

## Status

**Accepted** för Fas 1. Omvärderas vid Fas 6 (impersonation/admin-actions). Eventuell migration till `audit_log` defereras till då.
