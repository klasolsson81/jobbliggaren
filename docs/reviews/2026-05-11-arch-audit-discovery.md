# Retrospektiv arkitekturell audit — JobbPilot STEG 1–14

**Datum:** 2026-05-11
**HEAD:** `6e5e6b0`
**Audit-typ:** Discovery (Fas 1) — ingen kod-ändring
**Granskare:** dotnet-architect (Klas-invokerad efter två-fas-approach-valet)

> **Leverans-not:** dotnet-architect-agentens tool-config är read-only (saknar `Write`/`Edit`). Rapporten levereras verbatim från agent-output, applicerad till disk av parent-CC.

---

## 1. Sammanfattning

**Top-level verdict: clean — minor refactoring opportunities only.**

JobbPilot:s STEG 1–14 är arkitekturellt påfallande disciplinerad. 24 architecture-tester låser Clean Arch-gränser, audit-bypass-port-konsumtion, IL-skannade connection-string-läckage, paged-query-kontrakt, Worker-isolering och pipeline-behavior-ordning. Sökning efter de blocker-klass anti-patterns som CLAUDE.md §5.1 listar (`DateTime.UtcNow`, `.Result`/`.Wait()`, `dynamic`, Repository-pattern, AutoMapper, `catch (Exception)` utan action) gav **noll träffar** i `src/`. Domain-aggregat är invariant-skyddade, alla state-transitions går via explicita metoder med `IDateTimeProvider`, strongly-typed IDs används konsekvent mellan aggregat, och pipeline-ordningen är dokumenterad som single source of truth (`MediatorPipelineBehaviors.InOrder`).

**Hot spots:** **0 Blocker, 0 Major, 4 Minor, 3 Nit.** Allt klassat som "förbättring", inget som klassat som "borde inte ha släppts genom review".

**Total CC-tid förbrukad:** ~65 minuter discovery + rapport-syntes. Inom 1–2h-budgeten.

---

## 2. STEG-för-STEG-klassning

> Klassningarna nedan är baserade på arkitekturell yta vid HEAD `6e5e6b0`, inte på commit-bevittnande från varje STEG-period. Audit:n är synkron med nuvarande kod, inte arkeologisk på historik.

