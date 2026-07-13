# ADR 0024 — Audit-retention via PostgreSQL native partitioning + GDPR Art. 17-cascade-orchestration

**Datum:** 2026-05-08
**Status:** Accepted
**Kontext:** STEG 10a + 10b — TD-16 stängning (BUILD.md §18 Fas 1, sista Fas 1 prod-deploy-blockare relaterad till audit/GDPR)
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0008 (pipeline-ordning), ADR 0009 (no-repository), ADR 0010 (Worker-projekt), ADR 0017 (frontend auth pattern — `ISessionStore`-yta), ADR 0022 (audit log pipeline-behavior — Art. 17-policyn deklareras där, implementeras här), ADR 0023 (Worker-pipeline + Hangfire — orchestratorerna här konsumerar samma chassi), ADR 0049 (Accepted — TD-13 PII-fält-kryptering: **komplementär**, lägger backup-PII-lagret via crypto-erasure ovanpå denna ADR:s backup-overwrite-story; cross-ref, **EJ amendment** — denna ADR:s text oförändrad), BUILD.md §7.1, §7.2, §7.3, §13.3

## Kontext

ADR 0022 deklarerade GDPR Art. 17-policy och 90-dagars audit-retention som *spec*, men deferrerade implementationen till TD-16 med kommentaren "blocker för Fas 1 prod-deploy". STEG 9 unblockerade TD-16 genom att aktivera Hangfire-chassit (ADR 0023). STEG 10 implementerar policyn i två operationellt separata sub-STEG (10a + 10b) under en gemensam ADR.

Tre policyer ska implementeras:

1. **Art. 5(1)(e) — Storage limitation:** `audit_log` behålls i 90 dagar. Spec:ad i BUILD.md §7.1 som "partitionering per dag".
2. **Art. 17 — Right to erasure:** användarens audit-trail anonymiseras (`user_id`, `ip_address`, `user_agent` → NULL); övriga fält bevaras 90 dagar för accountability per Art. 17(3)(b) + Art. 5(2). Spec:ad i ADR 0022.
3. **Soft-delete-cascade vid `DELETE /me`:** alla user-ägda aggregat (JobSeeker + Application + Resume) soft-deletas i samma transaction. 30-dagars restore-fönster. Hard-delete via Hangfire-jobb. Spec:ad i BUILD.md §7.3 + §13.3.

Frågorna som avgörs i denna ADR:

1. **Audit-retention-mekanik** — native partitioning, pg_partman, eller daily DELETE?
2. **Migration av befintliga audit-rader** — data-migrerande migration eller separat backfill?
3. **Audit-bypass-pattern** — hur kan Art. 17-anonymisering bryta "audit är write-only"-invarianten utan att urholka disciplinen för normala flöden?
4. **DeleteAccountCommand-strategi** — samlat command eller per-aggregate-loop?
5. **30-dagars restore-fönster — semantik mot Identity** — hur blockeras login utan att hard-deleta `ApplicationUser` under fönstret?
6. **Hard-delete-jobb-scope** — vad omfattas, när triggers Art. 17-anonymiseringen?

## Beslut

Sju delbeslut. D1–D6 landades i STEG 10a + 10b; D7 kompletterar i STEG 11 (app-logg-redaction). De är tätt sammanvävda — uppdelning hade gett flera ADR:er som måste läsas tillsammans ändå.

### Delbeslut 1 — Audit-retention via PostgreSQL native partitioning per dag (STEG 10a)

`audit_log` partitioneras per dag (`PARTITION BY RANGE (occurred_at)`). En daglig Hangfire-jobb `AuditLogRetentionJob` skapar morgondagens partition + droppar alla partitions med `to_date < UTC.Now - 90 days`. Cron `03:00 UTC`, idempotent via `CREATE TABLE IF NOT EXISTS` + `DROP TABLE IF EXISTS`.

**Avvisade alternativ:**

- **pg_partman** — extension-beroende på AWS RDS (extra `CREATE EXTENSION` + GRANT-yta + version-tracking). Native partitioning räcker — vi har inga retention-features som kräver pg_partman:s premium-funktioner.
- **Daily `DELETE WHERE occurred_at < ...`** — VACUUM-overhead på growing audit-tabell. Native partitioning gör retention till en `DROP TABLE`-operation (instant + ingen index-pressure). På Fas 1-volym irrelevant; på Fas 4-volym (när AI-jobb-audit lagras) blir VACUUM-bördan reell.

### Delbeslut 2 — Migration via rename + reinsert i samma migration (STEG 10a)

Befintliga audit-rader i `audit_log` (skapade i STEG 8 dev-DB) bevaras genom en data-migrerande migration `AddAuditLogPartitioning`:

```sql
-- 1. Rename befintlig tabell
ALTER TABLE audit_log RENAME TO audit_log_legacy;

-- 2. Skapa partitionerad parent-tabell med samma kolumner + constraints
CREATE TABLE audit_log (
    id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    correlation_id uuid NOT NULL,
    user_id uuid NULL,
    impersonated_by uuid NULL,
    event_type varchar(100) NOT NULL,
    aggregate_type varchar(100) NOT NULL,
    aggregate_id uuid NOT NULL,
    ip_address varchar(45) NULL,
    user_agent varchar(256) NULL,
    PRIMARY KEY (id, occurred_at)  -- partitions-kravet: PK måste innehålla partition-key
) PARTITION BY RANGE (occurred_at);

-- 3. Bootstrap-partitions: idag + 6 dagar framåt = 7 partitions.
--    Skapas FÖRE default-partitionen — om default skulle existera först
--    och ha rader, kan PG behöva re-routa dem vid range-partition-skapning
--    och fail:a på överlapp. Range-first-default-last eliminerar risken
--    permanent (default har inga rader förrän alla range-partitions finns).
--
--    Bakgrund för "idag + 6 framåt"-orientering: tabellen är tom (0 rader)
--    vid migration. Inga historiska rader behöver bakåt-partitions. Alla
--    NYA inserts behöver framåt-buffer för att inte träffa default. Retention-
--    jobbet (delbeslut 1) skapar morgondagens partition dagligen — bootstrap-
--    bufferten täcker uppstart-fönstret tills jobbet etablerat sitt rullande
--    fönster. (Tidigare ADR-text "senaste 7 dagar" var oprecis och förtydligad
--    efter STEG 10.3-implementation.)
-- (faktiska bootstrap-partitions skapas av Up()-migration-kod via dynamisk SQL —
--  se Infrastructure/Persistence/Migrations/<TIMESTAMP>_AddAuditLogPartitioning.cs)

-- 4. Default-partition fångar rader vars occurred_at hamnar utanför
--    explicit partition-range. Säkerhetsnät i normal drift.
CREATE TABLE audit_log_default PARTITION OF audit_log DEFAULT;

-- 5. Återflytta rader från legacy. Explicit kolumnlista — production-DDL
--    får inte bero på implicit kolumn-ordnings-kontrakt.
INSERT INTO audit_log (
    id, occurred_at, correlation_id, user_id, impersonated_by,
    event_type, aggregate_type, aggregate_id, ip_address, user_agent
)
SELECT
    id, occurred_at, correlation_id, user_id, impersonated_by,
    event_type, aggregate_type, aggregate_id, ip_address, user_agent
FROM audit_log_legacy;

-- 6. Droppa legacy
DROP TABLE audit_log_legacy;

-- 7. Återskapa index på parent (propageras till partitions automatiskt)
CREATE INDEX ix_audit_log_occurred_at ON audit_log (occurred_at DESC);
```

**Konsekvens — PK-ändring:** PostgreSQL native partitioning kräver att partition-key (`occurred_at`) ingår i PK. PK ändras från `(id)` till `(id, occurred_at)`. Detta är en **medveten breaking change** mot ADR 0022:s schema-spec; ADR 0022 kompletteras implicit. Ingen befintlig kod queryar audit-rader på PK-bas (vi har bara `Add(...)` via `AuditBehavior` + `OrderBy(occurred_at DESC)` i framtida admin-läs-yta) — riskytan är minimal.

