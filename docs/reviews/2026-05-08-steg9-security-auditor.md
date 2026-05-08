---
review: security-auditor
fas: STEG 9 — pre-commit security audit
datum: 2026-05-08
status: APPROVED med 2 Major + 4 Minor (inga Blockers)
relaterad-adr: 0010, 0019, 0022, 0023 (kommande)
relaterad-build: §3.1, §16.2, §18, §7.1
veto: ej utövad
---

# security-auditor — STEG 9 pre-commit-granskning

Granskat scope: Worker-pipeline-aktivering, Hangfire-setup, `DetectGhostedApplicationsJob`, Worker-stubs av audit-portar, CVE-pinning av Newtonsoft.Json, migration `AddApplicationStaleDetectionFields`, integration-tests, architecture-tests.

**Sammanfattning:** Ren och säkerhetsmedveten implementation. Audit-paritet i Worker-context fungerar (verifierat via integration-test som assertar `entry.UserId.ShouldBeNull(...)`). Pipeline-bug-regressionen är hård-låst via architecture test. Inga GDPR-blockers, ingen ny PII-yta, ingen secret-läckage. Två Major handlar om operationell härdning (Hangfire prod-flagga + DB-permissions). Fyra Minor är defense-in-depth.

**Inga Blockers — commit godkänd ur säkerhetsperspektiv.**

---

## Critical (Blockers)

Inga.

---

## Major (bör fixas innan eller kort efter prod-deploy)

### MAJ-1 — `PrepareSchemaIfNecessary = true` ska flippas till `false` i prod, schema-permissions måste dokumenteras

**Fil:** `src/JobbPilot.Worker/Program.cs:54-58`

**Vad:** Hangfire-storage konfigureras med `PrepareSchemaIfNecessary = true`. Det innebär att Worker vid varje uppstart i prod kan köra schema-migrationer mot DB. Utöver schema-skapnings-rättigheter (vilket DB-användaren generellt inte ska ha i prod) öppnar det också för en silent migration-konflikt mellan Worker-instanser om ni rullar två concurrent.

**Varför säkerhetsrelevant (Major, ej Blocker):**
- **Privilege-escalation-yta:** Worker-DB-användarens GRANT-set bestämmer attack-blast-radius om Worker komprometteras. `CREATE SCHEMA` + `CREATE TABLE` är breda permissions som inte ska finnas i runtime-användaren.
- **Race condition på första deploy:** Två concurrent Worker-instanser som båda ser "schema saknas" kan båda försöka skapa det. Hangfire 1.21.1 har advisory locks men dependency på timing.

**Status i kod:** Kommentar erkänner detta ("`I prod sätts false och schema migreras via runbook`") men ingen TD-post är synlig och ingen runbook finns.

**Föreslagen åtgärd:**
1. Lägg till TD-post (`docs/tech-debt.md`) eller bind till ADR 0023: "Hangfire schema-migration-runbook + flagg-flip innan Fas 1 prod-deploy."
2. Skapa stub `docs/runbooks/hangfire-schema.md` med (a) initial schema-skapnings-DDL, (b) GRANT-modell — Worker-runtime-user `SELECT/INSERT/UPDATE/DELETE` på `hangfire.*`, en separat migrations-user för DDL.
3. Konfig-overlay i Worker som läser `PrepareSchemaIfNecessary` från `appsettings.{env}.json` istället för hårdkod (false i prod, true i dev/test).

### MAJ-2 — Hangfire dashboard-route saknas men ingen authorization-policy är förbeställd

**Fil:** `src/JobbPilot.Worker/Program.cs` (Worker är inte HTTP-host — gäller framtida exposure)

**Vad:** Worker är generic host idag, ingen HTTP-yta. Om Hangfire dashboard någonsin exponeras i Api eller deploy-helper måste den default-skyddas — `app.UseHangfireDashboard("/hangfire")` är **publik default** och exponerar:
- Alla recurring jobs (inklusive deras lambda/expression)
- Job-arguments (kan innehålla user-IDs / aggregat-IDs)
- Pågående/failed jobs med stack traces (potentiellt PII i exception-data)

**Varför säkerhetsrelevant:** Detta är en känd Hangfire-fotpistol — dashboard utan `IDashboardAuthorizationFilter` är default open. Det är inte i scope för denna commit men ska förebyggande dokumenteras eftersom diff:en aktiverar Hangfire-stacken.