| STEG | Status | Riskklassning | En-rads-motivering |
|------|--------|---------------|---------------------|
| Pre-STEG (scaffolding) | Klar | grön | Lösnings-topologi följer ADR 0001 strikt. |
| STEG 1 (Domain-grund + JobAd) | Klar | grön | AggregateRoot/Entity rena baser, Result-typer separata. |
| STEG 2 (CQRS-pipeline + EF Core) | Klar | grön | Pipeline-behaviors single-purpose, UoW post-action konsistent. |
| STEG 3 (JobSeeker-aggregat) | Klar | grön | Register-flöde via static factory, ingen public setter. |
| STEG 4a (Application-aggregat) | Klar | grön | SmartEnum för status med transition-graf, soft-delete cascade i aggregatet. |
| STEG 4b (FollowUp + Note) | Klar | grön | Subaggregat med egna Create-factories, refererade via ID från root. |
| STEG 5 (Resume-aggregat) | Klar | grön | MasterVersion-invariant skyddad, hela ResumeContent ersätts (ingen partial mutation). |
| STEG 6 (Identity + Sessions) | Klar | grön | Separat DbContext per ADR 0013, opaque session-IDs, RSA-keys singleton. |
| STEG 7a (AuditBehavior + log-skrivning) | Klar | grön | Marker-interface-pattern (IAuditableCommand), opt-in, atomic via UoW. |
| STEG 7b (Audit-portar) | Klar | grön | ICorrelationIdProvider/IRequestContextProvider/IIpAnonymizer ger HTTP-fri Worker-stubbning. |
| STEG 8 (GhostedDetection-job) | Klar | grön | Orchestrator i Application, Worker binder bara cron, audit-paritet via per-item dispatch. |
| STEG 9 (Worker-compose + ADR 0023) | Klar | grön | AddPersistence-modulering, Worker-stubs implementerar Application-portar (verifierat av arch-test). |
| STEG 10a (DeleteAccount soft) | Klar | grön | Idempotent cascade-SoftDelete genom aggregate-rooter, AuditBehavior triggar via marker. |
| STEG 10b (HardDeleteJob + AccountHardDeleter) | Klar | gul | Cross-context atomicitet hanteras explicit (port docs:ar boundary-modellen), men porten är något fet (3 ansvar i ett interface — se H-1). |
| STEG 11 (AuditRetention + partitionering) | Klar | grön | Retention-job idempotent, partition-DDL via port med arch-låsta konsumenter. |
| STEG 12 (ForwardedHeaders, HSTS, AlbOptions) | Klar | grön | `EnsureSafeForEnvironment`-mönstret konsistent, fail-loud disciplin etablerad. |
| STEG 13a (Migrate-projekt) | Klar | grön | Console-app isolerad, ConnectionStringFactory testbar med IL-arch-test som vakt. |
| STEG 13b (HTTPS aktiverat) | Klar | grön | HSTS gate:at på AlbOptions.HttpsEnabled, dev-skydd från HSTS-lock. |
| STEG 13c (Admin-endpoint + audit-log query) | Klar | grön | Paged-query kontrakt etablerat, query-handler ren projektion. |
| STEG 14a (AdminBootstrap-seeder) | Klar | gul | Hosted-service kör Identity-DDL-tolerant — gråzon mellan operations-task och runtime-bootstrap (se N-2). |
| STEG 14b (RequireRole-policy) | Klar | grön | HTTP-policy + AdminAuthorizationBehavior (defense-in-depth) verifierad av arch-test. |
| STEG 14c (AdminAuthorizationBehavior) | Klar | grön | Marker IAdminRequest, kastar ForbiddenException i pipeline innan UoW öppnas. |
| Fas 1 Block A (TD-cleanup) | Klar | grön | ConnectionStringFactory split, IL-arch-test för Trust=true-läckage. |
| Fas 1 Milestone (CTO-formalisering) | Klar | grön | Pipeline-ordning lyft till `MediatorPipelineBehaviors.InOrder`, arch-test låser den. |

**Frontend-touch sub-block:** ej i scope för denna audit (backend-fokuserad).

---

## 3. Hot spot-lista

### H-1 (Minor): `IAccountHardDeleter` blandar tre ansvar

- **Klassificering:** Minor
- **Område:** SOLID (ISP), DDD (port-design)
- **Fil-referens:** [src/JobbPilot.Application/Auth/Jobs/HardDeleteAccounts/IAccountHardDeleter.cs](../../src/JobbPilot.Application/Auth/Jobs/HardDeleteAccounts/IAccountHardDeleter.cs) (interface) + [src/JobbPilot.Infrastructure/Auth/AccountHardDeleter.cs](../../src/JobbPilot.Infrastructure/Auth/AccountHardDeleter.cs)
- **Beskrivning:** Porten exponerar tre operationer av olika natur: `CleanupIdentityOrphansAsync` (idempotency-städ-loop), `GetAccountsReadyForHardDeleteAsync` (read-side query) och `HardDeleteAccountAsync` (transactional cross-context mutation). Detta är "all-the-things-the-job-needs" — orchestrator-bekvämlighet, inte port-design. ISP säger: klienter ska inte tvingas bero på metoder de inte använder. Här är `HardDeleteAccountsJob` enda konsumenten så ISP-skadan är teoretisk, men porten blockerar framtida återbruk (om en admin-vy ville köra `CleanupIdentityOrphans` standalone får man hela porten på köpet).
- **Risk:** Låg på kort sikt — arch-test lockar konsument-listan till `HardDeleteAccountsJob`. Långsiktig risk: när Fas 6 introducerar admin-impersonation + manuell-purge-knapp blir det frestande att återbruka samma port istället för att splitta.
- **Rekommenderad åtgärds-scope:** TD eller ny refactor-STEG (~2h CC-tid). Split i `IIdentityOrphanCleaner` + `IExpiredAccountReader` + `IAccountHardDeleter`. Arch-test uppdateras till tre separata konsumentlistor.
- **Motivering för scope:** ingen brådska — fungerar idag, lockas av arch-test. Splittas naturligt när Fas 6 admin-yta kommer.