**Konsekvens — `AuditLogEntryConfiguration`:** EF Core konfig uppdateras till komposit-PK. `AuditLogEntry`-entity får inga ändringar (`Id` är fortfarande primärnyckeln ur Domain-perspektiv; komposit-PK är ett persistence-detalj).

**Konsekvens — pre-prod prod-deploy:** Migrationen skalar till audit-tabellens storlek vid deploy-tillfället. För Fas 1 dev-DB är det irrelevant. För prod-deploy dokumenteras nedtid-fönster i `docs/runbooks/audit-retention.md` (D8). Eftersom STEG 10a körs *innan* Fas 1 går till prod, blir prod-migrationen mot en tom tabell — noll nedtid.

### Delbeslut 3 — Audit-bypass-pattern: dedikerad `IAuditTrailEraser`-port (STEG 10b)

Art. 17-anonymisering bryter "audit är write-only"-invarianten (ADR 0022). Bypass-mekaniken designas explicit för att **isolera bypass-ytan** så att normala command-flöden inte kan smyga in audit-mutationer.

**Port (Application-lagret):**

```csharp
namespace JobbPilot.Application.Common.Auditing;

public interface IAuditTrailEraser
{
    /// <summary>
    /// Anonymiserar alla audit-rader som hör till en användare per GDPR Art. 17.
    /// Sätter user_id, ip_address, user_agent till NULL.
    /// Bevarar correlation_id, event_type, aggregate_type, aggregate_id, occurred_at
    /// i 90 dagar för Art. 5(2) accountability.
    /// </summary>
    /// <returns>Antal rader anonymiserade.</returns>
    Task<int> AnonymizeUserAuditTrailAsync(Guid userId, CancellationToken cancellationToken);
}
```

**Implementation (Infrastructure-lagret):**

```csharp
namespace JobbPilot.Infrastructure.Auditing;

public sealed class AuditTrailEraser(IAppDbContext db) : IAuditTrailEraser
{
    public async Task<int> AnonymizeUserAuditTrailAsync(Guid userId, CancellationToken ct)
    {
        // Direct SQL UPDATE — audit-bypass per ADR 0024 delbeslut 3.
        // ExecuteSqlAsync (parameterized) eftersom ExecuteUpdateAsync kräver
        // en LINQ-query som DbContext.AuditLogEntries inte exponerar för
        // mutation (write-only ADR 0022).
        return await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE audit_log
            SET user_id = NULL,
                ip_address = NULL,
                user_agent = NULL
            WHERE user_id = {userId}
            """,
            ct);
    }
}
```

**Avvisade alternativ:**

- **Alt B — `[SuppressAudit]`-marker på command** — bypass på command-nivå öppnar för missbruk. Marker-interface kan smyga in i nya commands utan medveten review. Dedikerad port är svårare att missbruka.
- **Alt C — Asynkron post-DELETE Hangfire-jobb** — bryter atomicitet med kontoraderingen. Om jobb-trigger:n misslyckas är audit-trail inte anonymiserad samtidigt som kontot är borta. Inte acceptabelt för GDPR-spår.

**Audit-bypass-disciplin (architecture test):**

`IAuditTrailEraser` får anropas **endast** av `HardDeleteAccountsJob` (D6). Architecture test verifierar att ingen annan kod-yta refererar till porten:

```csharp
[Fact]
public void IAuditTrailEraser_should_only_be_referenced_by_HardDeleteAccountsJob()
{
    var result = Types.InAssembly(ApplicationAssembly)
        .That()
        .HaveDependencyOn("JobbPilot.Application.Common.Auditing.IAuditTrailEraser")
        .Should()
        .HaveNameMatching("HardDeleteAccountsJob")
        .GetResult();

    result.IsSuccessful.ShouldBeTrue();
}
```

**Atomicitet — krav på anropare.** `ExecuteSqlAsync` startar ingen egen transaction. `HardDeleteAccountsJob` ansvarar för att öppna en explicit `BeginTransactionAsync` runt anropet till `IAuditTrailEraser.AnonymizeUserAuditTrailAsync` plus efterföljande hard-delete-operationer. Architecture test verifierar inte detta — det är en algoritm-disciplin dokumenterad i delbeslut 6.

### Delbeslut 4 — `DeleteAccountCommand` som samlat Mediator-command (STEG 10b)

`DELETE /me`-endpointen anropar en enda Mediator-command `DeleteAccountCommand` som soft-deletar JobSeeker + alla användarens Application-aggregat + alla användarens Resume-aggregat i **samma `SaveChanges`** (atomisk via `UnitOfWorkBehavior` per ADR 0022 + ADR 0008).

**Command-form:**

```csharp
public sealed record DeleteAccountCommand
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "Account.Deleted";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result response) => Guid.Empty; // se nedan
}
```

**Aggregate-ID-extraktion** är icke-trivial — handler känner JobSeeker.Id men command-record:en gör inte det vid `ExtractAggregateId`-anrop (post-handler). Lösning: handler returnerar `Result<Guid>` (jobSeekerId) och command implementerar `IAuditableCommand<Result<Guid>>` istället. Slutgiltig form bestäms i implementation, dokumenteras i 10b-session-loggen.

**Audit-paritet:** *en* audit-rad per radering (`Account.Deleted`), inte en per cascade-aggregat. Cascade är persistence-detalj — användaren begär en handling, inte 100. Tradeoff dokumenterad och accepterad.

**Avvisade alternativ:**

- **Alt B — `AccountDeletionService`** (domain service som komponerar flera commands) — domain services är för affärslogik som inte hör hemma i ett enskilt aggregat. Konto-radering är applikations-orchestration, inte domain-logic. Domain service hade stulit ansvar från Application-lagret.
- **Alt C — Per-aggregate-command-loop** — `DeleteApplicationCommand` på alla, `DeleteResumeCommand` på alla, `DeleteJobSeekerCommand`. Bevarar audit-paritet 1:1 (`Application.Deleted` × N + `Resume.Deleted` × M) men ger 100+ audit-rader för power user. Användaren begärde *en* handling. Avvisat på UX-grunder och audit-noise-grunder.

**Handler-skiss:**

```csharp
public sealed class DeleteAccountCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ISessionStore sessionStore,
    IDateTimeProvider clock)
    : ICommandHandler<DeleteAccountCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(DeleteAccountCommand cmd, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) throw new UnauthorizedException();

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, ct)
            ?? throw new NotFoundException("Konto hittades inte.");

        // Idempotency: om redan soft-deletat, returnera success utan ny audit.
        if (jobSeeker.DeletedAt is not null)
            return Result.Success(jobSeeker.Id.Value);

        // Hämta alla aggregat — global query filter exkluderar redan soft-deletade
        // (de kan inte vara soft-deletade eftersom JobSeeker själv inte är det än).
        var applications = await db.Applications
            .Where(a => a.JobSeekerId == jobSeeker.Id)
            .ToListAsync(ct);
        var resumes = await db.Resumes
            .Where(r => r.JobSeekerId == jobSeeker.Id)
            .Include(r => r.Versions)
            .ToListAsync(ct);

        foreach (var app in applications) app.SoftDelete(clock);
        foreach (var resume in resumes) resume.SoftDelete(clock);
        jobSeeker.SoftDelete(clock);

        // SaveChanges sker via UnitOfWorkBehavior (atomic).
        // AuditBehavior lägger Account.Deleted-raden i samma transaction.

        // Sessions invalideras post-SaveChanges? Nej — om SaveChanges misslyckas
        // efter session-invalidation har vi inkonsistens. Invalidation sker
        // i en post-commit-hook eller i endpoint-koden efter commandet returnerat.
        // Beslut: invalidation i endpoint-koden (Api/Endpoints/MeEndpoints.cs)
        // efter Result.Success returnerats.

        return Result.Success(jobSeeker.Id.Value);
    }
}
```

**Endpoint-skiss:**

```csharp
// Api/Endpoints/MeEndpoints.cs
me.MapDelete("/", async (IMediator mediator, ISessionStore sessions,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    var result = await mediator.Send(new DeleteAccountCommand(), ct);

    if (result.IsFailure) return result.ToProblem();

    // Post-commit: invalidate alla sessioner. Failsafe — om detta failer
    // får vi en logg-warning, men kontot är redan soft-deletat (idempotent
    // re-delete ger ingen skada).
    await sessions.InvalidateAllForUserAsync(currentUser.UserId!.Value, ct);

    return Results.NoContent();
});
```