**Status i kod:** Inget dashboard-anrop. Nuvarande commit är säker.

**Föreslagen åtgärd:** Lägg till en TD-post eller en `// SECURITY:`-kommentar i `Program.cs`: "Hangfire dashboard får aldrig exponeras utan custom `IDashboardAuthorizationFilter`. När Api/dev-tooling vill nå dashboard, kräv admin-policy + IP-restriktion + audit-logg av access."

---

## Minor (defense-in-depth, valfria)

### MIN-1 — Architecture-test täcker inte `Microsoft.AspNetCore.Authentication.Cookies` / `Microsoft.AspNetCore.Authorization`

**Fil:** `tests/JobbPilot.Architecture.Tests/WorkerLayerTests.cs:25-32`

**Vad:** Test förbjuder `Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.Identity`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`. Lista är defensiv men inte uttömmande:
- `Microsoft.AspNetCore.Authentication.Cookies` (cookie-auth bagage — ingen anledning för Worker)
- `Microsoft.AspNetCore.Authorization` (policy-eval — Worker ska inte göra)
- `Microsoft.AspNetCore.Hosting` (vs `Microsoft.Extensions.Hosting` som Worker använder)

**Föreslagen åtgärd (icke-blockerande):** Utöka listan, eller flippa till en allow-list-pattern: "Worker får INTE bero på `Microsoft.AspNetCore.*` förutom JSON-serialisering". Tradeoff är att architectures-tests blir mindre brittle på vissa NuGet-uppdateringar.

### MIN-2 — `WorkerSystemUser` Singleton är korrekt, men exponerar konstant `IsAuthenticated = false` även för commands utan `IAuthenticatedRequest`

**Fil:** `src/JobbPilot.Worker/Auditing/WorkerSystemUser.cs`

**Vad:** Worker-stub returnerar `UserId = null`. AuthorizationBehavior släpper igenom (correct — `MarkGhostedCommand` saknar `IAuthenticatedRequest`). Men handler eller domain-logik som anropar `currentUser.UserId.Value` direkt får `InvalidOperationException` vid runtime. Idag är `MarkGhostedCommandHandler` ren — den läser bara DbContext + clock. OK.

**Föreslagen åtgärd:** När fler Worker-jobb läggs till (Fas 2 JobTech-sync), instruera att alltid läsa `currentUser.UserId` som `Guid?` via null-coalescing eller pattern-match, aldrig `.Value`. Lägg till test/lint-rule när scope växer. Idag — bara dokumentera i ADR 0023 (kommer skrivas).

### MIN-3 — Migration-backfill med `NOW()` är säker mot mass-batch-ghosting men har en subtil race vid migration

**Fil:** `src/JobbPilot.Infrastructure/Persistence/Migrations/20260508093139_AddApplicationStaleDetectionFields.cs:53-54`

**Vad:** `UPDATE applications SET last_status_change_at = NOW() WHERE last_status_change_at IS NULL;` — exekveras mellan `AddColumn(nullable=true)` och `AlterColumn(nullable=false)`. Korrekt mönster.

**Säkerhetsanalys:** Det enda massbatch-ghosting-fönster som öppnar är om migrationen dröjer 21+ dagar mellan `AddColumn` och `AlterColumn` — inte en realistisk attack-vektor. Klas tillägg #1 är säkerhetsmässigt korrekt:
- Befintliga apps får 21-dagars-fönster räknat från migrationsdatum
- Första cron-körning efter prod-deploy kan **inte** mass-ghosta historiska apps
- Status-mismatch-edge-case: en app som var Submitted för 30 dagar sedan, har sedan redan transitionerat till Acknowledged, kommer att få sin `last_status_change_at = migrations-tidpunkt`. Det är fel värde semantiskt (sann tid var Acknowledged-transition), men det är **konservativt fel** — appen får längre liv, inte ghostas felaktigt.

**Föreslagen åtgärd:** Ingen kodändring. Dokumentera i migration-kommentar (eller ADR 0023) att första körningens dataset är "kalibrerings-fas" och första 21 dagarna efter deploy ska Klas följa Hangfire-dashboard för anomaliska volymer. Defensiv runbook-anteckning.

### MIN-4 — `ConnectionStrings:Postgres` används både för EF-Migrations + Hangfire — ingen separat least-privilege-DB-user för Hangfire

**Fil:** `src/JobbPilot.Worker/Program.cs:46-47`

**Vad:** Samma connection string driver `AppDbContext` (där Hangfire INTE har access — `hangfire`-schemat är separat) och `Hangfire.PostgreSQL` (där `applications`-tabellen INTE behövs).

**Säkerhetsanalys:** Lateral access-yta — om Hangfire har sårbarhet som leakar/logar query-arguments kan den teoretiskt komma åt `applications`. I praktiken är Hangfire 1.21.1 + Newtonsoft 13.0.3 patched. Risken är låg men separation-of-privilege rekommenderar två connection strings:
- `ConnectionStrings:Postgres` — Worker-app-user (SELECT/INSERT/UPDATE på `public.*`)
- `ConnectionStrings:HangfireStorage` — Hangfire-user (SELECT/INSERT/UPDATE/DELETE bara på `hangfire.*`)

**Föreslagen åtgärd:** Lägg till TD-post för Fas 1 prod-deploy: "Splittra DB-connection strings — Hangfire får separat least-privilege user." Kostsam i dev/test (kräver två DB-users), defereras tills prod-deploy. Inte i scope för denna commit.

---

## Verifierat OK (positiva findings)

### Audit-paritet i Worker-context — KORREKT

- `WorkerSystemUser`: `UserId = null`, `IsAuthenticated = false` — audit-rad får `user_id = NULL` (verifierat i `DetectGhostedApplicationsJobIntegrationTests.RunAsync_StaleSubmittedApplication_TransitionsToGhostedAndWritesAuditEntry` med explicit `entry.UserId.ShouldBeNull(...)`-assertion)
- `WorkerCorrelationIdProvider`: instans-fält `Guid _id = Guid.NewGuid()` med Scoped DI — ger en correlation-ID per Hangfire-job-execution. Hangfire skapar fresh `IServiceScope` per invokation via `JobActivator`, så correlation-ID läcker inte mellan jobb. Verifierat genom design.
- `WorkerRequestContextProvider`: `IpAddress = null`, `UserAgent = null` — korrekt för system-jobb. GDPR Art. 5(1)(c) (data-minimering på IP) tillämpligt — null är minst möjliga data.

### Privilege-escalation — INGA HÄNDIGA YTOR

- `MarkGhostedCommand` saknar `IAuthenticatedRequest` (kommentar i kod erkänner detta är medvetet och varnar mot API-exposure utan RBAC). `AuthorizationBehavior` släpper igenom utan auth-krav — korrekt för system-jobb.
- `MarkGhostedCommandHandler` läser bara `appId` från command + `DbContext` — inga user-kontroller, inga ownership-kontroller. Det är medvetet eftersom det är system-jobb. Eventuell framtida API-exposure kräver explicit RBAC + ownership-check.
- Worker-DI registrerar **inte** `AddIdentityAndSessions` eller `AddHttpAuditing`. Architecture test (`Worker_should_not_depend_on_AspNetCore_Http_or_Identity`) cementerar detta som regression-skydd.

### Pipeline-bug-fix robusthet — REGRESSIONSSKYDDAD

- `MediatorPipelineBehaviors.AddMediatorPipelineBehaviors()` använder open-generic DI-registrering (`services.AddScoped(typeof(IPipelineBehavior<,>), behaviorType)`) istället för Mediator's `options.PipelineBehaviors = ...`-fält som inte kunde analyseras compile-time av source generator. Detta är **standard Mediator-pattern** och fungerar pålitligt.
- Architecture test `MediatorPipeline_should_have_expected_behaviors_in_order` cementerar att array-ordningen inte ändras tyst — om någon backar fixen failar testet. **Bra regression-skydd.**
- **Bonus-skydd:** ett integration-test som verifierar att en `IAuditableCommand` faktiskt skriver en audit-rad. Detta finns i `DetectGhostedApplicationsJobIntegrationTests` (verifierar att audit-rad skapas + user_id = null), så även om någon framtida dev tar bort `AddMediatorPipelineBehaviors()`-anropet failar smoke-testet. Bra dual-coverage.

### CVE-pinning Newtonsoft.Json 13.0.3 — KORREKT VAL

- 13.0.3 är senaste stabila och första post-fix release-linjen för CVE-2024-21907 (GHSA-5crp-9r3c-p9vr).
- `CentralPackageTransitivePinningEnabled` (Directory.Build.props) är aktiverat per kommentar — pinningen propagerar transitivt.
- Hangfire 1.8.23 är senaste stabila och har inga öppna högrisk-CVE:s. `dotnet list package --vulnerable --include-transitive` rekommenderas innan prod-deploy som ops-procedur.
- Hangfire.PostgreSQL 1.21.1 är senaste och underhållen.

### GDPR — INGA NYA PII-YTOR

- `LastStatusChangeAt` (timestamptz): timing-data, **inte** PII självt. Tillsammans med `JobSeekerId`-FK utgör det aktivitetsmönster — men `applications`-tabellen redan har `CreatedAt`/`UpdatedAt` med samma natur, inget nytt risk-tillägg.
- `GhostedThresholdDays` (int): konfig-värde, inte PII.
- Ingen ny audit-fält tillagd. Inget nytt logging-yta. Inga external integrations. Ingen consent-fråga.
- Migration backfill rörsigt PII-data (`UPDATE applications`) — körs inom DB-transaktion, ingen extern exponering.

### Soft-delete-paritet — VERIFIERAD

- `StaleApplicationSpecification.CandidateStatusFilter` exkluderar inte `DeletedAt IS NULL` — kommentar förklarar att `AppDbContext`s globala query filter (per `ApplicationConfiguration`) hanterar det. Test `RunAsync_SoftDeletedApplication_RemainsUnchanged` verifierar att soft-deleted apps inte ghostas.
- Partial-index `ix_applications_stale_detection` har `WHERE ... AND deleted_at IS NULL` — index-paritet med query.

### Concurrency / idempotens — SÄKER

- `MarkGhostedCommand`-handler är idempotent: `if (app is null) return Result.Success()` + `Application.MarkGhosted` skyddar med `if (Status != Submitted && Status != Acknowledged) return Result.Success()`.
- Två concurrent Worker-instanser som båda får "scan-jobb-tick" är skyddade av Hangfire's distributed lock (advisory lock i Postgres) på recurring-job-execution.
- `WorkerCount = 4` är inom rimlig gräns för IO-bunden Mediator-dispatch. Ingen DoS-yta mot DB (`AsNoTracking + Select` materialiserar inte hela aggregat).

### Test-isolation — INTEGRATION-TESTS ÄR FLAKY-TOLERANTA

- `WorkerTestFixture` är `ICollectionFixture` per `WorkerCollection.cs`. Det betyder **shared Postgres-container** mellan alla tester i `[Collection("Worker")]`. Risk: state-leakage mellan tester.
- **Mitigering i test-design:** varje test seedar med `Guid.NewGuid()` för `JobSeeker`-ID och får unik `ApplicationId.New()` — ingen overlap. Audit-entries kontrolleras på sin egen aggregate-id — bra isolering.
- **Kvarstående risk:** `RunJobAsync` skannar HELA tabellen, så om ett tidigare test seedat en stale Submitted-app som ännu finns kvar med samma "now"-tidpunkt → ghostas av nästa test. Tester använder dock olika `statusChangeAt` + olika `now`, men ett tidigare tests stale-app ghostas vid nästa körning utan side-effect på det testets assertion.
- **Föreslagen åtgärd (Minor — inte i scope för denna commit):** Lägg en cleanup `IAsyncLifetime.DisposeAsync` på själva test-klassen (`InitializeAsync` truncatar `applications`, `audit_log`, `job_seekers` mellan tester). Idag **fungerar testerna**, men de är teknisk skuld inför fler tester i samma collection.

---

## Sammanfattning

| Kategori | Antal | Status |
|---|---|---|
| Critical (blockers) | 0 | — |
| Major | 2 | Pre-prod, inte commit |
| Minor | 4 | Defense-in-depth |
| Verifierat OK | 7 områden | — |

**Veto:** ej utövad. Commit godkänd.

**Pre-prod-deploy-krav (MAJ-1 + MAJ-2):** ska adresseras innan Fas 1 går till prod, inte innan denna commit. Spåra som TD eller bind till ADR 0023 (som skrivs i Fas 9.8).