### H-2 (Minor): "Resolve JobSeekerId from current user" duplikat i ~13 handlers

- **Klassificering:** Minor
- **Område:** DRY, SoC
- **Fil-referens:** 13 filer enligt Grep-resultat — `CreateApplicationCommandHandler.cs`, `AddNoteCommandHandler.cs`, `TransitionToCommandHandler.cs`, `AddFollowUpCommandHandler.cs`, `GetPipelineQueryHandler.cs`, `GetApplicationsQueryHandler.cs`, `GetApplicationByIdQueryHandler.cs`, `CreateResumeCommandHandler.cs`, `RenameResumeCommandHandler.cs`, `DeleteResumeCommandHandler.cs`, `DeleteResumeVersionCommandHandler.cs`, `UpdateMasterContentCommandHandler.cs`, `GetResumesQueryHandler.cs`. Mönstret är identiskt:

  ```csharp
  if (!currentUser.UserId.HasValue)
      throw new UnauthorizedException(); // eller return Failure(...)

  var jobSeekerId = await db.JobSeekers
      .AsNoTracking()
      .Where(js => js.UserId == currentUser.UserId.Value)
      .Select(js => js.Id)
      .FirstOrDefaultAsync(cancellationToken);
  ```

- **Beskrivning:** Identisk uppslagslogik (user → JobSeekerId) i 13 handlers. Sömlösa subtila skillnader: vissa kastar `UnauthorizedException`, andra returnerar `Result.Failure<T>(...)`. `CreateApplicationCommandHandler` saknar dessutom `.AsNoTracking()`. Detta är inte ett brott mot Clean Arch (handlers gör fortfarande "en sak" — de behöver bara extra service-resolution-steg), men det är klassiskt DRY-läckage som kommer bita när logiken behöver utvidgas (t.ex. impersonation: JobSeekerId-resolution behöver kolla `ImpersonatedJobSeekerId`-claim).
- **Risk:** Inkonsistens kryper in vid retrofit. En lösning som ska gälla för alla handlers (impersonation, soft-deleted-block, multi-jobseeker per user) kräver patch på 13 platser.
- **Rekommenderad åtgärds-scope:** TD (~2-3h CC-tid). Introducera `ICurrentJobSeeker`-port (Application-lager) som omsluter user→JobSeekerId-resolutionen + lyfter felhanterings-policyn till en plats. Infrastructure-impl wrappar `IAppDbContext` + `ICurrentUser`. Handlers krymper till 1 rad: `var jobSeekerId = await currentJobSeeker.RequireIdAsync(ct);`
- **Motivering för scope:** Refactor är mekanisk men 13 filer berörda. Tester behöver enklare mock-yta (NSubstitute istället för fake DbContext-projection). Inte brådska — handlers är korrekta idag, bara repetitiva.

### H-3 (Minor): SessionAuthenticationHandler gör per-request role-fetch utan circuit-protection