### Delbeslut 5 — 30-dagars restore-fönster utan Identity-tabell-migration (STEG 10b)

**Inget nytt fält på `ApplicationUser`.** Restore-fönstret modelleras via `JobSeeker.DeletedAt` (befintlig kolumn). Login-blockering sker genom kontroll av `JobSeeker.DeletedAt` i auth-flödet:

- `LoginCommandHandler` (eller motsvarande session-skapande-yta per ADR 0017) hämtar JobSeeker post-credentials-validering. Om `DeletedAt is not null`: returnera `Result.Failure(DomainError.Validation("Auth.AccountPendingDeletion", "Kontot är raderat. Kontakta support inom 30 dagar för återställning."))`.
- `SessionAuthenticationHandler` (per request) kollar inte JobSeeker.DeletedAt — sessions invalideras direkt vid `DELETE /me` så pågående sessioner upphör. Om det finns kvar en session som inte invaliderades (Redis-fel): nästa request misslyckas på `ICurrentUser`-resolve eftersom JobSeeker inte längre existerar i query-filtrerad context. Det är acceptabelt fail-safe-läge.

**Re-registration under fönstret:** ApplicationUser hard-deletas inte under fönstret → email är fortfarande UNIQUE i Identity-tabellen → `UserManager.CreateAsync` failer på `DuplicateUserName`. Användaren kan inte registrera om sig under 30 dagar. Detta är **avsikt** — bevarar audit-trail-länken och förhindrar email-recycling-attack.

**Restore-endpoint deferreras till Fas 6** (admin-yta per BUILD.md §7.3 + §13.3). State-en är klar i Fas 1: SQL-restore via runbook (`docs/runbooks/account-deletion.md`) är fallback om någon ångrar sig innan admin-UI:t finns.

**Avvisade alternativ:**

- **Custom kolumn `PendingDeletionAt` på `ApplicationUser`** — kräver migration mot AppIdentityDbContext (per ADR 0013), ny kolumn, custom check i SignInManager. Onödigt — `JobSeeker.DeletedAt` räcker eftersom JobSeeker har 1:1-mappning mot ApplicationUser.UserId.
- **`LockoutEnd = DateTimeOffset.MaxValue` som proxy** — semantik-överbelastning. LockoutEnd är för failed-login-spam, inte för konto-radering. Kommer att förvirra framtida läsare.

### Delbeslut 6 — `HardDeleteAccountsJob` (STEG 10b)

Daily Hangfire-jobb `HardDeleteAccountsJob`, cron `04:00 UTC` (1h efter `AuditLogRetentionJob` så de inte konkurrerar om DB-resurser). Idempotent via `AddOrUpdate` i `RecurringJobRegistrar`.

**Algoritm:**

```
Steg 0 — Orphan-cleanup (race-window-skydd):
  Hitta alla ApplicationUser där ingen matchande JobSeeker existerar
  (varken aktiv eller soft-deletad — d.v.s. domain-aggregaten är borta
  men Identity-raden hängde kvar från tidigare körning).
  För varje orphan: UserManager.DeleteAsync. Idempotent — om Identity
  redan tog bort raden mellan SELECT och DELETE är det inget fel.

Steg 1 — Hämta soft-deletade konton mogna för hard-delete:
  Alla JobSeeker WHERE deleted_at < UTC.Now - 30 days
  (IgnoreQueryFilters() — vi vill ha soft-deletade)

Steg 2 — För varje JobSeeker:
  a. Öppna explicit DB-transaction (BeginTransactionAsync)
  b. Anropa IAuditTrailEraser.AnonymizeUserAuditTrailAsync(userId)
  c. Hard-delete alla Application + ApplicationNote + FollowUp
     WHERE JobSeekerId (FK CASCADE i DB tar barnen)
  d. Hard-delete alla Resume + ResumeVersion WHERE JobSeekerId
     (FK CASCADE)
  e. Hard-delete JobSeeker
  f. db.SaveChangesAsync()
  g. transaction.CommitAsync()
  h. UserManager.DeleteAsync(applicationUser) — separat boundary
     (AppIdentityDbContext per ADR 0013). Om denna failer: orphan
     plockas upp av Steg 0 i nästa körning. Idempotent.
  i. Cancel-token-check
  j. Progress-log var 25:e (samma pattern som DetectGhostedApplicationsJob,
     ADR 0023)
```

**Atomicitet — medveten gränsdragning.** Domain-aggregat + audit-anonymisering är atomic via explicit transaction (Steg 2 a–g). Identity-DELETE är separat (Steg 2 h) och kan failas — orphan-loop i Steg 0 plockar upp resten på nästa daily run. Detta är **inte** TD; det är medveten design som följer Clean Arch:s context-isolering. AppDbContext och AppIdentityDbContext har separata ansvar (ADR 0013) och ska inte tvinga distribuerade transaktioner mot samma fysiska Postgres-server bara för att vinna nominell atomicitet.

**Audit-paritet vid hard-delete:** ingen ny audit-rad skrivs (kontot är raderat — det finns ingen att referera). Anonymisering av befintliga audit-rader sker via `IAuditTrailEraser`. `event_type = "Account.Deleted"`-raden från D4 finns redan och anonymiseras (user_id → NULL) men bevaras i 90 dagar för accountability.

### Delbeslut 7 — App-logg-redaction + retention-policy (STEG 11, kompletterar D3)

Audit-tabellen anonymiseras via `IAuditTrailEraser` efter 30-dagars restore-fönstret. **Men app-loggen** (CloudWatch i prod, `Microsoft.Extensions.Logging` Console-sink i dev) bär parallell PII (IP-adress, User-Agent, EmailHash) via `AuthAuditLogger` — oberoende av audit-tabellen. Utan motåtgärder kan en angripare med CloudWatch-access re-identifiera användare även efter Art. 17-anonymiseringen.

Tre policyer:

**1. App-logg-retention: 30 dagar (CloudWatch LogGroup retention).**

Matchar Art. 17 restore-fönstret från D5/D6. Efter 30 dagar är användarens audit-rad anonymiserad och konton hard-deletad — då ska app-loggens IP/UA/EmailHash inte heller vara åtkomliga. Ren GDPR Art. 5(1)(c) data-minimisation-story.

Avvisade alternativ:
- 90 dagar (matcha audit-tabellen) — pseudonym data finns kvar 60 dagar efter Art. 17, svårare att försvara mot Datainspektionen
- 14 dagar — för kort för incident-postmortems vid Fas 1 prod-launch

CloudWatch LogGroup-konfig (`retention_in_days = 30`) är operativ uppgift som spec:as här men appliceras vid första prod-deploy (Fas 0-stängning).

**2. IP /24+/48-anonymisering vid logg-tid — defense-in-depth.**

`AuthAuditLogger.ExtractRequestContext()` anonymiserar IP innan loggning, så app-loggen aldrig bär unik IPv4-fingerprint. Maskningen återanvänds från audit-pipeline via en gemensam port:

```csharp
// Application/Common/Auditing/IIpAnonymizer.cs
public interface IIpAnonymizer
{
    string Anonymize(IPAddress address);
}
```

Logiken (lyft från `RequestContextProvider`) är:
- IPv4: sista oktetten nollas (/24-mask) — bevarar geo-region för ops, eliminerar unik fingerprint
- IPv6: sista 80 bitarna nollas (/48-mask)
- IPv4-mapped-IPv6 (`::ffff:1.2.3.4`) normaliseras till IPv4 före maskning
- Okänd familj → `"unknown"` (fail-safe — aldrig rå adress)

Både `RequestContextProvider` och `AuthAuditLogger` injicerar `IIpAnonymizer`. Singleton (stateless BCL-baserad helper).

Defense-in-depth-motivering: retention-policy (1) skyddar inte mot logg-läckage *under* retention-fönstret. Ops-personal med CloudWatch-access kan korrelera under 30 dagar utan maskningen.

