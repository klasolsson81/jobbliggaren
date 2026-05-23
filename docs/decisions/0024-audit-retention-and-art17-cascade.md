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

**Datum:** 2026-05-13
**Källa:** TD-73 prod-gating-batch (CTO-rond 2026-05-13)
**Trigger:** TD-73 amendment-batch (ADR 0032 §8 punkt 4)

### Cross-ref

Denna ADR (0024) etablerar Art. 17-cascade-mönstret för **user-ägd data** (JobSeeker + Application + Resume soft-delete → hard-delete via `HardDeleteAccountsJob`, audit-anonymisering via `IAuditTrailEraser`).

För **rekryterar-PII i `job_ads.raw_payload`** (icke-användar-data där JobbPilot ändå är data controller per GDPR Art. 4(1) så snart payload persisteras) implementeras Art. 17 separat per [ADR 0032 §8 amendment 2026-05-13](./0032-jobtech-integration.md#amendment-2026-05-13--%C2%A78-punkt-4-implementeras-audit-wire-%CE%B1-via-adr-0035--right-to-erasure-email-only):

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