- **Klassificering:** Minor
- **Område:** SoC (auth-handler vs role-resolution)
- **Fil-referens:** [src/JobbPilot.Infrastructure/Auth/SessionAuthenticationHandler.cs:86-95](../../src/JobbPilot.Infrastructure/Auth/SessionAuthenticationHandler.cs#L86-L95)
- **Beskrivning:** Per-request role-fetch via `UserManager.GetRolesAsync` (1 DB-query/autentiserat request). Kommentaren noterar den medvetna avvägningen (security-first, omedelbar role-revoke, CTO-beslut A1). Tekniskt korrekt val. Arkitekturellt: auth-handler:n tar nu beroende på `IUserAccountService`, vilket är en Application-port som råkar implementeras i Infrastructure — handler:n är *själv* Infrastructure och anropar Application-portens Infrastructure-impl. Detta fungerar (samma assembly) men cementerar att SessionAuthenticationHandler har en "tjockare" SoC-yta än bara token-validation. Klassisk middleware skulle göra session-validation och delegera role-resolution till en `IClaimsTransformation` eller liknande ASP.NET-extension-punkt.
- **Risk:** Låg. Den dolda risken är att framtida observability-behov (token-validation-latency vs role-fetch-latency) blir svår att dekomponera när bägge sker i samma handler.
- **Rekommenderad åtgärds-scope:** TD eller in-block-fix (1-2h CC-tid). Flytta role-fetch till `IClaimsTransformation` (kör efter SessionAuthenticationHandler men före authorization). Bibehåller per-request-fetch-modellen men separerar concerns. Alternativt: lämna som det är och dokumentera SoC-medvetenheten i ADR (CTO-beslut A1 är redan dokumenterat i kommentar — räcker kanske).
- **Motivering för scope:** Liten win, ingen brådska. ASP.NET-naturlig refactor utan att röra Application-portar.

### H-4 (Minor): Pagineringsegenskapsnamn-inkonsistens i query-records

- **Klassificering:** Minor
- **Område:** DRY, kontrakt-konsistens
- **Fil-referens:** [src/JobbPilot.Application/Common/PagedResult.cs](../../src/JobbPilot.Application/Common/PagedResult.cs) (har `Page`) + [src/JobbPilot.Application/Admin/Queries/GetAuditLogEntries/GetAuditLogEntriesQuery.cs](../../src/JobbPilot.Application/Admin/Queries/GetAuditLogEntries/GetAuditLogEntriesQuery.cs) (har `Page`) + [src/JobbPilot.Application/Resumes/Queries/GetResumes/GetResumesQuery.cs](../../src/JobbPilot.Application/Resumes/Queries/GetResumes/GetResumesQuery.cs) (har `PageNumber`) + [tests/JobbPilot.Architecture.Tests/PagedResultContractTests.cs:73-74](../../tests/JobbPilot.Architecture.Tests/PagedResultContractTests.cs#L73-L74) (heuristik accepterar bägge namnen).
- **Beskrivning:** `PagedResult<T>`-DTO:n har egenskapen `Page`. Två queries använder `Page`, övriga använder `PageNumber`. Arch-testet `PagedResultContractTests` har explicit kommentar som rättfärdigar att bägge är legitima ("wire-shape:n är 'page' via System.Text.Json camelCase"). Det fungerar men den interna API-ytan är inkonsistent — Application-koden känns slarvig att läsa när två varianter samexisterar.
- **Risk:** Mycket låg. Wire-stable. Kosmetisk kod-smell.
- **Rekommenderad åtgärds-scope:** in-block-fix (~30 min CC-tid). Bestäm kanon (rekommenderar `Page` eftersom det matchar `PagedResult.Page`-output), rename övriga, ta bort heuristik-accepteransen i arch-testet.
- **Motivering för scope:** Trivial mekanisk refactor, fångas av kompilator om något missas.

### N-1 (Nit): `Application.SoftDelete` raisar inte domain event

- **Klassificering:** Nit
- **Område:** DDD (event-consistency)
- **Fil-referens:** [src/JobbPilot.Domain/Applications/Application.cs:129-134](../../src/JobbPilot.Domain/Applications/Application.cs#L129-L134)
- **Beskrivning:** `Application.SoftDelete` muterar `DeletedAt` och cascadar till children men raisar **inget** `ApplicationDeletedDomainEvent`. Jämför `Resume.SoftDelete` (rad 143-149) som raisar `ResumeDeletedDomainEvent`. Inkonsistens. Andra aggregat (`JobSeeker.SoftDelete` rad 77-78) saknar också event — så Resume är faktiskt udda-positiv, inte Application/JobSeeker som är udda-negativ.
- **Risk:** Mycket låg idag (inga handlers lyssnar på `ResumeDeletedDomainEvent` heller, baserat på Glob — dispatcher-infra finns men inga subscribers). Risken är framtida regression när någon förlitar sig på events för audit eller projection-uppdatering.
- **Rekommenderad åtgärds-scope:** in-block-fix (~30 min CC-tid). Antingen lägg till `ApplicationDeletedDomainEvent` + `JobSeekerDeletedDomainEvent` (konsistent uppåt), eller ta bort `ResumeDeletedDomainEvent` (konsistent nedåt). Klas-val: vilken riktning?
- **Motivering för scope:** Trivial, men kräver Klas-val på riktning.

### N-2 (Nit): `IdempotentAdminRoleSeeder` blandar bootstrap-policy med runtime-tolerans

- **Klassificering:** Nit
- **Område:** SoC
- **Fil-referens:** [src/JobbPilot.Infrastructure/Identity/IdempotentAdminRoleSeeder.cs:50-60](../../src/JobbPilot.Infrastructure/Identity/IdempotentAdminRoleSeeder.cs#L50-L60)
- **Beskrivning:** Seedern fångar `PostgresException 42P01` (undefined_table) som specialfall för integration-test-fixturer där host-start triggas innan migrations körts. Detta är en operations-quirk (DDL-ordning i tester) som läckt in i Identity-Bootstrap-rutinen. I prod kan denna catch maskera en faktisk migrate-task-failure (Migrate-task hängde, Api startade ändå, seeder slukar PostgresException, prod startar utan Admin-role).
- **Risk:** Låg-medel i prod-rollout-edge-case. Kommentaren noterar att Migrate kör först, men `LogSchemaMissing(...)` är `LogLevel.Warning` — inte en startup-failure. En tappad warning-larm är möjlig om CloudWatch-alarm inte fångar exakt den event-IDn.
- **Rekommenderad åtgärds-scope:** in-block-fix eller TD (~1h CC-tid). Tre alternativ:
  1. Gate på `IHostEnvironment.IsDevelopment() || IsEnvironment("Test")` — i prod ska 42P01 bubbla som faktiskt fel.
  2. Skapa separat `MigrationsAwareSeederFixture` för integration-tester och låt seedern vara strikt.
  3. Behåll men höj log-level till `LogLevel.Error` + lägg till CloudWatch-alarm-kontrakt.
- **Motivering för scope:** Hanterbart, ingen brådska, men prod-safety-net är värt att stärka innan första prod-deploy.

### N-3 (Nit): `Resume.MasterVersion` kastar utan kontextuell fel-info

- **Klassificering:** Nit
- **Område:** DDD (invariant-rapportering)
- **Fil-referens:** [src/JobbPilot.Domain/Resumes/Resume.cs:26-27](../../src/JobbPilot.Domain/Resumes/Resume.cs#L26-L27)
- **Beskrivning:** `MasterVersion => _versions.Single(...)` kastar `InvalidOperationException` om invarianten bryts (0 eller >1 Master). Kommentaren noterar "Kastar om invarianten 'exakt en aktiv Master' bryts" — bra dokumentation, men exception-typen är generisk. En `DomainException` (eller dedikerad `ResumeInvariantViolationException`) skulle ge audit-trail-kontext.
- **Risk:** Mycket låg. Endast nått om EF Core-rehydrering levererar inkonsistent state, vilket är db-corruption-scenario.
- **Rekommenderad åtgärds-scope:** in-block-fix (~15 min CC-tid). Wrap `Single()` i custom guard som kastar `DomainException("Resume.MasterInvariantBroken", ...)`. CLAUDE.md §3.4 säger "Aldrig `throw new Exception(...)` — alltid specifik subclass" — `InvalidOperationException` (från `Single()`) är technically ärvd från `SystemException`, gråzon.
- **Motivering för scope:** Trivial.

---

## 4. Strukturella spärrar som FUNGERAT genom historien

Motvikt mot "allt är problem"-bias. Vad de 24 arch-testerna + ADR-disciplinen + agent-reviews-pipeline faktiskt har fångat och låst:

1. **Domain-isolering är hermetisk.** `DomainLayerTests.Domain_should_not_depend_on_any_other_project` skannar 8 namespaces inklusive `Mediator`, `FluentValidation`, EF Core, ASP.NET. Domain importerar bara sig själv.
2. **Application↔Infrastructure-gränsen är låst.** Tre separata tester: `Application_should_not_depend_on_Infrastructure`, `Application_should_not_depend_on_AspNetCore`, `Application_should_not_depend_on_EFCore_database_providers`. Application får använda `Microsoft.EntityFrameworkCore` (per medveten ADR 0009-kompromiss) men inte Npgsql, SqlServer, Sqlite eller Relational.
3. **Worker-HTTP-isolering bevarad.** `WorkerLayerTests.Worker_should_not_depend_on_AspNetCore_Http_or_Identity` — trots TD-19 (Hangfire.AspNetCore drar HTTP transitivt) bryts inte den explicita policyn av Worker-egna typer.
4. **Pipeline-ordning är single source of truth.** `MediatorPipelineBehaviors.InOrder` används av båda composition roots OCH verifieras av arch-test. Drift mellan Api/Worker är omöjlig utan att build:n bryter.
5. **Audit-bypass-portar har explicit konsument-allowlist.** 7 arch-tester (`AuditingLayerTests`) lockar `IAuditPartitionMaintainer`, `IAuditTrailEraser`, `IAccountHardDeleter`, `IIpAnonymizer`-konsumenterna per lager. Ny konsument måste explicit godkännas via test-update — medveten review krävs.
6. **Aggregate-invarianter har structural protection.** `Domain_aggregates_should_only_have_private_setters` skannar reflektivt alla AggregateRoot-subklasser. `AuditLogEntry_should_have_no_public_setters` är dedikerat eftersom det inte är aggregate root.
7. **Trust=true-läckage är IL-skannat.** `ConnectionStringLeakageTests` använder Mono.Cecil för Ldstr-introspektion. Catches `Trust Server Certificate=true` i alla Api/Worker/Infrastructure-assemblies. Migrate explicit exkluderad med dokumenterad rationale.
8. **Paged-query-kontrakt låst reflectivt.** `PagedResultContractTests` upptäcker bare-array-returns från queries med paged-semantik. Förhindrar TD-55-regression.
9. **IAuditableCommand-placering låst.** `IAuditableCommand_implementations_should_reside_in_Commands_namespaces` — queries kan inte av misstag bli auditerade.
10. **DI-disciplin synlig per lager.** Tre separata `Infrastructure.DependencyInjection.Add*`-extensions: `AddPersistence` (HTTP-fri), `AddIdentityAndSessions` (HTTP-only), `AddHttpAuditing` (HTTP-only), `AddCoreIdentityForWorker` (Worker-only). Modulariteten är inte teoretisk — Worker/Program.cs anropar bara HTTP-fria varianterna.
11. **`EnsureSafeForEnvironment`-mönster konsistent.** Replikerat över `HstsOptions`, `ForwardedHeadersConfig`, samt `HangfireWorkerOptions`-runtime-check. Allow-list (Development/Test) → fail-loud i alla andra miljöer. Skydd mot tyst säkerhetsregression vid env-overlay-fel.
12. **CLAUDE.md §5.1 anti-pattern-katalogen är ren.** Grep efter `DateTime.UtcNow`, `.Result`/`.Wait()`, `dynamic`, Repository, AutoMapper, `catch (Exception)` i `src/` → noll träffar. Disciplinen håller.
13. **Soft-delete via global query filter.** `ApplicationConfiguration` etc. har `HasQueryFilter(a => a.DeletedAt == null)`. Handlers använder `IgnoreQueryFilters()` explicit när soft-deleted-rows behövs — pattern är synligt och medvetet.
14. **xmin concurrency-token via PostgreSQL system-kolumn.** `builder.Property<uint>("xmin").IsConcurrencyToken()` — ingen DDL-rad behövd. Smart EF Core-utnyttjande, inte primitive `[Timestamp]`-attribut.

---

## 5. Rekommendation för Fas 2

### Deep-dive-värda hot spots

**Inga.** Det finns inget Major eller Blocker som motiverar en dedikerad Fas 2-session.

### Föreslagen ordning för in-block-fix (snabba wins, ~3h CC-tid totalt)

1. **N-1 (Application/JobSeeker SoftDelete-events)** — 30 min, Klas-val på riktning behövs.
2. **H-4 (paging-property-rename)** — 30 min, ren kompilator-driven refactor.
3. **N-3 (Resume.MasterVersion exception-typ)** — 15 min, trivial.
4. **N-2 (IdempotentAdminRoleSeeder prod-gate)** — 1h, bestäm policy.
5. **H-3 (SessionAuthHandler role-fetch → IClaimsTransformation)** — 1h, ren ASP.NET-refactor utan att röra Application.

### TD-värda fynd (passar TD-list-deferral, fixas naturligt vid framtida feature)

- **H-1 (IAccountHardDeleter ISP-split)** — defer till Fas 6 admin-impersonation-arbete; arch-test låser tills dess.
- **H-2 (ICurrentJobSeeker-port)** — defer till impersonation-feature; lyfts naturligt när logiken behöver utvidgas.

### Acceptera som "rimligt val givet konstraint X"

- **TD-19 (Hangfire.AspNetCore transitivt HTTP)** — ADR 0023 dokumenterar avvägningen.
- **TD-29 (`/api/ready` är liveness)** — Fas 2 strict-readiness är spec'd.
- **`PagedResult.Items` exponerar IReadOnlyList<T>** — DTO-yta, inte aggregate-mutation, OK.
- **`Resume.MasterVersion` invariant-baserad `Single()`** — defended av domain-create-flöde; N-3 är cosmetic.
- **`MediatorPipelineBehaviors` registrering som open-generic Scoped** — Mediator.SourceGenerator 3.x-krav, inget val.

### Föreslagen Fas 2-sekvens

Eftersom inga Major-fynd finns: **ingen sub-block-sekvens behövs**. Kör H-3 + H-4 + N-1 + N-2 + N-3 som ett "polish-block" på ~3h om Klas vill ha 100% clean inför Fas 2-feature-arbete. Annars defer alla och fortsätt feature-arbetet — ingen av dem blockar något.

### Estimat

- **Allt in-block-fix:** ~3h CC-tid, en session.
- **Split:** sub-block A (N-1 + H-4 + N-3, ~1.25h) + sub-block B (N-2 + H-3, ~2h).
- **H-1 + H-2 (TDs):** ~5h CC-tid om de fixas direkt — rekommenderas defer.

---

## Relevanta filer för Fas 2-uppföljning

- [src/JobbPilot.Application/Auth/Jobs/HardDeleteAccounts/IAccountHardDeleter.cs](../../src/JobbPilot.Application/Auth/Jobs/HardDeleteAccounts/IAccountHardDeleter.cs) (H-1)
- [src/JobbPilot.Infrastructure/Auth/AccountHardDeleter.cs](../../src/JobbPilot.Infrastructure/Auth/AccountHardDeleter.cs) (H-1)
- [src/JobbPilot.Application/Applications/Commands/CreateApplication/CreateApplicationCommandHandler.cs](../../src/JobbPilot.Application/Applications/Commands/CreateApplication/CreateApplicationCommandHandler.cs) (H-2, mönster-representant)
- [src/JobbPilot.Infrastructure/Auth/SessionAuthenticationHandler.cs](../../src/JobbPilot.Infrastructure/Auth/SessionAuthenticationHandler.cs) (H-3)
- [src/JobbPilot.Application/Common/PagedResult.cs](../../src/JobbPilot.Application/Common/PagedResult.cs) (H-4)
- [src/JobbPilot.Application/Resumes/Queries/GetResumes/GetResumesQuery.cs](../../src/JobbPilot.Application/Resumes/Queries/GetResumes/GetResumesQuery.cs) (H-4)
- [src/JobbPilot.Domain/Applications/Application.cs](../../src/JobbPilot.Domain/Applications/Application.cs) (N-1)
- [src/JobbPilot.Domain/JobSeekers/JobSeeker.cs](../../src/JobbPilot.Domain/JobSeekers/JobSeeker.cs) (N-1)
- [src/JobbPilot.Infrastructure/Identity/IdempotentAdminRoleSeeder.cs](../../src/JobbPilot.Infrastructure/Identity/IdempotentAdminRoleSeeder.cs) (N-2)
- [src/JobbPilot.Domain/Resumes/Resume.cs](../../src/JobbPilot.Domain/Resumes/Resume.cs) (N-3)
- [tests/JobbPilot.Architecture.Tests/](../../tests/JobbPilot.Architecture.Tests/) (alla 6 arch-test-filer — strukturella spärrar)