**3. EmailHash → HMAC med roterande nyckel: defererat till Fas 2.**

`LoginCommandHandler.HashEmail` använder rå SHA-256 (deterministic). Samma email → samma hash över tid → korrelerbar. HMAC med roterande nyckel hade brutit korrelationen, men kräver KMS-integration + nyckel-arkiv för att verifiera historiska hashar (audit-paritet vid restore). Inte trivialt i Fas 1 — 30-dagars retention minimerar korrelations-fönstret tillräckligt.

Defererat till Fas 2 som ny TD (utvidgning av TD-22 eller fristående). Beslut tas i Fas 2 när KMS-integrations-mönstret etableras (TD-13 PII-encryption använder samma KMS-yta).

**Tester:**
- `IpAnonymizerTests` (Application.UnitTests) — IPv4/24, IPv6/48, IPv4-mapped, ::1
- `AuthAuditLoggerTests.LoginSucceeded_AnonymizesIpv4ToSlash24` + `LoginFailed_*` + `NoIp_LogsUnknown` — verifierar att app-loggen får anonymiserad IP, inte rå
- Befintliga `RequestContextProvider`-täckning via audit-integration-tester (oförändrad eftersom logiken är identisk)

**Vad som *inte* görs i STEG 11:**
- CloudWatch LogGroup-konfig (deferreras till Fas 0-stängning — IaC eller AWS-konsol)
- HMAC-nyckel-rotation (Fas 2)
- Serilog-stack-byte (Fas 0-stängning, separat ADR vid behov)

**Avvisade alternativ:**
- *Bara retention-policy, ingen logg-tid-redaction* — pseudonym-data flödar fritt under 30d, ops-personal kan korrelera. Defense-in-depth-värde högt jämfört med implementations-kostnad (ren refaktor av befintlig metod).
- *Egen anonymiserings-logik i `AuthAuditLogger`* — duplicerar `RequestContextProvider`-logik. Drift-risk om någon glömmer uppdatera båda. Port + delad impl är rätt nivå.

## Konsekvenser

### Positiva

- **TD-16 stängs** — Fas 1 prod-deploy-blockare relaterad till audit/GDPR är borta
- **Native partitioning** — retention-jobbet är `DROP TABLE`, inga VACUUM-kostnader
- **`IAuditTrailEraser`-isolering** — bypass-pattern är architekt-låst via arch-test, inte spritt över kodbasen
- **Inga Identity-tabell-migrationer** — JobSeeker.DeletedAt räcker som restore-fönster-state
- **Dual-coverage regression-skydd** — arch-test (port-isolering) + smoke-test (Testcontainers) på båda nya jobb
- **Idempotenta jobb** — retention och hard-delete tål re-runs efter omstart
- **DDD-renlärig orchestration** — DeleteAccountCommand i Application-lagret, ingen domain service som överreker

### Negativa

- **PK-ändring på audit_log** — `(id)` → `(id, occurred_at)`. Breaking change för eventuell extern audit-läsning. Mitigerat: ingen sådan kod finns idag.
- **Cross-context-gränsen mellan AppDbContext och AppIdentityDbContext** — Identity-DELETE sitter utanför domain-transactionen och kan failas oberoende. Mitigerat **inom samma jobb** via Steg 0 orphan-cleanup-loop (se Algoritm). Ingen TD genereras — orphan-cleanup är en arkitektur-vald responsvektor, inte teknisk skuld.
- **Bootstrap-partitions vid migration** — `Up()`-koden måste skapa partitions för senaste 7 dagar (default-partitionen fångar oss om jobb-cron missar första körningen). Migrations-koden blir längre än standard EF-migrations.
- **Restore-endpoint saknas i Fas 1** — manuell SQL-restore via runbook är enda väg de första 30 dagarna efter prod-deploy. Acceptabelt — runbook spec:as i 10b.
- **Email-recycling blockerad i 30 dagar** — användare kan inte registrera om sig med samma email. Avsikt, men ska kommuniceras tydligt i raderings-bekräftelsen och i `docs/runbooks/account-deletion.md`.

### Mitigering

- Architecture test `IAuditTrailEraser_should_only_be_referenced_by_HardDeleteAccountsJob` förhindrar tyst regression av bypass-disciplinen
- Smoke-test `AuditLogRetentionJobIntegrationTests` (Testcontainers) verifierar att partitions skapas och droppas korrekt
- Smoke-test `Art17CascadeIntegrationTests` (Testcontainers) verifierar att audit-rader anonymiseras men retention-fält bevaras
- Integration-test `DeleteMeEndpointTests` (WebApplicationFactory) verifierar end-to-end DELETE /me + cascade-state + session-invalidation
- TD-20 (Worker-orphan-detection) loggas i `docs/tech-debt.md` vid 10b-stängning

## GDPR-policy

Denna ADR **implementerar** ADR 0022:s deklarerade Art. 17-policy. Inga nya policy-beslut — bara mekaniken.

- **Art. 5(1)(e) — Storage limitation:** uppfylls via 90-dagars retention-jobbet (delbeslut 1)
- **Art. 5(2) — Accountability:** uppfylls via behåll-policyn — `correlation_id`, `event_type`, `aggregate_type`, `aggregate_id`, `occurred_at` bevaras 90 dagar även efter Art. 17-anonymisering
- **Art. 17 — Right to erasure:** uppfylls via DeleteAccountCommand (soft-delete-cascade) + HardDeleteAccountsJob (hard-delete + anonymisering efter 30 dagar). 30-dagars restore-fönstret är vår tolkning av "rimlig betänketid" — inga GDPR-bestämmelser kräver det, men det skyddar användare mot impulsiva raderingar och förhindrar account-takeover-attack-radering.
- **Art. 17(3)(b) — Undantag för rättsliga skyldigheter:** 90-dagars audit-retention efter anonymisering motiveras av accountability-skyldigheten — anonymiserade rader bär inte längre PII och är därmed inte "personuppgifter" i Art. 4(1):s mening efter anonymisering.

**Anonymiserings-tidpunkt:** vid hard-delete (efter 30 dagar), inte vid soft-delete. Skäl: under restore-fönstret ska användaren kunna se sin egen audit-historik om kontot återställs. Anonymisering vid soft-delete hade gjort restore till en delvis radering — semantiskt felaktigt.

## Alternativ övervägda

(Avvisade alternativ inline i respektive delbeslut. Övriga alternativ som diskuterades och avvisades på meta-nivå:)

### Splitt vs kombinerad implementation

Implementation splittas i STEG 10a (retention) + STEG 10b (DELETE /me + cascade + hard-delete). Skäl: olika risk-profiler, migrations-risk-isolering, reviewer-fokus per STEG. ADR 0024 är *en* ADR för båda eftersom policy:n är konceptuellt enhetlig.

### Jobb-delning vs två separata jobb

`AuditLogRetentionJob` och `HardDeleteAccountsJob` är två separata Hangfire-jobb med olika cron-schedule (03:00 + 04:00 UTC). Avvisat alternativ: ett kombinerat `DailyMaintenanceJob`. Separata jobb ger:

- Tydligare failure-isolering (om hard-delete failar fortsätter retention)
- Separata Hangfire-statistik per ansvarsområde
- Lättare att tillfälligt pausa ett jobb (manuell ops-procedur i runbook)

## Status

**Accepted** för Fas 1 (STEG 10a + 10b). Omvärderas vid:

- **Fas 4** — när AI-jobb-audit börjar lagras i `audit_log` (Worker-jobb genererar mer volym): bekräfta att 90-dagars retention och daily partition-skapande räcker, eller om automatisk vacuum-tuning krävs
- **Fas 6** — när admin-restore-endpointen införs: bekräfta att 30-dagars-fönstret är rätt + lägg till audit-rad `Account.Restored` (separat marker-interface utvärderas)

ADR 0022 kompletteras implicit av denna ADR — Art. 17-policyn är nu implementerad, inte deferrerad. PK på `audit_log` ändras från `(id)` till `(id, occurred_at)` enligt delbeslut 2 — schema-spec i ADR 0022 uppdateras retroaktivt.

---

## Cross-ref-amendment 2026-05-13 — right-to-erasure-cascade för rekryterar-PII i raw_payload

> ### ⚠ SUPERSEDED 2026-07-13 (#842) — DO NOT RELY ON THE ENTRY BELOW
>
> **Every clause of the cascade registered in this section is false, and its scope is wrong.**
> The command no longer exists (deleted); the `raw_payload` null-out was **100 % vacuous** (measured:
> 0 of 93 469 ads carry the probed key); `PurgeStaleRawPayloadsJob` minimises nothing for this PII
> (it never touches `description`); and the cascade **omits `job_ads.description`**, which is where
> the recruiter's contact details actually are (27 077 ads) and which is full-text searchable.
> **The corrected cascade, the full surface inventory, and the governing contract are in
> [Amendment 2026-07-13](#amendment-2026-07-13--the-recruiter-pii-art-17-cascade-failed-and-the-registrys-scope-was-wrong-842).**
> This section is retained unaltered as the record of what was believed and why it failed.

**Datum:** 2026-05-13
**Källa:** TD-73 prod-gating-batch (CTO-rond 2026-05-13)
**Trigger:** TD-73 amendment-batch (ADR 0032 §8 punkt 4)

### Cross-ref

Denna ADR (0024) etablerar Art. 17-cascade-mönstret för **user-ägd data** (JobSeeker + Application + Resume soft-delete → hard-delete via `HardDeleteAccountsJob`, audit-anonymisering via `IAuditTrailEraser`).

För **rekryterar-PII i `job_ads.raw_payload`** (icke-användar-data där JobbPilot ändå är data controller per GDPR Art. 4(1) så snart payload persisteras) implementeras Art. 17 separat per [ADR 0032 §8 amendment 2026-05-13](./0032-jobtech-integration.md#amendment-2026-05-13--%C2%A78-punkt-4-implementeras-audit-wire-%CE%B1-via-adr-0035--right-to-erasure-email-only):

⚠ **FALSIFIED 2026-07-13 (#842) — all four clauses. See the superseded banner above.**

- `RedactRecruiterPiiCommand` admin-endpoint (Email-only nu, Name som TD-75)
- Total null-out av matchande `raw_payload` via `ExecuteUpdateAsync`
- En aggregerad audit-rad per request via befintlig `AuditBehavior` (`Admin.RecruiterPiiRedacted`)
- 30d-retention via `PurgeStaleRawPayloadsJob` minimerar fönstret

`IAuditTrailEraser`-mönstret från D3 (audit-bypass-port) återanvänds **inte** för rekryterar-PII-erasure — `RedactRecruiterPiiCommand` går via Mediator-pipeline (det är en `IAuditableCommand`). System-event-audit-mönstret från [ADR 0035](./0035-system-event-audit-pipeline.md) (`ISystemEventAuditor` bypass-port) är parallell till `IAuditTrailEraser` men för system-jobb, inte erasure-flöden.

### Aktiveringspolicy

`ApplicationUser`-anonymisering kvarstår oförändrat i ADR 0024 D3 + D6. Rekryterar-erasure kvarstår oberoende av kontoraderings-flödet eftersom rekryterare inte har JobbPilot-konton i Fas 2.

---

## Amendment 2026-05-20 — Cascade utökad till SavedSearches + RecentJobSearches (per ADR 0060)

**Datum:** 2026-05-20
**Källa:** F6 P4a security-auditor GDPR-1 fynd 2026-05-20
**Trigger:** [ADR 0060](./0060-recent-job-searches-auto-capture.md) — ny PII-domän (RecentJobSearches) införd, samtidigt pre-existing latent cascade-lucka för SavedSearches (ADR 0039) upptäckt

### Bakgrund

`SavedSearches` (ADR 0039) och `RecentJobSearches` (ADR 0060) saknar databas-FK till `job_seekers` ([ADR 0011](./0011-strongly-typed-ids.md) strongly-typed soft-reference-mönster). De har **inte** `ON DELETE CASCADE` — rader skulle bli orphans efter `HardDeleteAccountsJob` om de inte raderas explicit. För RecentJobSearches lagrar `q`-fältet (varchar 100) PII (söktermer kan innehålla person-/företagsnamn) och utgör därför GDPR Art. 17-cascade-blocker.

### Beslut

`AccountHardDeleter.HardDeleteAccountAsync` uppdaterad 2026-05-20 till att explicit `RemoveRange` både `db.SavedSearches` och `db.RecentJobSearches` per JobSeeker innan `db.JobSeekers.Remove(jobSeeker)`. Operationerna deltar i samma `BeginTransactionAsync`-transaktion → atomic Art. 17-erasure för hela user-trädet (paritet med Applications + Resumes-cascade).

Integration-test `HardDeleteAccountsJobIntegrationTests.RunAsync_CascadesHardDelete_ToSavedSearchesAndRecentJobSearches` verifierar end-to-end.

Cascade-tabellen utökad:

| Aggregat | Mekanik | Driver |
|---|---|---|
| Application + barn (FollowUp/Note) | DB-CASCADE (HasMany→WithOne→Cascade) | EF Core |
| Resume + Versions | DB-CASCADE | EF Core |
| SavedSearch | Explicit `RemoveRange` (ingen DB-FK) | `AccountHardDeleter.HardDeleteAccountAsync` |
| RecentJobSearch | Explicit `RemoveRange` (ingen DB-FK) | `AccountHardDeleter.HardDeleteAccountAsync` |
| audit_log | Anonymisering (D3) | `IAuditTrailEraser` |
| user_data_keys (TD-13/ADR 0049) | Crypto-erasure (delete DEK) | `IUserDataKeyStore.DeleteDataKeysAsync` |

### Pre-existing lucka (SavedSearches)

SavedSearches saknade cascade i `HardDeleteAccountAsync` redan innan F6 P4a — pre-existing GDPR Art. 17-lucka som inte upptäcktes tidigare (low-volume-domän, ingen security-audit hade triggat genom det specifika delete-flödet med seedade SavedSearches). In-block-fix i samma commit-batch som RecentJobSearches per CLAUDE.md §9.6 (samma fas, samma blast-radius). Ingen separat TD lyfts.

---

## Amendment 2026-05-23 — Cascade utökad till SavedJobAds (per F6 P5 Punkt 2 Del A)

**Datum:** 2026-05-23
**Källa:** F6 P5 Punkt 2 Del A leverans (commit c015918) — code-reviewer Minor 2-fynd 2026-05-23 (agentId a57baf5e5e5539d9e: commit-meddelande och kod-kommentarer refererade till ADR 0024-amend som saknades i denna fil)
**Trigger:** [ADR 0053 Amendment 2026-05-23](./0053-detail-modal-intercepting-parallel-route.md#amendment-2026-05-23--sparahar-anskt-knappar-accepted-i-f6-p5-punkt-2) — `SavedJobAd`-aggregat infört som strongly-typed soft-reference (ADR 0011-mönster, paritet med SavedSearches/RecentJobSearches) utan databas-FK till `job_seekers`

### Bakgrund

`SavedJobAd`-aggregatet följer samma `ADR 0011 strongly-typed soft-reference`-mönster som SavedSearches (ADR 0039) och RecentJobSearches (ADR 0060): ingen databas-FK till `job_seekers`, alltså inte heller `ON DELETE CASCADE`. Utan explicit `RemoveRange` i hard-delete-flödet skulle SavedJobAd-rader bli orphans efter `HardDeleteAccountsJob` — bryter atomisk GDPR Art. 17-erasure för hela user-trädet.

### Beslut

`AccountHardDeleter.HardDeleteAccountAsync` uppdaterad 2026-05-23 till att explicit `RemoveRange(db.SavedJobAds.Where(s => s.JobSeekerId == jsId))` innan `db.JobSeekers.Remove(jobSeeker)`. Operationen deltar i samma `BeginTransactionAsync`-transaktion som övriga cascades + DEK-erasure → atomic GDPR Art. 17-erasure för hela user-trädet bevarad (paritet med Applications + Resumes + SavedSearches + RecentJobSearches-cascade).

Integration-test `HardDeleteAccountsJobIntegrationTests.RunAsync_CascadesHardDelete_ToSavedJobAds` (Worker.IntegrationTests/Auth/) verifierar end-to-end i CI.

Cascade-tabellen utökad:

| Aggregat | Mekanik | Driver |
|---|---|---|
| Application + barn (FollowUp/Note) | DB-CASCADE (HasMany→WithOne→Cascade) | EF Core |
| Resume + Versions | DB-CASCADE | EF Core |
| SavedSearch | Explicit `RemoveRange` (ingen DB-FK) | `AccountHardDeleter.HardDeleteAccountAsync` |
| RecentJobSearch | Explicit `RemoveRange` (ingen DB-FK) | `AccountHardDeleter.HardDeleteAccountAsync` |
| SavedJobAd | Explicit `RemoveRange` (ingen DB-FK) | `AccountHardDeleter.HardDeleteAccountAsync` |
| audit_log | Anonymisering (D3) | `IAuditTrailEraser` |
| user_data_keys (TD-13/ADR 0049) | Crypto-erasure (delete DEK) | `IUserDataKeyStore.DeleteDataKeysAsync` |

### Disciplin

Amendment är additivt — originaltexten + Cross-ref-amendment 2026-05-13 + Amendment 2026-05-20 består oförändrade. Ingen separat TD lyfts; cascade-tillägget är in-block-fix i samma commit-batch som SavedJobAd-aggregatet introducerades (commit c015918), enligt CLAUDE.md §9.6 (samma fas, samma blast-radius som ADR 0060-cascade-utökningen 2026-05-20).

---

## Amendment 2026-07-03 — Orphan-sweep grace-fönster kräver `ApplicationUser.CreatedAt` (per #508, epik #482 PR-2)

**Datum:** 2026-07-03
**Källa:** CTO-bind `docs/reviews/2026-07-02-persistence-482-cto.md` (BESLUT 2.#508) + review-rond (security-auditor + dotnet-architect flaggade att D5/Konsekvenser behöver kvalificeras)
**Trigger:** #508 — `CleanupIdentityOrphansAsync` (D6 Steg 0) hade en TOCTOU: en user som registrerar sig mellan sweepens två snapshots (Identity-ids, sedan JobSeeker-UserIds) raderades som orphan → JobSeeker-raden commit:ade sedan med dangling UserId (permanent utelåst live-konto, själv en Art. 17-lucka).

### Bakgrund

D5 avvisade ett nytt fält på `ApplicationUser` FÖR RESTORE-FÖNSTRET, med motiveringen att `JobSeeker.DeletedAt` är en 1:1-proxy (JobSeeker existerar för ett raderat konto). Den motiveringen är strukturellt OTILLGÄNGLIG för orphan-grace-fallet: grace-fönstret gäller per definition en Identity-user som ÄNNU INTE har en JobSeeker-rad (in-flight registrering, Identity committad först per D6:s två-boundary-modell). Det finns ingen JobSeeker-rad att proxya mot → ett Identity-fält är nödvändigt här, till skillnad från restore-fallet.

### Beslut (CTO-bunden mekanik A; mekanik C avvisad)

Lägg `ApplicationUser.CreatedAt` (DB store-default `now()` via `.HasDefaultValueSql("now()")` → `ValueGeneratedOnAdd`; `UserManager.CreateAsync` insertar utan kolumnen → DB stämplar → ingen wiring i `RegisterCommandHandler`). Identity-migration `AddApplicationUserCreatedAt` (schema `identity`, additiv ADD COLUMN; backfill av pre-existerande rader till epoch `1970-01-01` så de är omedelbart sweepbara — de föregår kolumnen och är ej mid-registrering).

Grace-filter i `CleanupIdentityOrphansAsync`: sopa en JobSeeker-lös Identity-user ENDAST om den är äldre än ett grace-fönster (1 h, hårdkodad Fas 1-konstant i paritet med `HardDeleteAccountsJob.RestoreWindowDays`). Root-orsaken (icke-atomisk två-boundary-registrering) fixas MEDVETET INTE — vi härdar den kompenserande kontrollen (orphan-sweepen), vi inför ingen cross-context-transaktion (linje med D6:s medvetna gränsdragning + ADR 0013).

Reverse-orphan-detektor (defense-in-depth, log-only): en `JobSeeker` vars `UserId` saknar Identity-user LOGGAS (Warning, count-only, EventId 2503) men RADERAS ALDRIG här; remediation ägs av #524.

Mekanik C (query-domän-presence-only re-check) avvisad: smalnar men stänger ej race:et; felläget = permanent radering av ett levande konto.

### Klargörande — D5 återöppnas INTE

Restore-fönstret modelleras FORTSATT via `JobSeeker.DeletedAt` (D5 oförändrad i sak). `CreatedAt` tjänar ENBART orphan-grace-fönstret för det JobSeeker-lösa in-flight-fallet. Konsekvenser-utfästelsen "Inga Identity-tabell-migrationer" gäller D5:s restore-scope, inte hela ADR:n — den kvalificeras härmed: D6:s kompenserande kontroll kräver en additiv Identity-migration.

### Disciplin

Additivt amendment (originaltext + tidigare amendments oförändrade). Ingen separat TD; docs-sync i samma PR som scope (ADR 0065). Tester: 3 Testcontainers-integrationstester (färsk orphan sopas EJ [RED-bevisad], åldrad orphan sopas, reverse-orphan icke-destruktiv). security-auditor CONDITIONAL-GREEN (0 Blockers), dotnet-architect + code-reviewer APPROVE.

**Referenser:** #508, #482, #524, ADR 0013, ADR 0066, CTO-bind 2026-07-02, CLAUDE.md §5/§9.2/§12.

---

## Amendment 2026-07-13 — the recruiter-PII Art. 17 cascade failed, and the registry's scope was wrong (#842)

**Datum:** 2026-07-13
**Källa:** CTO ruling `docs/reviews/2026-07-13-842-erasure-contract-cto.md` (BOUND) + evidence pack `docs/research/2026-07-13-842-erasure-evidence-pack.md` (all `file:line` claims re-verified at HEAD)
**Trigger:** #842 — the only Art. 17 erasure path for recruiter PII was a structural no-op that reported success. Discovered while auditing #824 (DPIA, archived ads).

This ADR is the canonical Art. 17 erasure-cascade registry: it is the first place an auditor or a DPA looks. The entry it carried for recruiter PII since 2026-05-13 was wrong in **every clause**, and wrong about **scope** in a way that matters more than any of the clauses. This amendment marks it failed and re-registers it correctly.

### 1. What failed

| Registered clause (Cross-ref-amendment 2026-05-13) | Verdict |
|---|---|
| "`RedactRecruiterPiiCommand` admin-endpoint (Email-only nu, Name som TD-75)" | **The command no longer exists.** Deleted in #842 PR1, together with `RecruiterPiiPurger`/`IRecruiterPiiPurger` — dead code that impersonated a safety control. The **route is deliberately kept** and now returns **501** (`AdminJobAdsEndpoints.cs:70-79`) so that this registry's reference fails loud rather than dangling silently. **TD-75 is closed as void** (CTO V17): its rationale ("Email är primär rekryterar-identifier i JobTech-payloads") is not outdated, it is **falsified** — the email is never a structured key in storage, so the deferral deferred the only branch that could ever have worked. |
| "Total null-out av matchande `raw_payload` via `ExecuteUpdateAsync`" | **100 % vacuous — not approximately vacuous.** The purger matched rows by jsonb containment on `{"employer":{"contact_email": …}}`. That key is guaranteed absent by two independent locks: the wire POCO cannot emit it, and the ingest sanitizer's default-deny allowlist drops it. **Measured against the real corpus: 0 of 93 469 ingested ads carry that key.** `rowsAffected = 0` was its **only possible outcome**, for every ad, always. Same defect class as #805-3 (a filter over a column nothing ever writes). |
| "En aggregerad audit-rad per request via befintlig `AuditBehavior` (`Admin.RecruiterPiiRedacted`)" | The row was written, but it is **empty of everything that matters** — see §4. |
| "30d-retention via `PurgeStaleRawPayloadsJob` minimerar fönstret" | **It minimises nothing for this PII.** The job's only write is `SetProperty(j => j.RawPayload, _ => null)` (`PurgeStaleRawPayloadsJob.cs:104-106`). It never touches `description`. Its own doc claimed to erase precisely the PII it cannot reach (truth-synced in PR1). |

**And the scope error, which is the more serious half:**

The cascade registered **exactly one surface — `raw_payload` — and never `job_ads.description`.** The ad body is where the recruiter's contact details actually live: stored plaintext and verbatim (`.Trim()` only), and **full-text searchable by any authenticated user today**. Proven against real Postgres: `search_vector @@ websearch_to_tsquery('swedish', '<email>')` returns a hit, and the recruiter's **name** hits independently through ordinary word lexemes.

**Measured on the real corpus (2026-07-13, 93 469 ads):** **27 077 ads (29 %) carry a well-formed email in the ad body**; **13 134** carry a phone number; only **17** use textual obfuscation.

This was never a gap in the sanitizer's design. **It IS the design.** The sanitizer is a **key-name** filter that never examines a value, and it deliberately retains every free-text key (`description`, `text`, `company_information`, `needs`, `requirements`, `salary_description`). **It strips the FIELD, not the ADDRESS.** The registry entry inherited that misunderstanding and made it load-bearing.

**The one mitigating fact:** the endpoint has been called **0 times** (`audit_log` rows matching `%RecruiterPiiRedact%` = 0). **No data subject has yet received a false confirmation**, and no notification duty is live. This bounds the harm (P1, not P0). It does not soften the finding: the documented procedure would have produced a false Art. 12(3) statement on the first real request.

### 2. The cascade, re-registered — the FULL surface inventory

Any erasure of recruiter PII from an ad must account for **every** surface below. The 2026-05-13 entry listed only row 3. This is the table an auditor should read.

| # | Surface | Kind | What clears it |
|---|---|---|---|
| 1 | `job_ads.description` | text, plaintext, verbatim | **The real target, and the one the old cascade never named.** Cleared only by rewriting `description` itself (Tier A, at ingest) or by removing the record (Tier B). A `raw_payload` null-out **never touches it**. |
| 2 | `job_ads.search_vector` | tsvector, **STORED GENERATED**, GIN-indexed (`JobAdConfiguration.cs:174-179`) | **Self-heals.** Postgres recomputes it on any write to `title`/`description` (PG18 §5.4; empirically confirmed). No trigger, no reindex, no backfill; it cannot be written directly. It is **not** derived from `raw_payload` → the old purger could not have affected it even in principle. |
| 3 | `job_ads.raw_payload` | jsonb | Values scrubbed at ingest (Tier A) or nulled with the record (Tier B). **This is the only surface the failed cascade listed.** |
| 4 | `job_ads.extracted_terms` | jsonb, **C#-written, NOT generated** (`JobAdConfiguration.cs:191-196` — a plain column, no `HasComputedColumnSql`) | **Does NOT self-heal — this is the load-bearing surface the old cascade did not know existed.** Only a re-run of the extractor clears it: `UpsertExternalJobAdCommandHandler.cs:121-123` reads `jobAd.Title`/`jobAd.Description` (the *aggregate's* values), on both the Add (`:62`) and Update (`:110`) paths. **The recruiter's NAME survives here verbatim** as a `Display`/`MatchedOn` surface form (`JobAdKeywordExtractor.cs:129-136`) — a searchable surface that **no regex over `description` reaches**. |
| 5 | `job_ads.extracted_lexemes` | jsonb, **STORED GENERATED from `extracted_terms`** (`JobAdConfiguration.cs:208-213`) | Transitively — follows #4 whenever the extractor re-runs. Never derived from `description`. |
| 6 | The seven `raw_payload`-derived generated columns (`organization_number`, `ssyk_concept_id`, `region_`/`municipality_`/`occupation_group_`/`employment_type_`/`worktime_extent_concept_id` — `JobAdConfiguration.cs:100,104,116,120,138,142,166`) | text, STORED GENERATED | Go NULL with `raw_payload` (`NULL->'x'->>'y'` is NULL). This is the **#824/#841 blast radius**. **On an erased ad it is irrelevant** — the row leaves every read path (§3, Tier B) and its facets have no consumer. Tier A never nulls `raw_payload`, so it does **not** trigger this. |
| 7 | `job_ads.title` / `job_ads.url` | text | Tier A scrubs `title`; Tier B clears both. (`mailto:` is already filtered out of `url` at ingest.) |
| 8 | `applications.snapshot_description` (`ApplicationConfiguration.cs:104-105`) | text, **a different aggregate**, one frozen copy **per applicant** | **Explicitly OUT of Tier B — a recorded decision, see §3.3.** Tier A reaches *new* snapshots for free: `AdSnapshot.Capture` copies `jobAdData.Description` (`CreateApplicationFromJobAdCommandHandler.cs:83-92`), which post-Tier-A is already scrubbed. |
| 9 | Second-order: `recent_job_searches.q`, `saved_searches.criteria` | text / jsonb | Already in this ADR's **user** cascade (Amendment 2026-05-20) — but that cascade is keyed to the *searching user*, not to the recruiter. If a user searches the recruiter's email (which §1 proves works), the string persists in **another user's** row. Whether any such row exists is **UNPROVEN** (no query run). **PR3 queries the DB and cascades if so** (STOPP-4). |
| 10 | Postgres MVCC residue: pre-update heap tuple, WAL, replicas, base backups/PITR | — | VACUUM reclaims the heap; the rest is a **disclosure** obligation, not a bug. **The backup/PITR retention window is not yet stated, and CC must not invent it** (STOPP-4): the DPIA cannot be signed and no `v*` tag cut until Klas fills it. EDPB CEF 2025 singles out exactly this gap. |

**The durability constraint that binds every row above.** The nightly full backfill (`sync-platsbanken-snapshot`, 02:00) and the 10-minute stream both funnel through `UpdateFromSource`, which **unconditionally reassigns** `Title`/`Description`/`Url`/`RawPayload` and then re-runs the extractor. ⇒ **Any one-shot redacting UPDATE is undone within ≤24 h — within ≤10 min for a still-streaming ad.** This is why the failed cascade could not have been repaired by "make the purger null `description` too". A durable erasure must either live **inside the ingest path** (Tier A) or **remove the carrier and block its re-import** (Tier B). There is no third shape.

### 3. The governing contract — ADR 0106 (BOUND, NOT YET SHIPPED)

> ⚠ **Tense matters here, and it is the exact defect this issue is about.** ADR 0106's two tiers are **bound** (CTO ruling 2026-07-13, executable without further GO). **Neither is shipped.** Tier A ships in PR2, Tier B in PR3. **Today the product has no working Art. 17 erasure path for recruiter PII** — only the containment in §1 (a 501 and a truthful runbook). A doc that describes a control we do not yet have is precisely the failure being corrected; this section must not be read as describing present behaviour.

**Tier A — Art. 25, everyone, no request needed, heuristic, DISCLOSED.** We do not *store* recruiter contact details. Email and phone are stripped from the ad body **at ingest, as a `JobAd` aggregate invariant** (`RecruiterContactRedactor` — deterministic, no LLM per ADR 0071), and replaced by a marker pointing to the canonical ad at Arbetsförmedlingen. Placement in the aggregate (not the handler, not the ACL) is what closes durability and completeness for free: the nightly rewrite goes through the same invariant, and `extracted_terms` is re-derived from the already-scrubbed aggregate values.
**Reaches:** surfaces 1, 2, 3, 4, 5, 7 — and 8 for new snapshots. **Detection is imperfect and we say so** (misses obfuscation, and image-embedded addresses are a hard 0 %).

**Tier B — Art. 17, on request, PROVABLE, no detector involved.** On a valid request we **remove the entire ad record** (`JobAdStatus.Erased`, zero migration) and **block its re-import**. It deletes the **carrier**, not the **string**.
**Reaches:** surfaces 1-7 together, with no recall question, no obfuscation question, no image-embedded question — **and it covers the recruiter's NAME (surface 4), which no regex can reach.** Plus surface 9 if the DB query finds rows (STOPP-4).
**Does not reach:** surface 8 — deliberately (§3.3).

**Each tier is what makes the other honest.** Tier A alone leaves no honest answer to a request (a redact-on-request path tells a named data subject "your data is erased" when we only know "our regex found nothing more" — Art. 17(1) is textually **unqualified**; the "reasonable steps / available technology" language lives **only** in Art. 17(2), which governs informing *other* controllers, not erasure from our own store; and no EDPB/IMY/DPA authority accepts best-effort erasure of unstructured free text). Tier B alone would leave us hoarding contact details for 27 077 ads whose recruiters will never know we exist and will never ask. **Neither is optional. Neither is sufficient.**

#### 3.1 Bound disclosure wording (Swedish, substance bound)

- *Privacy policy (Tier A):* "Vi hämtar annonstexter från Platsbanken. Innan en annons sparas tar vi automatiskt bort e-postadresser och telefonnummer ur annonstexten. Kontaktuppgifterna finns kvar i originalannonsen hos Arbetsförmedlingen, som vi länkar till. Borttagningen är regelbaserad och kan missa uppgifter som skrivits på ovanliga sätt eller som ligger i en bild."
- *Erasure contract (Tier B):* "Om du begär radering av dina kontaktuppgifter i en annons vi har hämtat tar vi bort hela annonsen ur våra system och hindrar att den hämtas in igen. Vi kan inte ta bort annonsen hos Arbetsförmedlingen, som är den som publicerat den."

The second sentence of the Tier-B text is **mandatory**. *Google Spain* (C-131/12) cuts both ways: it legitimises "we erase our copy, not Arbetsförmedlingen's" — and it **forecloses** refusing a request on the ground that the ad is already published. Erasing our copy does not remove the data from the world, and we say so.

#### 3.2 Shipping order and current status

| PR | Scope | Status |
|---|---|---|
| **PR1** | Containment + truth: endpoint → **501**; `RecruiterPiiPurger` + `IRecruiterPiiPurger` + the command deleted; test fiction rewritten (#843); runbook's false confirmation pulled; source docs and ADRs 0032/0024/0049 truth-synced | **COMMITTED** |
| **PR2** | **Tier A** — ingest scrub as a `JobAd` aggregate invariant + backfill of all 93 469 ads + measured recall/precision | **NOT SHIPPED** |
| **PR3** | **Tier B** — `JobAdStatus.Erased`, re-import tombstone, 410 Gone on detail, audit payload + failure audit; **lifts the launch gate** | **NOT SHIPPED** |

**No migrations in any of the three** (`JobAdStatus` is a string-converted SmartEnum with no CHECK constraint; `audit_log.payload` jsonb already exists — `AuditLogEntryConfiguration.cs:57`). The #821/#841 migration lane is not touched, not blocked and not waited on. `db-migration-writer` is therefore **not** invoked for #842 — its absence is a ruling, not a skipped gate.

**Launch gate: no `v*` prod tag until Tier B ships** (STOPP-6).

#### 3.3 RECORDED SCOPE DECISION — Tier B does NOT cascade into `applications.snapshot_description`

**This is a decision, not an omission.** It is recorded here, in the cascade registry, precisely because a silent gap in a cascade table is how #842 happened in the first place.

**Bound (CTO V13):** a Tier-B erasure removes the `job_ads` record; it does **not** null or redact applicants' frozen `applications.snapshot_description` copies. [ADR 0086](./0086-applied-ad-snapshot-write-side-retention.md) exists **precisely so the snapshot outlives the ad** — nulling it would destroy an applicant's own record of what she applied to.

The rights collision is real and is resolved explicitly: **the recruiter's Art. 17 interest vs the applicant's interest in her own evidence — resolved in favour of the applicant.** The collision is also *mostly designed away* by minimisation getting there first: post-Tier-A, new snapshots are clean by construction (`AdSnapshot.Capture` copies an already-scrubbed description), and the dev DB currently holds **0 non-null `snapshot_description` rows**. The genuine residual is narrow: a snapshot holding a *missed* contact detail, or a Tier-B erasure of an ad that already has applicant snapshots.

⚠ **The legal ground is PENDING, not settled.** The proposed ground is **Art. 17(3)(e)** (establishment, exercise or defence of legal claims). **Klas must affirm it (STOPP-3);** it is a legal call, not CC's, and it is **not asserted as settled here.** Until affirmed, this row of the cascade carries a technical decision with an unconfirmed legal basis, and the DPIA cannot be signed on it.

### 4. The audit gap in this ADR's own pipeline

The 2026-05-13 entry promised "en aggregerad audit-rad per request". The row exists. **It records nothing.**

- **`AuditLogEntry.Create` hard-codes `payload: null`** (`AuditLogEntry.cs:81-92`, the literal at `:92`), and it is the factory `AuditBehavior` calls. So the recruiter-PII audit row carries **no identifier, no identifier type, no `rowsAffected`** — it is an `event_type` and a timestamp attached to a non-event. This **directly contradicts** what the now-deleted command's own doc claimed it wrote (an audit row with payload `{ identifier, type, rowsAffected }`), and it means the verification query the erasure runbook points an operator at (`recruiter-pii-erasure.md:61-63`) selects a column that is **always NULL**. An Art. 5(2)/30 accountability gap sitting inside the ADR that owns accountability.
- **`AuditBehavior.cs:35-38` skips audit on `Result.Failure`** ("Fas 1 auditerar bara success per ADR 0022"). So a **rejected** erasure request leaves **no trace it was ever received** — a direct **Art. 12(3)** exposure, since Art. 12(3)/(4) obliges the controller to respond even when it takes no action, and we would have no record that a request arrived at all.

**PR3 fixes both:**
- `AuditLogEntry.Create` gains a payload parameter. **The `audit_log.payload` jsonb column already exists** (`AuditLogEntryConfiguration.cs:57`) ⇒ this is a **pipeline change, not a migration**. Payload shape: `{ identifierHmac, identifierKind, matchedExternalIds[], erasedCount, dryRun }`. The `externalIds` are not PII and are the accountability spine: they let a future auditor verify the erasure actually happened.
- **HMAC-SHA256 with the server pepper — explicitly NOT md5** (which the old runbook suggested at `:134`). An md5 of an email address is dictionary-reversible in milliseconds; it is not a pseudonym, it is a fig leaf. Same primitive and same precedent as the org.nr HMAC (ADR 0090 D5, #824 condition C1) — one house rule.
- **`IAuditableCommand.AuditFailures`, opt-in, default `false`**, set `true` for the erasure command. Opt-in keeps every existing command's behaviour bit-identical (OCP) and holds the blast radius at exactly one command.

The general "Fas 6 retro-fit" of failed-attempt audit (`AuditBehavior.cs:36`) is **not** re-opened here; only the erasure command opts in, because only it carries an Art. 12(3) duty to record a request it refuses.

### 5. Discipline

Additive amendment. The original text and all prior amendments stand unaltered; the 2026-05-13 cross-ref section is **retained as the record of what was believed and why it failed**, with a superseded banner rather than a rewrite. The corrected cascade is §2 of this amendment.

Docs-sync ships in the same PR as scope (ADR 0065) — no docs-only PR. **This ADR does not restate ADR 0106; it points to it.** ADR 0106 is the decision record for the two-tier contract and is **local/gitignored per ADR 0072** (0074+), which is why its substance is summarised here in the tracked registry rather than linked as a tracked file.

**Referenser:** #842, #843, #845, #824, #841, #821, #805-3, ADR 0032 (§8 + amendments A2/A3), ADR 0049, ADR 0071, ADR 0072, ADR 0086, ADR 0087 D8(a), ADR 0090 D5, ADR 0106, CTO ruling 2026-07-13, evidence pack 2026-07-13, GDPR Art. 5(1)(c)/5(2), 12(3), 17(1)/17(3)(e), 19, 25(2), 30, CJEU C-131/12 (*Google Spain*), CLAUDE.md §2.2/§3/§5/§6.5/§9.2/§12.