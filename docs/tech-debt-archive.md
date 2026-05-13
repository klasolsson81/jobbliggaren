# Tech Debt — Arkiv (stängda TDs)

Stängda TD-poster i kronologisk ordning på stäng-datum (äldsta först). Full
kropp bevaras för audit-trail, ADR-cross-references och granskningsbevis per
CTO-triage 2026-05-11.

Aktiva TDs finns i [`tech-debt.md`](./tech-debt.md). Refactor-bakgrund:
denna fil etablerades 2026-05-11 när tech-debt.md splittes till
aktiv-fil + arkiv-fil (Severity × Fas-matris för aktiva, kronologisk
arkiv för stängda) per Klas-direktiv + senior-cto-advisor-triage.

---

## TD-9: Audit log saknas för Application-domänhändelser ✓ STÄNGD STEG 8 (2026-05-08)
**Kategori:** GDPR / Compliance
**Severity:** Major
**Källa:** security-auditor, STEG 5 (2026-05-07)
**Stängd:** STEG 8 (2026-05-08) — implementerad enligt ADR 0022

GDPR Art. 5(2) kräver att behandling av personuppgifter kan redovisas
(accountability). Application-aggregatets domänhändelser (skapande,
status­övergångar, noteringar, uppföljningar) loggas inte till
audit-trail. `IAuthAuditLogger` finns men är bunden till auth-flödet.

**Risk:** Kan inte bevisa vem som skapat eller ändrat en ansökan om tvist
uppstår eller Datainspektionen begär redovisning.

**Föreslagen åtgärd:** Implementera pipeline-behavior
`ApplicationAuditBehavior` (eller domain event handler) i Fas 1 som
skriver till en `application_audit_log`-tabell. Kräver ny migration,
ny `IApplicationAuditLogger`-abstraktion i Application-lagret, och
implementation i Infrastructure. Notera: ADR behövs för val av audit-
strategi (inline i handler vs. domain event subscriber vs. pipeline behavior).

**Stängningsnoteringar (STEG 8):**
- Strategi: pipeline-behavior + marker-interface (ADR 0022)
- Tabell: gemensam `audit_log` (per BUILD.md §7.1) — inte separata
  per-aggregat-tabeller. Täcker både Application och Resume.
- 10 commands markerade `IAuditableCommand<TResponse>` (5 Application + 5 Resume)
- Atomicitet via UoW.SaveChanges (audit-rad och data-mutation persisteras
  i samma transaction)
- Tester: 14 Domain + 11 Application + 12 Integration + 4 Architecture (41 nya)
- **Operativa beroenden vid prod-deploy:** se TD-16 (retention + Art. 17)

---
### TD-16 — Audit-log retention + GDPR Art. 17-anonymisering ✓ STÄNGD STEG 10a+10b (2026-05-08)

**Kategori:** GDPR / Compliance / Data retention
**Fas:** 1
**Prioritet:** Hög (Fas 1 prod-deploy-blockare)
**Källa:** Security audit STEG 8 2026-05-08 (Major M1 + Major M2)
**Status:** **STÄNGD KOMPLETT** 2026-05-08.
- Del 1 (audit-retention) stängd via STEG 10a (ADR 0024 D1+D2). 90-dagars partition-rotation via Hangfire-jobb.
- Del 2 (Art. 17-cascade + DELETE /me) stängd via STEG 10b (ADR 0024 D3-D6). IAuditTrailEraser + DeleteAccountCommand + HardDeleteAccountsJob.
- Runbook: `docs/runbooks/audit-retention.md` + `docs/runbooks/account-deletion.md`
- Tester: 22 arch + 16 worker smoke + 5 api integration för STEG 10a+b kombinerat

ADR 0022 specificerar 90-dagars retention för `audit_log` via PostgreSQL daily
partitioning, samt anonymiseringspolicy vid GDPR Art. 17-radering (user_id,
ip_address, user_agent → NULL; övriga fält behålls 90 dagar för accountability
per Art. 5(2)). Båda mekanismerna är **dokumenterade men inte implementerade**
i Fas 1.

**Risk i Fas 1 dev:** noll (ingen produktion, ingen riktig PII).

**Risk vid Fas 1 prod-deploy:** Major.

- Audit-tabellen växer obegränsat → bryter Art. 5(1)(e) storage limitation
- GDPR Art. 17-begäran kan inte utföras korrekt utan anonymiserings-cascade
- Datainspektionen kan ifrågasätta retention-policy om ingen aktiv mekanism finns

**Föreslagen åtgärd (Fas 4 eller tidigare innan prod-deploy):**

1. Hangfire-jobb `AuditLogRetentionJob` som dagligen:
   - Skapar nästa dags partition (`audit_log_YYYYMMDD`)
   - Droppar partitions äldre än 90 dagar
2. Application-command `EraseUserAuditTrail(userId)` som cascade-anropas
   av kontoraderings-flödet (`DELETE /me` Fas 1 eller admin-erase Fas 6).
   Implementeras som direct SQL UPDATE (audit-bypass — write-only-disciplinen
   gäller normala flöden, inte GDPR-anonymisering).
3. Runbook `docs/runbooks/audit-retention.md` med manuell ops-procedur
   (`pg_partman`-eller-manuell-ALTER-TABLE) som fallback om Hangfire-jobbet
   inte körts på X dagar.

**Beroenden:** Hangfire-setup (Fas 2) måste vara klar. ADR krävs eventuellt
för audit-bypass-pattern (cascade-anonymisering bryter "audit är write-only"-
invarianten — motiverat val men dokumenteras).

**Tester:**
- Integration-test som verifierar att daily partition skapas/droppas korrekt
- Integration-test för anonymiserings-cascade vid kontoradering
- Smoke-test som verifierar att `EraseUserAuditTrail` inte påverkar
  `correlation_id`/`event_type`/`aggregate_id` (bara identifierande fält)

---
### TD-22 — App-logg-retention + IP/UA-redaction ✓ DELVIS STÄNGD STEG 11 (2026-05-08)

**Kategori:** Säkerhet / GDPR / Data retention
**Fas:** 1 (innan prod-deploy)
**Prioritet:** Hög (urholkar Art. 17-anonymisering annars)
**Källa:** Security audit STEG 10b 2026-05-08 (Major-3)
**Status:** **Delvis stängd** STEG 11 (ADR 0024 D7).
- ✓ Policy-beslut: 30d retention för CloudWatch (matchar Art. 17-fönstret)
- ✓ Logg-tid-redaction implementerad: `IIpAnonymizer`-port (Application) +
  `IpAnonymizer`-impl (Infrastructure) lyft från `RequestContextProvider`
  och konsumeras nu även av `AuthAuditLogger` (defense-in-depth)
- ✓ ADR-tillägg landad: ADR 0024 D7 (App-logg-redaction + retention-policy)
- ✓ Tester: 8 IpAnonymizer Theory + 3 nya AuthAuditLogger-tester (IPv4
  anonymisering vid logg-tid, "unknown" fallback)
- **Utestående för Fas 0-stängning:** CloudWatch LogGroup retention=30 sätts
  via AWS-konfig (IaC eller konsol) vid första prod-deploy
- **Utestående för Fas 2:** EmailHash-HMAC med roterande nyckel — se TD-27

Audit-tabellen anonymiseras efter Art. 17 (user_id/ip/user_agent → NULL
efter 30 dagars restore-fönster). MEN app-loggen (CloudWatch sink i prod,
Seq i dev) innehåller IP + UA + email-hash vid login-failures, oberoende
av audit-tabellen. Även efter Art. 17-anonymisering kan en angripare
re-identifiera användare via app-logg-data.

Kombinationen email-hash + IP + UA är **pseudonym** (GDPR Art. 4(5)).
Samma user kan korreleras över tid via app-loggen även efter audit-
anonymisering.

**Risk i Fas 1:** noll (dev-loggar, ingen prod-data).
**Risk vid Fas 1 prod-deploy:** Mitigerad efter STEG 11. CloudWatch LogGroup
retention=30 är sista operativa pusslebit för full Fas 1 prod-deploy-
godkännande.

**Stängningsnoteringar (STEG 11):**
- `IIpAnonymizer.Anonymize(IPAddress)` är stateless BCL-port, registrerad som
  Singleton i `AddPersistence` så både Api+Worker har den tillgänglig
- `RequestContextProvider` refaktorerad till att delegera till porten
  (ingen logikförändring mot audit-tabellen)
- `AuthAuditLogger` injicerar nu porten + maskar IP innan
  `LogLoginSucceeded`/`LogLoginFailed`/`LogLogoutSucceeded`-anropen
- 470 backend-tester gröna efter ändring (157 Domain + 182 Application
  +11 nya + 22 Architecture + 109 Api Integration)

---
### TD-17 — Hangfire prod-härdning ✓ KOD-DELEN HELT STÄNGD STEG 12 (2026-05-09)

**Kategori:** Säkerhet / Operations
**Fas:** 1 (innan prod-deploy)
**Prioritet:** Hög (blocker för Fas 1 prod-deploy, tillsammans med TD-16)
**Källa:** Security audit STEG 9 2026-05-08 (MAJ-1, MAJ-2, MIN-3, MIN-4) + ADR 0023
**Status:** **Kod-delen helt stängd** efter STEG 12. Punkt 1, 2, 3, 5, 6 stängda
STEG 11. Punkt 4 (ConnectionStrings split-fallback i kod) stängd STEG 12 via
`HangfireConnectionStringResolver`. Operativ split (två AWS Secrets Manager-
poster + två Postgres-roller) appliceras i STEG 13/14 enligt runbook §4.

**STEG 12-tillägg (kod-delen av punkt 4):**
- ✓ `HangfireConnectionStringResolver.Resolve(IConfiguration)` — statisk testbar metod
  med fallback-kedja `HangfireStorage → Postgres`. Prod-overlay sätter `HangfireStorage`
  → routar Worker till `jobbpilot_worker`-rollen (DML-only på `hangfire.*`); dev faller
  tillbaka på `Postgres` (en sanning lokalt, ingen split-overhead).
- ✓ `Worker/appsettings.Production.json` overlay (`PrepareSchemaIfNecessary: false` +
  `ShutdownTimeoutSeconds: 25`) committad. ConnectionStrings injiceras via env-vars
  från ECS task-definition + AWS Secrets Manager — committas INTE i overlay.
- ✓ Felmeddelandet pekar konkret på env-var (`ConnectionStrings__HangfireStorage`) +
  Secrets Manager-path-konvention (`jobbpilot/<env>/postgres-worker`) + runbook §4.
- +5 tester (HangfireStorage-prefer + Postgres-fallback + throw-both-missing + null-arg
  + const-stability).

**Stängningsnoteringar (STEG 11):**
- ✓ Punkt 1 — `HangfireWorkerOptions.PrepareSchemaIfNecessary` (default `true` Development/Test,
  övriga miljöer kräver explicit `false`-overlay). Production-defense i `Worker/Program.cs`
  använder allow-list — bara Development/Test får auto-skapa schema (kastar
  `InvalidOperationException` annars). Range-validering på `ShutdownTimeoutSeconds` (1-300)
  fail-loud vid orealistiska overlay-värden.
- ✓ Punkt 2 — `docs/runbooks/hangfire-schema.md` skapad (Install.sql-export,
  GRANT-modell, schema-state-felsökning).
- ✓ Punkt 3 — `// SECURITY:`-kommentar i `Worker/Program.cs` + dashboard-auth-checklista
  i runbook §5 (`AdminOnlyDashboardFilter` + IP-allowlist + audit-loggning).
- ✓ Punkt 5 — Kalibrerings-fas-anteckning i runbook §8 (första 21 dagar efter
  prod-deploy är detect-ghosted-anomaliska volymer förväntade).
- ✓ Punkt 6 — `BackgroundJobServerOptions.ShutdownTimeout = 25s` (default via
  `HangfireWorkerOptions.ShutdownTimeoutSeconds`) — strax under Fargate default
  stopTimeout 30s. Plus explicit `HostOptions.ShutdownTimeout = +3s` så hela
  timeout-kedjan (Hangfire 25s → Host 28s → Fargate 30s) är synlig. Idempotency-
  tabell i runbook §6 verifierar att alla 3 jobb tål abort + restart.
- ✓ Cron-kollision åtgärdad — `detect-ghosted` flyttat 03:00 → 03:30 UTC så det
  inte krockar med `audit-log-retention` (Sec-Minor STEG 11).
- ✓ REVOKE PUBLIC-block i runbook §4 (Sec-Major-2 STEG 11) — eliminerar default-
  Postgres-PUBLIC-läs-yta innan GRANT-block.
- ✓ Dashboard-checklistans utvidgning i runbook §5 (Sec-Major-3 STEG 11) — CSRF-
  version-check, rate-limiting, kort admin-session-expire, CSP-relax, no-cache
  headers, granulär audit-events, read-only-roll-not.
- ⏸ Punkt 4 — ConnectionStrings split. Runbook §4 dokumenterar GRANT-modell +
  Worker/Program.cs-ändring. Faktisk split appliceras vid första prod-deploy
  (kräver två AWS Secrets Manager-poster). Kvarstår som operativ uppgift för
  Fas 0-stängning.

**Tester:** 5 nya `HangfireWorkerOptionsTests` (defaults, section-name, full+partial
overlay, missing-section). Ingen smoke-test för SIGTERM mid-flight idempotency
(svårt att simulera utan AWS-deployment) — verifieringen sker via runbook §7.4
övervakning vid prod-deploy.

**Återstående follow-ups (icke-blocker för STEG 11-stängning):**
- Production-defense (`Worker/Program.cs` startup-throw) är inte direkt unit-tested.
  Kräver refactor till en separat policy-klass för testbarhet — defererad som
  opportunistic improvement (code-reviewer STEG 11 M2).
- Worker.csproj refererar `Hangfire.AspNetCore` som drar in `Microsoft.AspNetCore.*`
  — motverkar ADR 0023 "Worker HTTP-fri"-disciplin. Migrering till `Hangfire.NetCore`
  utvärderas som del av TD-19 (Worker defense-in-depth Fas 2).
- `appsettings.Production.json` med Hangfire-overlay-block skapas vid Fas 0-stängning
  prod-deploy (kräver två AWS Secrets Manager-poster för ConnectionStrings split).

ADR 0023 aktiverar Hangfire-infrastrukturen i Worker. Fem operationella härdnings-punkter måste adresseras innan Fas 1 går till prod:

**1. `PrepareSchemaIfNecessary` ska flippas till `false` i prod (MAJ-1):**

`src/JobbPilot.Worker/Program.cs` har idag hårdkodat `PrepareSchemaIfNecessary = true`. I prod betyder detta att Worker vid uppstart kan skapa schema + tabeller. Två problem:
- Worker-DB-användarens GRANT-set blir för brett (CREATE SCHEMA + CREATE TABLE) → privilege-escalation-yta vid kompromettering
- Race condition om två concurrent Worker-instanser båda försöker skapa schema vid första deploy

**Åtgärd:** Konfig-overlay som läser `PrepareSchemaIfNecessary` från `appsettings.{env}.json` (false i prod, true i dev/test).

**2. Hangfire-schema-runbook (MAJ-1):**

Skapa `docs/runbooks/hangfire-schema.md` med:
- Initial schema-skapnings-DDL (kör manuellt i prod innan första Worker-deploy)
- GRANT-modell: separat migrations-user (DDL-rättigheter) vs runtime-user (DML på `hangfire.*`)
- Felsöknings-procedur om schema-state är inkonsistent

**3. Hangfire dashboard får aldrig exponeras utan auth (MAJ-2):**

Worker är inte HTTP-host idag, ingen dashboard. Men om dashboard någonsin exponeras i Api eller dev-tooling måste den default-skyddas:
- Kräv custom `IDashboardAuthorizationFilter` (Hangfire-default är publik)
- Admin-roll-policy + IP-restriktion + audit-logg av dashboard-access
- Dashboard exponerar job-arguments (kan innehålla user-IDs/aggregat-IDs) + stack-traces (potentiellt PII i exception-data)

**Åtgärd:** Lägg `// SECURITY:`-kommentar i Worker/Program.cs som varnar mot direkt-anrop av `UseHangfireDashboard(...)`. Eller bind till runbook ovan.

**4. Splittra ConnectionStrings för least-privilege (MIN-4):**

Idag delar `ConnectionStrings:Postgres` mellan AppDbContext och Hangfire-storage. Lateral access-yta — om Hangfire har sårbarhet kan den teoretiskt komma åt `applications`-tabellen.

**Åtgärd vid prod-deploy:**
- `ConnectionStrings:Postgres` — Worker-app-user (SELECT/INSERT/UPDATE på `public.*`)
- `ConnectionStrings:HangfireStorage` — Hangfire-user (SELECT/INSERT/UPDATE/DELETE bara på `hangfire.*`)

Kostsam i dev/test (kräver två DB-users) — defereras tills prod-deploy.

**5. Defensiv runbook-anteckning för "kalibrerings-fas" (MIN-3):**

Migration `AddApplicationStaleDetectionFields` backfillar `last_status_change_at = NOW()` (per Klas tillägg #1). Befintliga apps får 21-dagars-fönster räknat från migrationsdatum.

**Åtgärd:** Dokumentera i runbook att de första 21 dagarna efter prod-deploy är "kalibrerings-fas" — Klas följer Hangfire-dashboard för anomaliska volymer av MarkGhostedCommand. Defensiv anteckning, ingen kodändring.

**6. Fargate SIGTERM-grace period + Hangfire ShutdownTimeout:**

Worker körs i AWS ECS Fargate-container. Default Fargate-flöde vid scale-in /
deployment / underliggande patch: SIGTERM → 30s grace → SIGKILL. Hangfire's
`BackgroundJobServerOptions.ShutdownTimeout` default är 15 sekunder.

Idag har `Worker/Program.cs` ingen explicit shutdown-konfiguration —
`AddHangfireServer` använder defaults. Vid SIGTERM avbryts pågående jobb
mid-flight. Mitigerat av att alla jobb är idempotenta (DetectGhosted,
AuditLogRetention, HardDeleteAccounts via ADR 0023+0024-design), men ger
oönskade rollbacks + retry-volym.

**Åtgärd vid prod-deploy:**

1. `BackgroundJobServerOptions.ShutdownTimeout = TimeSpan.FromSeconds(25)`
   — strax under Fargate default 30s, säkerställer att Hangfire hinner
   committa job-state innan SIGKILL
2. Eventuellt: höj Fargate task-definition `stopTimeout` till 60s om
   smoke-tests visar att SaveChanges-batches eller Hangfire-cleanup tar
   > 25s vid hög belastning
3. Verifiera idempotency-egenskaper i ny smoke-test (kör jobb, skicka
   SIGTERM mid-flight, verifiera korrekt restart)
4. Ingen kod-ändring av jobb-orchestrators krävs — befintliga retry-/
   cancel-token-mönster räcker

Källa: AWS docs, Hangfire BackgroundJobServerOptions docs, EDPB CEF 2025-
related Fargate-shutdown-rekommendationer.

**Beroenden:** ADR 0023 implementerad (klart). Inga andra beroenden — kan adresseras parallellt med TD-16 (audit-retention) eftersom båda gäller Fas 1 prod-deploy.

---
### TD-21 — Rate-limiting på DELETE /me + andra känsliga auth-endpoints ✓ STÄNGD STEG 11 (2026-05-09)

**Kategori:** Säkerhet / DoS-skydd
**Fas:** 1 (innan prod-deploy)
**Prioritet:** Hög (blocker för Fas 1 prod-deploy)
**Källa:** Security audit STEG 10b 2026-05-08 (Major-2)
**Status:** **Stängd** STEG 11 (efter Sec-Major-fixes). Tre rate-limit-policies
registrerade och verifierade:
- ✓ `account-deletion` (1 req/60s per UserId-claim "sub") på DELETE /me. Anonymous
  → `NoLimiter` (Sec-Minor-1: RequireAuthorization returnerar 401 innan endpoint).
- ✓ `auth-write` (20 req/min per IP) på POST /auth/login + POST /auth/register.
  Höjt från 10 → 20 (Sec-Minor-3: CGN/NAT-kompabilitet, OWASP-rekommenderad).
- ✓ `auth-loose` (30 req/min per IP) på POST /auth/logout.
- ✓ `RateLimitingOptions` config-driven via `RateLimiting:*`-section.
- ✓ `OnRejected`-callback (Sec-Major-3): strukturerad warning utan PII +
  `Retry-After`-header (RFC 6585-compliance) via LoggerMessage source-gen.
- ✓ `UseForwardedHeaders` middleware tillagd (Sec-Major-1) — klient-IP plockas
  från `X-Forwarded-For` när Api körs bakom proxy/ALB. Pre-launch-gate
  `KnownNetworks=ALB-VPC-CIDR` dokumenterad i `docs/runbooks/aws-setup.md` §3.3,
  appliceras operativt i STEG 13 (Terraform).
- ✓ **STEG 12-tillägg:** `ForwardedHeadersConfig` config-driven med fail-loud parse
  + `EnsureSafeForEnvironment` production-defense (Sec-Major-1 STEG 12) — tom
  KnownNetworks utanför Development/Test → uppstart-throw → ECS-container startar
  inte. Allow-list-symmetri med Worker `safeForAutoSchema`. `Api/appsettings.Production.json`
  overlay (KnownNetworks tom som pre-launch-gate, ForwardLimit=1 ALB-only).
  **Sec-Major-2 docs-fix:** `aws-setup.md §3.3` förtydligad om CloudFront
  edge-IPs i AWS-managed prefix-list (`com.amazonaws.global.cloudfront.origin-facing`)
  — bara VPC-CIDR räcker inte för ForwardLimit=2.
- ✓ Frontend typed-confirmation-UX + re-auth-prompt (punkt 3+4 från ursprungs-TD)
  → ny TD-28 (separat frontend-STEG, inte prod-blocker — UX nice-to-have ovanpå
  hard rate-limit-ceiling).

**Filer:**
- `src/JobbPilot.Api/RateLimiting/RateLimitingOptions.cs` (ny)
- `src/JobbPilot.Api/RateLimiting/RateLimitingExtensions.cs` (ny)
- `src/JobbPilot.Api/Program.cs` (`UseForwardedHeaders` + `AddJobbPilotRateLimiting` + `UseRateLimiter`)
- `src/JobbPilot.Api/Endpoints/AuthEndpoints.cs` (RequireRateLimiting på 3 endpoints)
- `src/JobbPilot.Api/Endpoints/MeEndpoints.cs` (RequireRateLimiting på DELETE /me)
- `tests/JobbPilot.Api.IntegrationTests/RateLimiting/StrictRateLimitApiFactory.cs` (ny isolerad fixture)
- `tests/JobbPilot.Api.IntegrationTests/RateLimiting/AuthWriteRateLimitTests.cs` (Sec-Major-2: 429-respons + Retry-After)
- `tests/JobbPilot.Api.IntegrationTests/xunit.runner.json` (parallelizeTestCollections=false så collections inte race:ar env-vars)
- `docs/runbooks/aws-setup.md` (§3.3 ForwardedHeaders pre-launch-gate)

**Tester:** 8 nya tester totalt:
- 6 `RateLimitingOptionsTests` (defaults per policy, section-name, binding, policy-key-stabilitet)
- 2 `AuthWriteRateLimitTests` med strict-fixture (login spam → 429 efter 20, 429 inkluderar Retry-After)
- 117 Api Integration-tester gröna (109 tidigare + 8 nya).

DELETE /me är den dyraste endpointen i appen — cascade-soft-delete laddar
*alla* user-ägda Application + Resume in-memory utan paginering, triggar
AuditBehavior + bulk Redis-invalidering. Saknad rate-limiting öppnar för:

- Auth-credential-DoS (stulen session raderar kontot impulsivt)
- Resource-DoS (power user med 10000 applications laddar hela trädet)

Plus: hela auth-yta (login, register, refresh) saknar rate-limiting per
användare/IP, vilket är credential-stuffing-yta.

**Risk i Fas 1:** noll (dev, ingen prod-trafik).
**Risk vid Fas 1 prod-deploy:** Major.

**Föreslagen åtgärd:**
1. Lägg till `services.AddRateLimiter()` med policy `account-deletion`
   (1 request / 60s per user) + global default-policy för auth-endpoints
2. `[EnableRateLimiting("account-deletion")]` på DELETE /me
3. Frontend: typed-confirmation-UX för DELETE /me (civic-utility-ton)
4. Eventuellt re-auth-prompt (password-prompt) före DELETE /me för defense-in-depth

**Beroenden:** Ingen. Kan implementeras självständigt.

---
### TD-15 — Resume-formulär: koppla Zod-issue path till `aria-invalid` per fält ✓ STÄNGD Fas 1 Block A1 (2026-05-10)

**Status:** **STÄNGD** 2026-05-10 via Fas 1 Block A1 (sub-block A1).
- Helper `fieldA11y(path)` + `FieldError`-typ implementerade
- `aria-invalid` + `aria-describedby` spread:at på 16 fält
- Focus-flytt till första fel-fält via `useEffect` + `pathToElementId`-mappning (M1 från design-review)
- Suffix `(fält: ...)` borttaget — `aria-describedby` + focus gör det redundant (n1 från design-review)
- Verifierat: tsc ren, Vitest 65/65 grön
- Reviews: design-reviewer APPROVE-WITH-FIXES (M1+n1 fixade in-block, m1+m2 lyfta till TD-39+TD-40), code-reviewer APPROVE
- Originalbeskrivning bevaras nedan för audit-trail

---

**Originalbeskrivning (TD-15 design-review STEG 7b 2026-05-08):**

**Kategori:** Accessibility (WCAG 2.1 AA SC 3.3.1, 4.1.3)
**Fas:** 1 (a11y-pass)
**Prioritet:** Medium
**Källa:** design-review STEG 7b 2026-05-08 (Major M1)

`src/components/resumes/resume-content-form.tsx` visar valideringsfel via en
toppnivå `role="alert"` med första issue:n från Zod (inkl. path som sträng).
Fältet som triggade felet får dock inget `aria-invalid="true"` och inget
`aria-describedby` som pekar på felmeddelandet — kopplingen mellan fel och
fält är endast visuell.

**Bakgrund:** RHF + manuell `safeParse` valdes över `zodResolver` p.g.a. en
typkonflikt mellan formulärlagrets `string` (number-input) och schemats
`number | null`-output. Resolver-vägen hade gett RHF:s `errors`-objekt med
path-baserad fältkoppling out-of-the-box.

**Risk:** A11y-golvet är inte brutet (felet finns synligt och annonseras via
`role="alert"`), men för komplexa formulär (4 sektioner, 3 field arrays,
20+ fält) är path-baserad fältkoppling standard.

**Föreslagen åtgärd:** Lyft `serverError`-state till att hålla path. Sätt
`aria-invalid="true"` + `aria-describedby="content-form-error"` på det fält
vars path matchar. Eller — strukturell fix: lös typkonflikten med en
"display-shape"-schema som transformerar till "wire-shape" vid submit, så
`zodResolver` kan användas och RHF ger errors-objekt direkt. Den senare ger
bättre per-field-feedback från första försök.

Adresseras lämpligen i ett a11y-pass tillsammans med TD-1, TD-2.

---
## TD-31: Test för UseHttpsRedirection env-gate (Sec-Major-2 anti-regression) ✓ STÄNGD Fas 1 Block A3 (2026-05-10)

**Status:** **STÄNGD** 2026-05-10 via Fas 1 Block A3.
- 3 integration-tester implementerade i `tests/JobbPilot.Api.IntegrationTests/Configuration/UseHttpsRedirectionGateTests.cs`
  - Test 1: Production + Alb:HttpsEnabled=false → 200 (UseHttpsRedirection ej registrerad)
  - Test 2: Production + Alb:HttpsEnabled=true → 307 + Location: https://
  - Test 3: Development → 307 (default-redirect via dev-cert)
- Pattern: abstract base + 3 sealed concrete factories (matchar ProductionStartupFactory + TD-37-läxor)
- `PostConfigure<HttpsRedirectionOptions>(opts => opts.HttpsPort = 443)` löser middleware HTTPS-port-resolution i test-host
- Verifierat: 557/557 dotnet test PASS (var 554, +3 nya)
- Reviews: code-reviewer APPROVED, dotnet-architect APPROVED-with-fixes (Mindre 1+3 fixade in-block)
- Originalbeskrivning bevaras nedan för audit-trail

---

**Originalbeskrivning (TD-31, code-reviewer STEG 13b 2026-05-09):**

**Kategori:** Testing / Security
**Severity:** Minor
**Källa:** code-reviewer, STEG 13b-fix-review (2026-05-09)

`src/JobbPilot.Api/Program.cs:114-124` env-gate:ar `UseHttpsRedirection()` baserat
på `AlbOptions.HttpsEnabled`-konfig (per ADR 0026). Detta är säkerhets-kritiskt:
om någon framtida refaktor tar bort gaten → 307→443 mot HTTP-only-ALB → deploy
fail-circuit-breaker (Sec-Major-2 STEG 13b). Anti-regression bör vara
strukturell, inte bara docs-disciplin.

**Föreslagen åtgärd:** Integration-test via `WebApplicationFactory<Program>` som:

1. **Test 1:** `Alb:HttpsEnabled=false` + `ASPNETCORE_ENVIRONMENT=Production` →
   request mot HTTP-endpoint returnerar 200 (ingen redirect)
2. **Test 2:** `Alb:HttpsEnabled=true` + `ASPNETCORE_ENVIRONMENT=Production` →
   request mot HTTP-endpoint returnerar 307 till HTTPS
3. **Test 3:** `ASPNETCORE_ENVIRONMENT=Development` (oavsett Alb-flag) → redirect
   aktiv (dev-cert via Kestrel)

Filplats: `tests/JobbPilot.Api.IntegrationTests/Configuration/UseHttpsRedirectionGateTests.cs`.

**Beroenden:** Inga blockare. Adresseras opportunistiskt vid nästa Api-test-skrivning
eller som del av STEG 13c (när ADR 0026-trigger uppfylls och flag flippas — då
behövs anti-regression mest).

---
## TD-37: Backend Integration tests fail i CI — STÄNGD per STEG 14c (2026-05-10)
**Kategori:** Testing / CI/CD
**Severity:** Major (blockerade tag-deploy via deploy-dev.yml förrän löst)
**Källa:** STEG 14a build.yml första-run, 2026-05-10 (run 25634087757)
**Stängd:** 2026-05-10 ~21:55 — Backend CI 554/554 grön (run 25637996682)

### Symptom (originalt)

Lokalt passerade alla 554 backend-tester (157 Domain + 183 Application + 23 Architecture
+ 26 Worker + 165 Api Integration). I CI på ubuntu-latest-runner med Testcontainers:

- `JobbPilot.Worker.IntegrationTests`: 1 fail (`AuditLogRetentionJobIntegrationTests.
  DropPartitionsOlderThan_DropsOldPartitionsSkipsDefaultAndRecent`)
- `JobbPilot.Api.IntegrationTests`: 88 errors (alla Auth/Applications-tester får
  `500 Internal Server Error` på `/auth/register`-endpoint)

### Root cause (identifierat via debug-middleware i STEG 14c)

**Api 88-fail:** `StackExchange.Redis.RedisConnectionException: not possible to connect
to redis server(s)` vid `JobbPilot.Infrastructure.DependencyInjection.AddIdentityAndSessions`
line 131. ApiFactory.ConfigureServices replacar `IDistributedCache` men INTE
`IConnectionMultiplexer` — den registreras separat med string captured vid
registration-time (`services.AddSingleton<IConnectionMultiplexer>(_ =>
ConnectionMultiplexer.Connect(redisConnectionString))`). Lokalt på Windows funkar
default `localhost:6379` via Docker Compose; på Linux-CI utan default Redis kraschar
`Connect()` vid första request → 500 på alla auth-endpoints.

**Worker 1-fail:** Test-ordering-fragilitet. `RunAsync_EndToEnd_EnsuresNextDayAndDropsOld`
använder retention-cutoff = fixed-clock(2030-03-15) - 90d = ~2029-12-15. Om den körs
FÖRE `DropPartitionsOlderThan_DropsOldPartitionsSkipsDefaultAndRecent`, droppas alla
migration-bootstrap-partitions (2026-05-XX < 2029-12-15) → fragil `tomorrow`-assert
failer.

### Fix (commits 3b71fa5 → 8215658)

- **`ConnectionStrings__Redis` env-var i ApiFactory + StrictRateLimitApiFactory.InitializeAsync**
  FÖRE Services-access (samma pattern som ProductionStartupFactory hade redan)
- **`ConnectionStrings__Postgres`** för konsistens
- **Self-managed recent-partition i Worker-test** istället för bootstrap-litande
- **Rate-limit-test-merge** — slå ihop 2 separat tester som delade rate-limit-budget
  via samma factory-instans (1-minuts window återställs inte mellan tester)
- **`ProductionStartupSmokeTests`** — ny regression-skydd för Production-env-pipeline
  (UseEnvironment("Production") + populerad KnownNetworks via env-var)
- **`build.yml` `ASPNETCORE_ENVIRONMENT=Development`** som runner-level säkerhet

### Lärdomar (för Fas 1+)

- **`IWebHostBuilder.UseEnvironment()` är otillräckligt för minimal API + WebApplicationFactory**
  — `WebApplication.CreateBuilder()` läser ASPNETCORE_ENVIRONMENT INNAN ConfigureWebHost-callback
  körs. Verklig env-override sker via env-var i process FÖRE Services-access.
- **`IConnectionMultiplexer` kräver SEPARAT replace** utöver `IDistributedCache` —
  två olika DI-registreringar i Infrastructure DI.
- **Debug-middleware/console-logger snabbare path till root cause** än hypotes-jakt —
  IStartupFilter + AddSimpleConsole på Information-level exponerade exception-stack-trace
  i CI-stdout efter ~5 commits av blinda env-fix-försök.
- **Test-isolation viktig regression-skydd** — `ProductionStartupSmokeTests` säkerställer
  att framtida env-gated checks inte tyst breaker prod-pipelinen.

---
## TD-7: Zod runtime-validering för DTOs från backend — STÄNGD 2026-05-11
**Status:** STÄNGD 2026-05-11 — ADR 0020 + Zod-DTO-schemas levererade.
**Kategori:** Type safety / Architecture
**Severity:** Major (latent)
**Källa:** security-auditor 2026-05-07 Turn 2 (Major 1: `roles?` shape-skew)
**Fas:** 1

ADR 0020 (`docs/decisions/0020-frontend-dto-validation-with-zod.md`)
dokumenterar Anti-Corruption-Layer-mönstret vid HTTP-gränsen med Zod som
verktyg. Implementation:

- `lib/dto/_helpers.ts` — `parseResponse<T>` + `pagedResult<T>` + `pagedResultWithTotalPages<T>`
- `lib/dto/{me,applications,resumes,admin}.ts` — Zod-schemas per domän, typer härledda via `z.infer`
- 6 unchecked `as`-casts ersatta i `lib/auth/session.ts`, `lib/api/me.ts`, `lib/api/applications.ts`, `lib/api/resumes.ts`, `lib/api/admin.ts`
- `lib/types/*.ts` blir tunna re-exports (bakåtkompatibla för konsumenter)
- `lib/types/paged.ts` raderad — `isPagedResult` ersatt av Zod-pagineringsschema
- 51 nya unit-tests (helper + 4 domän-schemafiler), 18 test-filer / 205 tests grönt

**Faktisk CC-tid:** ~3h (discovery + ADR + helper + 4 schemas + refactor + tester).

**CTO-triage 2026-05-11:** senior-cto-advisor valde Variant A mot Variant B
(OpenAPI-codegen) och Variant C (hand-rullade guards). Motiveringar i ADR 0020
§ Avvisade alternativ.

**0 in-block-fix-defekter.** Lint grönt (0 errors, 2 pre-existing warnings utanför scope).

---
## TD-38: Trust Server Certificate=true persisteras i app/worker connection-strings ✓ STÄNGD Fas 1 Block A4 (2026-05-11)

**Status:** **STÄNGD KOMPLETT** 2026-05-11 via Fas 1 Block A4 (kod-fas + apply).

**Kod-fas (commit `ebb7550` + `7cde3c7`):**
- `ConnectionStringFactory.ForMigrate` (Trust=true, bootstrap-only) + `ForPersisted` (VerifyFull + Root Certificate)
- RDS global CA-bundle (`infra/certs/rds-global-bundle.pem`) COPY:ad till `/etc/ssl/certs/` i Api/Worker Dockerfiles
- 6 unit-tester i `JobbPilot.Migrate.UnitTests` verifierar anti-regression
- `deploy-dev.yml` uppdaterad att även bygga + registrera Migrate task-def
- `github_oidc/main.tf` uppdaterad med Migrate-ECR + Migrate-task-role i IAM-policy

**Apply-fas (2026-05-11):**
1. ✓ Bundle integritet verifierad mot AWS upstream (diff = 0)
2. ✓ terraform apply mot prod/baseline (IAM-policy update)
3. ✓ Tag `v0.1.2-dev` → deploy-dev.yml end-to-end PASS (api + worker + migrate images byggda)
4. ✓ Migrate-task re-runad (revision 5) → exit 0
5. ✓ Secrets Manager uppdaterad: `SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/rds-global-bundle.pem` — INGEN `Trust=true` kvar
6. ✓ Api + Worker force-new-deployment → båda 1/1 stable
7. ✓ Smoke-test `https://dev.jobbpilot.se/api/ready` → 200 + HSTS-header
8. ✓ Inga Npgsql TLS/handshake-errors i CloudWatch

**Reviews:** security-auditor APPROVED + Apply-fas-checklist (8 pkt), code-reviewer Changes Requested (B1 + Major fixade in-block), dotnet-architect APPROVED-with-fixes (Mindre 1+3 fixade in-block).

**TLS-postur post-stängning:** Api + Worker → RDS validerar både CA-signature och hostname-match. MITM-yta inom VPC eliminerad. GDPR Art. 32 defense-in-depth.

**Originalbeskrivning bevaras nedan för audit-trail:**

---

**Kategori:** Security / TLS
**Severity:** Minor (dev), eskaleras till Major innan staging/prod
**Källa:** security-auditor STEG 14b Sec-Minor-4 (2026-05-10)

`JobbPilot.Migrate/Program.cs:BuildConnectionString` använder
`SSL Mode=Require;Trust Server Certificate=true` för alla connection-strings.
Det är OK för Migrate själv (dev-RDS-cert är AWS-internal CA inte i container-
truststore), men samma helper bygger `appCs` och `hangfireCs` som skrivs
PERMANENT till Secrets Manager. Api + Worker får därmed `Trust=true` för all
framtid → MITM-yta inom VPC ("encrypted but not authenticated" TLS).

I dev-VPC med ECS-SG-only-ingress till RDS-SG är angripsytan nära noll. I
staging/prod blir det Sec-Major.

**Föreslagen åtgärd (Fas 1):**
1. Lägg in RDS-CA-bundle (eu-rds-ca-2019.pem eller global-bundle.pem) i
   container-truststores (Api/Worker Dockerfile via `update-ca-certificates`)
2. Splitta `BuildConnectionString` i två varianter:
   - Migrate själv: `Trust=true` OK (dev-fas)
   - Persisterade app/worker-CS: `Trust=false` med RDS-CA-validering
3. Re-run Migrate post-trust-flip → secrets uppdateras med strikt TLS

**Beroenden:** Inga 14b-blockare. Adresseras innan Fas 1-staging-rollout.

---
## TD-42: ~~Touch-target projektbrett under WCAG 2.5.5 (44×44 px)~~ — STÄNGD 2026-05-11

**Kategori:** Accessibility / WCAG 2.1 AAA
**Fas:** 1 a11y-pass-completion
**Prioritet:** Medium
**Källa:** design-review Fas 1 Block A2 2026-05-10 (Minor Mi1)
**Status:** **STÄNGD 2026-05-11 (Väg B a11y-pass).** Stationär-CC-session
levererade primitive-uppgradering (commit `f2b179a`) + in-block-fixar från
design-review (commit `1b0b9ec`). Backend oförändrad + Frontend 150/150 grönt.

**Levererat:**
- `input.tsx` h-8 → h-9 (32→36px). `file:h-6` → `file:h-7` för proportion.
- `button.tsx` default h-8 → h-9, lg h-9 → h-11 (kritiska CTAs nu WCAG 2.5.5
  AAA-höjd), icon size-8 → size-9, icon-lg size-9 → size-11. Dense-context
  varianter (xs/sm/icon-xs/icon-sm) bevarade.
- `select.tsx` data-[size=default]:h-8 → h-9. sm-variant bevarad.
- `me-profile-form.tsx` native language-select h-8 → h-9 (matchar Input).
- `add-follow-up-form.tsx` datetime-local h-8 → h-9.
- In-block-fixar från reviews (commit `1b0b9ec`):
  - `/ansokningar/ny` Avbryt-button size="sm" → default (matchar /cv/ny pattern)
  - `/ansokningar/page.tsx` "Ny ansökan"-CTA size="sm" → default
  - `/cv/page.tsx` "Nytt CV"-CTA size="sm" → default
- Checkboxes (`size-4`) bevarade — hit-area via row-pattern (items-start +
  gap-3 + cursor-pointer på label).

**Konvention dokumenterad:**
- `h-9` (36px) = default (skill-doc `jobbpilot-design-components`)
- `h-11` (44px) = critical CTAs (skill-doc `jobbpilot-design-a11y` §9)
- `h-7` (28px) = sm, dense-context
- WCAG 2.5.5 är AAA — JobbPilots civic-utility-densitet kompromissar mot
  h-9 default + h-11 critical CTAs (matchar Stripe Dashboard / GOV.UK).

**Reviews:**
- code-reviewer: APPROVE (1 Minor + 1 Nit — Minor lyft som TD-57)
- design-reviewer: APPROVE-WITH-FIXES (3 Major + 2 Minor — M1+M2 fixade
  in-block per 4h-regel, M3 lyft som TD-57)

**Follow-up:** TD-57 (native form-controls divergerar från Input-primitive).

---
## TD-43: Komponent-test-strategi för forms (Vitest + RTL + user-event) — **STÄNGD 2026-05-11**

**Kategori:** Testing / Quality Baseline
**Fas:** 1 (eget block efter A4) eller parallell session med A4
**Prioritet:** Medium-hög (kvalitets-baseline, inte feature-blocker)
**Källa:** Off-topic-fråga från Klas under Fas 1 Block A3 (2026-05-10)
**Stängd:** 2026-05-11 — komponent-test-baseline etablerad (15 nya tester över 3 forms)

### Stängningsnotis

Implementation parallell med A4 (TD-38). Levererat:

- `@testing-library/jest-dom@^6.9.1` adderat som devDep
- `src/test/setup.ts` uppdaterad med `jest-dom/vitest`-import
- 3 testfiler:
  - `src/components/forms/LoginForm.test.tsx` (4 tests)
  - `src/components/me/me-profile-form.test.tsx` (6 tests, inkl. path-routing)
  - `src/components/resumes/resume-content-form.test.tsx` (7 tests, inkl. array-path-routing)
- 90/90 Vitest PASS (75 → 90, +15)

Reviews:
- code-reviewer: Mergeklar, 0 Blockers, 0 Större. Rapport `docs/reviews/2026-05-10-td43-code-reviewer.md`
- design-reviewer: Approved, 0 Blockers, 2 Större (S1+S2) — adresserade innan commit. Rapport `docs/reviews/2026-05-10-td43-design-reviewer.md`

Follow-ups: TD-45 (LoginForm focus-flytt vid `state.error`), TD-46 (`pathToElementId` export för isolated unit-test).

JobbPilot:s test-pyramid har idag två lager: **Unit** (Vitest + Zod-schemas)
och **E2E** (Playwright happy-paths). Mellanlagret — komponent-tests för
React-forms — saknas helt. Forms är bland de mest kritiska user paths
(auth, profil, CV, ansökningar) och bär logik som varken Zod-schema-tester
eller E2E-tester täcker bra:

- **Form-state-flöden** (RHF + Server Action + felmappning) — Zod testar schema,
  E2E testar happy-path, men "vad händer när Server Action returnerar
  `{success: false, error: ...}` mid-submit?" är komponent-test-territorium
- **A11y-attribut-regression** — TD-15-läxan: bara design-reviewer fångade
  saknad focus-flytt. Ett komponent-test kan låsa fast att `aria-invalid`
  aktiveras vid fel + `document.activeElement === fält` post-submit
- **Refactor-säkerhet** — när någon byter `RHF` mot `react-hook-form/zodResolver`
  eller flyttar fält, fångar testet beteende-regression i sekunder

**Mastercard-CTO-perspektiv:** Stripe, Vercel, Linear, GOV.UK — alla har
komponent-tests som standard för alla forms med submit-logik.

**Föreslagen åtgärd:**

1. **Bibliotek:** `@testing-library/react`, `@testing-library/jest-dom`,
   `@testing-library/user-event`
2. **Test-coverage-mål per form:** rendering + happy submit + minst 1 felfall
   + a11y-attribut (aria-invalid + focus efter fail)
3. **Baseline-implementation:** börja med **3 highest-criticality forms**:
   - `LoginForm` (auth-yta)
   - `MeProfileForm` (TD-15-pattern att regression-låsa)
   - `ResumeContentForm` (TD-15-fix:ad — verifiera att aria-invalid + focus håller)
4. **Mall för alla framtida forms:** komponent-test obligatoriskt vid PR

**Beroenden:** Inga blockare. Kan köras parallellt med A4 (TD-38) eftersom
det är ren frontend-touch (ingen Migrate/Docker/Secrets Manager-koppling).

---
## TD-44: HSTS-header-anti-regression-test (Sec-Major-2 follow-up) — **STÄNGD 2026-05-11**

**Kategori:** Testing / Security
**Severity:** Minor
**Fas:** 1
**Källa:** dotnet-architect Mindre 4, Fas 1 Block A3 review (2026-05-10)
**Status:** STÄNGD 2026-05-11 — 3 nya `[Fact]`-tester utökar `UseHttpsRedirectionGateTests`
för att täcka `UseHsts()`-gaten med samma fixture-arv (Disabled-Production /
Enabled-Production / Development). Pattern: skicka `Host: dev.jobbpilot.se` per
HSTS-test-request → ASP.NET-default `HstsOptions.ExcludedHosts` (localhost-skydd)
bevaras intakt, vi simulerar verklig prod-DNS-trafik istället för att override
Microsoft-defense (dotnet-architect Major-fynd). 6/6 grön i HttpsRedirectionGate-
suiten. Minor 2 (HstsOptions.EnsureSafeForEnvironment-unit-test) lyft som TD-49.

---
## TD-45: LoginForm focus-flytt vid `state.error` (a11y-uppgradering) — **STÄNGD 2026-05-11**

**Kategori:** A11y / UX
**Severity:** Minor
**Fas:** 1
**Källa:** design-reviewer M1, TD-43-review (2026-05-11)
**Status:** STÄNGD 2026-05-11 — Variant A implementerad: focus flyttas till email-
fältet (inte till `<p role="alert">`) via `useRef<HTMLInputElement>` + `useEffect`
på `state?.error`. Pattern-skillnad mot TD-15: singelpunkt-fokus (inte path-baserad)
eftersom LoginForm:s error är medvetet generisk av säkerhetsskäl. Screen reader läser
`role="alert"` automatiskt — focus-flytt ger keyboard-användare visuell anchor +
direkt recovery-action. code-reviewer + design-reviewer APPROVED (0 Blocker / 0 Major).
Vitest 5/5 grön. design-reviewer noterade ärvt touch-target-problem på `Input` h-8
(32px) → spårat i TD-42 (inte regression i TD-45).

---
## TD-46: Exportera `pathToElementId` för isolated unit-test — **STÄNGD 2026-05-11**

**Kategori:** Testing / Architecture
**Severity:** Minor
**Fas:** 1
**Källa:** code-reviewer M3, TD-43-review (2026-05-11)
**Status:** STÄNGD 2026-05-11 — Discovery upptäckte att functions INTE var dubbletter
(olika dataformer: me-form switch-statement, resume-form regex-cascade). Klas valde
**Approach B (per-domän filer)** över ursprungliga Approach A (1 fil) efter
SOLID/SoC-analys. Skapat: `src/lib/forms/me-path-routing.ts` + `resume-path-routing.ts`
+ två parameteriserade test-filer (`it.each`). 35 nya unit-tests grön (12 me + 23 resume
inklusive negativ-fall som låser regex-kontraktet). 11/11 grön befintliga komponent-
tester (regression-check). code-reviewer + design-reviewer APPROVED. Bonus-fynd från
reviews: (1) `fieldA11y`-helper duplicerad mellan forms — kandidat för framtida TD,
(2) `src/lib/`-org-konvention (purpose-folder vs domain-folder) — kandidat för ADR.

---
## TD-47: ~~RDS CA-bundle-rotation-bevakning~~ — STÄNGD 2026-05-11

**Kategori:** Operations / Security
**Fas:** 1 (eller pre-staging) — operativ skuld, inte feature-blocker
**Prioritet:** Låg-medium
**Källa:** security-auditor S-Minor-1, Fas 1 Block A4 review (2026-05-11)
**Status:** **STÄNGD 2026-05-11 (Väg C Block B.2).** GitHub Actions workflow
`.github/workflows/rds-ca-bundle-check.yml` levererad. Månatlig hash-diff
(`sha256sum`) mot `https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem`.
Vid diff → öppnar GitHub-issue (labels: td-47, security) med rotation-procedur
(kopia av runbook §"RDS CA-bundle-rotation"). Idempotent — skippar om öppet
issue redan finns. Manuell trigger via workflow_dispatch tillgängligt.
Commit: `f9313af`.

`infra/certs/rds-global-bundle.pem` committat 2026-05-11 (TD-38). Bundle:n är
nuvarande G1 (täcker eu-north-1 fram till 2061/2121), men AWS kan introducera
`G2`/`rds-ca-2029-bundle` och rotera RDS-instans-certs till nyare CA *innan*
vår bundle uppdateras. Resultat: `SSL Mode=VerifyFull` failar → Api/Worker
tappar DB-anslutning hårt.

**Risk-fönster:** Lågt på kort sikt (AWS har inte annonserat G2), men ingen
strukturell bevakning finns. CA-rotationer historiskt: 2015 → 2019 → 2024.

**Föreslagen åtgärd:**

1. **Cron-job (GitHub Actions schedule):** kvartalsvis (eller månadsvis) job
   som hashar `infra/certs/rds-global-bundle.pem` lokalt och jämför mot
   `https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem`.
   Diff → öppna issue + Slack-notifiering (när Slack finns).
2. **Manuell rotation-procedur:** dokumentera i
   `docs/runbooks/td-38-tls-apply.md` så bundle-update + re-image + re-deploy
   går smärtfritt vid faktisk rotation.

**Beroenden:** GitHub Actions schedule-trigger (befintlig infrastruktur).
Inga blockare. Adresseras opportunistiskt eller pre-staging.

---
## TD-48: ~~Architecture-test för Trust Server Certificate=true-läckage~~ — STÄNGD 2026-05-11

**Kategori:** Testing / Security
**Severity:** Minor
**Fas:** 1 (pre-staging önskvärt)
**Källa:** dotnet-architect Mindre 2, Fas 1 Block A4 review (2026-05-11)
**Status:** **STÄNGD 2026-05-11 (Väg C Block B.1).** CTO-beslut: Alt A2
(Mono.Cecil IL string-table-scan) över A1 (reflection-on-fields, missar inline
strings) eller A3 (Roslyn, bryter assembly-baserad arch-test-konvention).
Nytt arch-test `tests/JobbPilot.Architecture.Tests/ConnectionStringLeakageTests.cs`
scannar alla Ldstr-instruktioner i Api/Worker/Infrastructure-assemblies via
Mono.Cecil. Migrate exkluderad (ForMigrate har Trust=true by design) — separat
sanity-test asserterar att Migrate faktiskt innehåller Trust=true så
exkluderingen kvarstår motiverad. Mono.Cecil 0.11.5 lagt till
Directory.Packages.props (test-only, transitiv via NetArchTest redan).
Commit: `9f33897`.

`ConnectionStringFactory` (TD-38) har unit-test som verifierar att
`ForPersisted` inte innehåller `Trust Server Certificate=true`. Det är
regression-skydd för *factory:n själv*, men inte för Api/Worker-assemblies
som helhet.

**Risk:** Om en framtida refactor lägger till en hardkodad CS med `Trust=true`
i `Infrastructure/Persistence/AppDbContext.cs` eller en `IOptions<>`-binder,
fångar inte vår unit-test det.

**Föreslagen åtgärd:** Architecture-test som scannar `JobbPilot.Api` +
`JobbPilot.Worker` + `JobbPilot.Infrastructure` assemblies efter
string-konstant `"Trust Server Certificate=true"`. Migrate exkluderas
(`ConnectionStringFactory.ForMigrate` har Trust=true by design).

**Implementation-detalj:** NetArchTest scannar typ-strukturer, inte
string-konstanter — behöver kompletteras med `Mono.Cecil` eller liknande
IL-introspektor för string-table-scan. Alternativt: enkel reflection-baserad
test som listar `internal const` + `static readonly string`-fält och letar
efter Trust-substrings.

**Pre-staging:** önskvärt att ha innan staging-promotion så TLS-postur är
mekaniskt låst.

---
## TD-49: ~~Unit-test för `HstsOptions.EnsureSafeForEnvironment` prod-defense~~ — STÄNGD 2026-05-11 (redan implementerad pre-TD-skapande)

**Kategori:** Testing / Security
**Severity:** Minor
**Fas:** 1 (opportunistiskt)
**Källa:** dotnet-architect Minor 2, Fas 1 Block A3 TD-44 review (2026-05-11)
**Status:** **STÄNGD 2026-05-11 (Väg E TDs-cleanup).** Stationär-CC-session discovery
fann att `tests/JobbPilot.Api.IntegrationTests/Configuration/HstsOptionsTests.cs`
redan existerar (143 rader, skapad vid STEG 13c HSTS-implementation 2026-05-10)
och täcker samtliga 6 TD-49-cases via xUnit Theory/Fact + Shouldly. Ingen
ny kod-touch behövs. **Discovery-fel vid TD-49-skapande:** dotnet-architect-review
kollade efter `JobbPilot.Api.UnitTests/`-projekt (existerar inte) men missade
att `JobbPilot.Api.IntegrationTests/Configuration/` redan har unit-style tester
(samma pattern som `ForwardedHeadersConfigTests` + `RateLimitingOptionsTests`).

**Befintlig täckning (HstsOptionsTests.cs):**

| TD-49-case | Befintlig test |
|---|---|
| 1. Production MaxAge<365 → throws | `FailsLoud_OnLowMaxAgeDays_OutsideDevTest` (Theory: Production/Staging/PROD/Demo, MaxAgeDays=30) |
| 2. Production MaxAge=365 → ok | `AcceptsSpecCompliantDefaults_InProduction` (defaults=365) |
| 3. Development MaxAge=0 → ok | `AllowsAnyConfig_InDevOrTest` (Theory: Development/development/Test/test, MaxAge=0+!IncludeSubDomains+Preload) |
| 4. Preload+MaxAge<365 → throws | Implicit (case 1-branchen kastar först oavsett Preload — defensiv duplikering i koden) |
| 5. Preload+!IncludeSubDomains → throws | `FailsLoud_OnPreloadWithoutIncludeSubDomains` |
| 6. Empty env-name → throws | `ThrowsArgumentException_OnEmptyEnvironmentName` (Theory: ""/" "/null) |

Plus två extra-bonus-cases (`Defaults_MatchHstsSpecRecommendation`,
`BindsFromConfiguration_ProductionOverlay`, `AcceptsValidPreloadConfig`).

**Lärdom:** TD-skapande ska verifiera test-existens via grep + Glob över ALLA
test-projekt, inte anta projekt-namn. Pattern (test-fil bredvid Configuration-klass
i IntegrationTests-projekt) är etablerat sedan STEG 12.

**Implementation-historik:** `HstsOptionsTests.cs` skapades 2026-05-10 vid
STEG 13c (HSTS-implementation, security-auditor Sec-Major-2 + dotnet-architect
Viktigt-fynd 5). Audit-trail i `docs/sessions/2026-05-10-*-steg13c-*.md`.

---
## TD-50: ~~Prod-konfig-källa för AdminBootstrap__InitialAdminEmail dokumenteras~~ — STÄNGD 2026-05-11
**Kategori:** Operations / Documentation
**Severity:** Sec-Minor (defense-in-depth)
**Källa:** security-auditor, 2026-05-11 (Fas 1-stängning admin-audit)
**Status:** **STÄNGD 2026-05-11 (Väg C Block C).** Ny `docs/runbooks/admin-bootstrap.md`
dokumenterar prod-konfig-flödet: AWS Secrets Manager + KMS + ECS task-def
env-var-mapping + IAM grants + rotation-procedur + lokal dev-bypass via
appsettings.Local.json. AdminBootstrapOptions.cs får utökad <remarks>-sektion
som förbjuder appsettings.json-källa i prod. Commit: `a9ca126`.

`IdempotentAdminRoleSeeder` läser email från `AdminBootstrapOptions` som
binds från config-sektion `AdminBootstrap`. I dev är default `""` (säkert).
I prod ska värdet komma från AWS Secrets Manager via ECS task-def env-var
men det är **inte dokumenterat** i runbook eller kod-kommentar.

**Risk:** Framtida Klas eller medarbetare sätter värdet i `appsettings.json`
direkt och commit:ar admin-email till git.

**Föreslagen åtgärd:**
1. Lägg kommentar i `src/JobbPilot.Infrastructure/Identity/AdminBootstrapOptions.cs`:
   `// Prod: läs ALDRIG via appsettings.json — alltid via AWS Secrets Manager + ECS task-def env-var.`
2. Skapa `docs/runbooks/admin-bootstrap.md` som dokumenterar prod-konfig-flödet.
3. Adressera vid nästa runbook-svep (när STEG 14 prod-deploy närmar sig).

**Scope:** ~30 min docs-arbete. TD-kandidat eftersom det är runbook-skrivande,
inte kod-fix.

---
## TD-53: ~~Frontend API-resultatformat — kind-union vs `T | null` standardisering~~ — ERSATT 2026-05-11 av TD-53a + TD-53b
**Status:** Ersatt 2026-05-11 — scope-split per CTO-triage (CLAUDE.md §9.6 kriterium 3).

Original TD-53 (scope >4h) split:
- **TD-53a** — Helper + ADR 0030 + 3 endpoints (`getMyProfile`, `getApplicationById`, `getResumeById`) + konsumenter
- **TD-53b** — 3 list-endpoints (`getPipeline`, `getApplications`, `getResumes`) + konsumenter

CTO-beslut 2026-05-11: Variant A (full kind-union) över Variant C (hybrid).
Motivering: CCP (Martin 2017 kap. 14), OCP (kap. 8), Anti-Corruption Layer
för outcome-semantik (Evans 2003 kap. 14), primitive-obsession på `null`
som komprimerar 4 betydelser. `getServerSession()` undantaget — `null` är
där legitim domän-semantik ("ingen session"), inte fel-komprimering.

---
## TD-53a: Frontend kind-union — Helper + ADR 0030 + detail/profile-endpoints — STÄNGD 2026-05-11
**Kategori:** Code consistency / Frontend / Architecture
**Status:** **STÄNGD 2026-05-11 (commit `7e90b36`).** ADR 0030 etablerad. 3 detail-endpoints + 3 konsumenter migrerade till `ApiResult<T>`. 11 nya tester. code-reviewer: 0 Blocker/Major, 2 Minor + 3 Nit fixade in-block. design-reviewer: 2 Blocker + 3 Major + 3 Minor fixade in-block + re-review Approved.
**Severity:** Minor (arkitektur-städ, ej bug-trycker)
**Fas:** 1
**Källa:** TD-53 split per senior-cto-advisor-triage 2026-05-11

Etablerar `responseToResult<T>`-helper i `lib/dto/_helpers.ts` som mappar
HTTP-status + DtoParseError till diskriminerat union:

```ts
type ApiResult<T> =
  | { kind: "ok"; data: T }
  | { kind: "unauthorized" }   // 401 — ingen session
  | { kind: "forbidden" }      // 403 — Admin krävs
  | { kind: "notFound" }       // 404 — endast detail-endpoints
  | { kind: "error" };         // network / shape-fel / 500
```

Refactor av 3 endpoints i samma batch:
- `getMyProfile()` — 3 lägen: ok / unauthorized / error
- `getApplicationById(id)` — 4 lägen: ok / unauthorized / notFound / error
- `getResumeById(id)` — 4 lägen: ok / unauthorized / notFound / error

Konsumenter uppdateras: `app/(app)/mig/page.tsx`,
`app/(app)/ansokningar/[id]/page.tsx`, `app/(app)/cv/[id]/page.tsx` —
`switch (result.kind)` med exhaustive UI-states.

ADR 0030 dokumenterar pattern, helper-API, `getServerSession`-undantag,
exhaustiveness-rationale via `never`-typ.

**Scope:** ~4h CC-tid (inom 4h-regeln). TD-53b separat batch.

**Trigger:** Direkt fortsättning på TD-53-split.

---
## TD-53b: ~~Frontend kind-union — list-endpoints~~ — STÄNGD 2026-05-11
**Kategori:** Code consistency / Frontend
**Severity:** Minor
**Fas:** 1
**Källa:** TD-53 split per senior-cto-advisor-triage 2026-05-11
**Status:** **STÄNGD 2026-05-11 (commit `aac9b2f`).** ADR 0030-migration
färdig över hela frontend-API-ytan (7 endpoints, 5+ konsumenter).

Levererat:
- 4 endpoints refactorade till `Promise<ApiResult<T>>` med explicit return-type:
  `getPipeline`, `getApplications`, `getResumes`, `getAuditLog`
- Lokala `AuditLogResponse`-typen raderad från `admin.ts` (ad-hoc-union
  ersatt med generisk `ApiResult`)
- 3 konsumenter med exhaustive switch + `assertNever`:
  `ansokningar/page.tsx`, `cv/page.tsx`, `admin/granskning/page.tsx`
- `responseToResult` används unconditionally (grep `parseResponse` i
  `lib/api/` = 0 träffar)

CTO-beslut (senior-cto-advisor 2026-05-11):
- Variant A för `getApplications` (refactora ändå trots inga konsumenter —
  ADR 0030-trohet + CCP per Martin 2017 kap. 13)
- Variant Y för test-scope (helper redan testad, DRY i test-kod, tsc +
  assertNever täcker exhaustiveness statiskt — Fowler 2012, Cohn 2009)

Reviews:
- code-reviewer: 0 Blocker/Major, 4 Minor (informativa), 1 Nit
- design-reviewer: 1 Major (`role="alert"`-borttagning i admin ErrorBlock,
  konsekvens med TD-53a-policy) + 1 Minor (kommentar om dead notFound-case)
  fixade in-block

Tester: 217/217 oförändrat (Variant Y). tsc --noEmit grönt.

---
## TD-54: ~~`text-text-tertiary` på empty-state sekundärtext bryter WCAG AA~~ — STÄNGD 2026-05-11
**Kategori:** Accessibility (WCAG 2.1 AA 1.4.3 Contrast)
**Severity:** Minor (replikerat pattern)
**Källa:** design-reviewer, 2026-05-11 (Fas 1-stängning admin-audit)
**Status:** **STÄNGD 2026-05-11 (Väg B a11y-pass).** Stationär-CC-session
levererade kontextuell mapping (commit `8cfbde4`) + in-block-fix från
design-review (commit `52f3b45`). Frontend 150/150 grönt.

Discovery hittade 16 träffar i 9 filer. Kontextuell mapping applicerad:

**Funktionell text (10 träffar) → `text-text-secondary` (7.2:1, AA):**
- `components/resumes/resume-card.tsx` (timestamp)
- `components/applications/application-card.tsx` (timestamp)
- `app/(app)/ansokningar/page.tsx` (empty-state)
- `app/(app)/ansokningar/[id]/page.tsx` (id-fragment + note-datum)
- `app/(app)/cv/[id]/page.tsx` (resume-name)
- `app/(app)/cv/page.tsx` (empty-state)
- `app/(admin)/admin/granskning/audit-log-pagination.tsx` (disabled-link-text)
- `app/(admin)/admin/granskning/audit-log-table.tsx` (sekundärtext + actor)

**Dekorativa separatorer (5 träffar) → KVAR `text-text-tertiary`:**
- Breadcrumb `/` (× 2)
- Aggregate-separator ` · `
- Em-dash placeholders `—` (× 2)

Per a11y-skill §4: "decorative/non-essential text" är undantaget från
4.5:1-kravet. Per components-skill: Breadcrumb-separator är dokumenterat
för `text-text-tertiary`.

**In-block-fix (commit `52f3b45`):**
- `cursor-not-allowed` på pagination disabled-spans (design-reviewer N1)
  för att stärka sighted-user-affordance utan att bryta civic-utility-ton.

**Reviews:**
- code-reviewer: APPROVE (0 blocker, 0 major, 0 minor, 0 nit)
- design-reviewer: APPROVE (0 blocker, 0 major, 4 minor, 2 nit) — N1
  fixad in-block, övriga acceptabla observations

---
## TD-55: ~~Hardening-pass för PagedResult + ApplicationsQuery paged-shape~~ — STÄNGD 2026-05-11
**Kategori:** Architecture / Consistency
**Severity:** Minor (housekeeping) → uppgraderad till runtime-bug under impl
**Källa:** dotnet-architect, 2026-05-11 (Fas 1-stängning admin-audit)
**Status:** **STÄNGD 2026-05-11 (Väg C Block A).** Stationär-CC-session levererade
backend-retro-fit (commit `c2f539e`) + frontend-konsumtion (`0b0886d`) + in-block-
fixar från reviews (`5784120`). Backend 594/594 + Frontend 150/150 grönt.

Discovery avslöjade att problemet inte var housekeeping utan en faktisk
runtime typ-skew: frontend `GetApplicationsResult` förväntade
`{items,totalCount,page,pageSize}` men backend returnerade bare array.
TypeScript-cast utan runtime-validering dolde buggen.

**Levererat:**
- `GetApplicationsQuery` + `GetResumesQuery` returnerar `PagedResult<T>` med
  separat count-query (CLAUDE.md §3.6)
- `ListJobAdsQuery` defererad till Fas 2 (TD-56) — fick `.Take(500)` hard cap
  som defense-in-depth mot DoS-vektor under tiden
- Architecture-test `PagedResultContractTests` låser kontraktet (queries med
  `Page/PageNumber + PageSize`-semantik MÅSTE returnera `PagedResult<T>`)
- Frontend generisk `isPagedResult<T>` i `lib/types/paged.ts` förebygger
  framtida per-endpoint duplikerings-mönster
- Integration-tester uppdaterade till PagedResult-shape (JsonValueKind.Object
  + items/totalCount/page/pageSize-properties)
- Reviews: code-reviewer APPROVE + dotnet-architect APPROVE-WITH-FIXES,
  alla 3 Minor in-block-fixade

---
## TD-60: ADR för auth-pipeline-ordning + `IClaimsTransformation`-disciplin
**Status:** STÄNGD 2026-05-11 — ADR 0029 levererad.
**Kategori:** Documentation / Architecture
**Severity:** Minor
**Fas:** 1.5 polish / framtida auth-changes
**Källa:** dotnet-architect-review 2026-05-11 Block C Minor (c+d), H-3 SoC-split

Block C införde `SessionRoleClaimsTransformation` och `ClaimsTransformationAllowlistTests`
låser konsument-listan strukturellt. Men auth-pipeline-ordning
(Authentication → IClaimsTransformation → Authorization) är ASP.NET-implicit
— inte dokumenterad single source of truth.

Future-Klas eller annan agent som rör auth-stacken har ingen ADR att läsa.

**Levererad åtgärd (Väg A docs-pass 2026-05-11):**
ADR 0029 (`docs/decisions/0029-auth-pipeline-and-claims-transformation.md`)
dokumenterar fyra beslut: (1) HTTP-pipeline-ordning explicit, (2) claim-placerings-regel
auth-handler vs transformation, (3) per-request-fetch-disciplin utan cache i Fas 1,
(4) konsument-allowlist-mönstret via `ClaimsTransformationAllowlistTests`. Komplementär
till ADR 0028 (supersedas inte). Plus 5 nya integration-tester i
`SessionRoleClaimsTransformationTests` som verifierar transformation-beteendet
end-to-end (607 → 612).

**Faktisk CC-tid:** ~2.5h (utöver original 45 min docs-scope — agent-review-driven
in-block-fix av M-1 prefix-längd, Min-1 path-fotnot, M-2 saknade tester via
Alt B-integration-test-strategi per senior-cto-advisor-triage).

**Reviews:** code-reviewer (2 Major + 1 Minor — alla fixade in-block per 4h-regeln),
dotnet-architect (0 Blocker / 0 Major / 3 Minor / 1 Nit — 3 Minor avvisade som TD per
CTO-rek: NetArchTest-stil cosmetic, sentinel-pattern-ADR YAGNI, pipeline-ordnings-arch-test
mitigerat av integration-test). 0 nya TDs lyfta.

---
## TD-61: Audit-trail-evidence-test för `IdempotentAdminRoleSeeder` — STÄNGD 2026-05-11

**Status:** STÄNGD 2026-05-11 (Väg B) — original-premiss var felgrundad,
korrigerad + verifierad mot rätt observability-spår.

**Kategori:** Testing / Observability
**Severity:** Minor
**Fas:** 1.5 polish (stängd) — vidare audit-port-arkitektur defereras till Fas 6
**Källa:** security-auditor 2026-05-11 Block B Minor 2

Seederns XML-doc (rad 19-22) hävdade: "operationer går via samma
Identity-pipeline som /auth/register, vilket gör seeding observerbar via samma
audit-log som admin-vyn själv granskar". Inget verifierade att audit-händelse
faktiskt skapas vid bootstrap-tilldelning.

**Discovery 2026-05-11 (Väg B):** Premissen är *provably false*.
`AuditBehavior` (`src/JobbPilot.Application/Common/Auditing/AuditBehavior.cs`) är
en Mediator-pipeline-behavior som ENDAST auditerar commands markerade med
`IAuditableCommand<TResponse>`. Seedern anropar `UserManager.AddToRoleAsync`
direkt utanför Mediator-pipelinen. `RegisterCommand` implementerar inte
`IAuditableCommand` och `IAuthAuditLogger` skriver bara strukturerad logg —
ADR 0022 §Kontext rad 11 bekräftar explicit: "skriver bara strukturerad logg,
inte till databas". Admin-vyns `GetAuditLogEntriesQueryHandler` läser
`AuditLogEntries`-tabellen, dit varken seedern eller `/auth/register` skriver.

**senior-cto-advisor-triage 2026-05-11 Väg B:** multi-approach
(A) korrigera XML-doc + test mot rätt sink (ILogger) /
(B) lägg till `AuditLogEntry`-skrivning i seedern utanför Mediator-pipelinen /
(C) defer till Fas 6.

**CTO-beslut: Alt A.** Motivering mot Robert C. Martin 2017 (SRP), Martin 2008
(Clean Code kap. 4 — comments that lie are defects), Fowler 2018 (Refactoring
kap. 3 — code smells), Ford/Parsons/Kua 2017 (Building Evolutionary
Architectures kap. 2 — fitness functions skyddar arkitekt-portar mot
smyg-erosion), Cohn 2009 (Test Pyramid), Twelve-Factor §XI (Logs as event
streams), ADR 0022 immutable-policy.

Alt B avvisad: bryter SRP (seeder får två change-reasons), introducerar ny
audit-skrivnings-port utanför ADR 0022:s etablerade Mediator-pipeline →
kräver dedikerad ADR, inte TD-stängning. "Smyg-in arkitekturbeslut via
TD-fix" är anti-pattern (Ford/Parsons/Kua). Alt C avvisad: CLAUDE.md §9.6
anti-pattern "spara TD så scope inte växer" — evidence-kravet kan uppfyllas
nu inom 1h mot rätt sink (ILogger).

**Levererad åtgärd:**

1. **XML-doc korrigerad** (`src/JobbPilot.Infrastructure/Identity/IdempotentAdminRoleSeeder.cs`
   rad 17-31): ärlig formulering — observability via `LogAdminAssigned`
   EventId=2 → ILogger → Serilog → Seq (dev) / CloudWatch Logs (prod).
   Explicit anti-claim att seedern INTE populerar `audit_log`-tabellen +
   hänvisning till ADR 0022 + Fas 6 admin-impersonation-ADR-kandidatur för
   dedikerad bootstrap-audit-port.
2. **Integration-test levererat** (`tests/JobbPilot.Application.UnitTests/IdentityBootstrap/IdempotentAdminRoleSeederAuditEvidenceTests.cs`):
   3 testfall med CapturingLogger + InMemory Identity-store (AddIdentityCore-
   pattern matchar Worker-DI). Verifierar:
   - Happy path: matchande user → EventId=2 (LogAdminAssigned) emit:as exakt 1×
   - Idempotens: user redan Admin → INGEN EventId=2, men EventId=3
     (LogAdminAlreadyAssigned) emit:as som no-op-bevis
   - Saknad user: ingen matchande user → INGEN EventId=2, men EventId=4
     (LogAdminUserNotFound) emit:as som warning-bevis

**Tester:** Application.UnitTests 201 → 204 (+3). Full svit 612 → 615.

**Faktisk CC-tid:** ~2h (discovery + CTO-triage + implementation). Inom
4h-regeln per CLAUDE.md §9.6.

**0 nya TDs lyfta.** Bootstrap-audit-port-frågan (om DB-persistent audit
för Identity-side-effects någonsin ska finnas) hör till Fas 6 admin-
impersonation-ADR-arbete — inte en defekt i nuvarande system så länge
XML-doc:en är ärlig om vad evidence-spåret faktiskt är.

---

## TD-30: Domänköp + Route53 + ACM-cert ✓ STÄNGD STEG 13c (2026-05-10), retroaktivt arkiverad 2026-05-11
**Kategori:** Infra / Security
**Severity:** Major (tidsbundet — hard deadline 2026-06-08)
**Källa:** security-auditor STEG 13b Sec-Major-1 + ADR 0026
**Status:** **STÄNGD 2026-05-10** via STEG 13c HTTPS-flip. ADR 0027 (`docs/decisions/0027-https-aktiverat-supersession.md`) superseder ADR 0026. Retroaktivt arkiverad 2026-05-11 (Klas-discovery: TD-30 stod kvar som öppen Major Nu trots att kod + infra + ADR var levererat).

ADR 0026 accepterade ALB HTTP-only under Fas 0 med tidsfönster 30 dagar
(deadline 2026-06-08) och 5 triggers för supersession. Trigger 1
(domän + ACM-cert) blev levererad innan deadline.

**Stängningsbevis (verifierat 2026-05-11):**
- `infra/terraform/environments/dev/terraform.tfvars` rad 15: `alb_https_enabled = true`
- `infra/terraform/environments/dev/terraform.tfvars` rad 16: `alb_acm_certificate_arn = "arn:aws:acm:eu-north-1:710427215829:certificate/f72a79d7-f964-49c7-abb5-cf81b8639d6a"`
- ACM-cert validerat 2026-05-10 via Route53 DNS-validation
- 16 terraform-filer refererar `jobbpilot.se` / `route53` / `acm` (modules + envs)
- ADR 0027 dokumenterad supersession av ADR 0026

**Original-spec (operativa steg):**

1. Registrera `jobbpilot.se` ✓ (Klas, hos svensk registrar)
2. Skapa Route53 hosted zone i AWS ✓ (modules/route53)
3. Begär ACM-cert via DNS-validering ✓ (modules/acm, cert f72a79d7-...)
4. Skapa A-ALIAS-record `dev.jobbpilot.se → ALB-DNS` ✓
5. Sätt `alb_https_enabled = true` + `alb_acm_certificate_arn` i tfvars ✓
6. `terraform apply` — ALB konverterar HTTP → HTTPS-redirect ✓
7. Skriv supersession-ADR ✓ (ADR 0027)
8. Update `current-work.md` + `steg-tracker.md` ✓ (STEG 13c-session-log)

**Lärdom:** TD-livscykel-disciplinen (CLAUDE.md §9.7) etablerades 2026-05-11
efter denna miss. Framtida STEG-stängningar som löser en TD ska flytta
TD-blocket till arkivet i samma commit som leveransen — annars hopar sig
"de facto stängda" TDs i aktiv-listan och bryter översiktstabellens
sanningshalt.

---

## TD-10: PII-läckage via `body?.detail` i Server Actions ✓ STÄNGD 2026-05-11
**Kategori:** Säkerhet (GDPR Art. 5(1)(f))
**Severity:** Major
**Fas:** 1
**Källa:** Security audit 2026-05-08 (Major 1)
**Status:** **STÄNGD 2026-05-11 (commit `0560718`)** via Batch A — säkerhet-hard frontend. Variant B (central helper) implementerad per CTO-beslut 2026-05-11.

Server Actions i `src/lib/actions/applications.ts`, `me.ts`, `resumes.ts`
exponerade `body?.detail` (samt `body?.title` i `me.ts` + `resumes.ts`) direkt
till UI-lagret över 10 error-sites. ASP.NET ProblemDetails kunde innehålla
stacktraces, SQL-felmeddelanden eller annan intern info → bröt GDPR Art. 5(1)(f)
integritet och konfidentialitet.

**Leverans (Batch A):**

- **NY:** `src/lib/actions/_action-error.ts` — `mapActionError(res: Response, fallback: string): string` sync helper, status→svensk text, läser ALDRIG body
- **NY:** `src/lib/actions/_action-error.test.ts` — 10 vitest cases inkl. säkerhetsinvariant `expect(res.json).not.toHaveBeenCalled()`
- **ÄNDRAD:** `applications.ts` (4 sites), `me.ts` (1 site), `resumes.ts` (5 sites) — alla `body?.detail`/`body?.title`-mönster borttagna
- **Säkerhetsinvariant:** body läses ALDRIG på error-path i action-layern
- **Whitelist:** 401/403/404/409/422/429 → fasta svenska strängar. Övriga statuskoder → per-action svensk fallback.

**CTO-beslut (senior-cto-advisor 2026-05-11):** Variant B över A/C.
Motivering: DRY (Hunt/Thomas 1999), SoC (Dijkstra 1974), OCP (Martin 2017
kap. 8), säkerhets-granskbarhet på ett ställe (OWASP ASVS V8.2), ADR 0030-
symmetri. Body kastas helt — "secure by default" invariant.

**Reviews:**
- code-reviewer: 0 Blocker / 0 Major / 2 Minor / 3 Nit. Minor-1 (substring-bypass i `assertSafeBaseURL`) + Nit-1 (DRY för 409+422) + Nit-2 (doc-precision) fixade in-block.
- security-auditor: Approved. GDPR-veto passerad utan blocker. TD-10 stängningskriterier uppfyllda — invariant verifierad i kod + test, inga kvarvarande call-sites.

**Tester:** Vitest 217 → 227 (+10 nya). tsc --noEmit grön.

**Defererade TDs lyfta i samma session:**
- TD-63: ActionResult kind-union för writes (ADR 0030-symmetri, Variant C-defererad)
- TD-64: i18n-migration av inline svenska error-strängar (omnibus)

---

## TD-11: Hårdkodad E2E-lösenord och testemail på produktionsdomän ✓ STÄNGD 2026-05-11
**Kategori:** Säkerhet (test-isolation)
**Severity:** Major
**Fas:** 1
**Källa:** Security audit 2026-05-08 (Major 3)
**Status:** **STÄNGD 2026-05-11 (commit `0560718`)** via Batch A — säkerhet-hard frontend.

`tests/e2e/helpers/auth.ts` innehöll hårdkodat lösenord (`E2eTestPass123!`)
och genererade testmail på `@jobbpilot.se` (produktionsdomän). E2E-testkonton
kunde hamna i produktionsdatabasen om CI/dev råkade peka mot prod.

**Leverans (Batch A):**

1. **Lösenord:** `TEST_USER_PASSWORD` env-var med dev-fallback `E2eTestPass123!Dev`
2. **Test-domän:** `@jobbpilot.se` → `@e2e.jobbpilot.test` (RFC 6761 reserverad TLD, garanterad non-resolvable)
3. **`assertSafeBaseURL`-guard:** URL-hostname-parse (inte substring-match) med whitelist `localhost / 127.0.0.1 / *.staging.jobbpilot.se / *.dev.jobbpilot.se`. Anropas i både `loginAs` (efter `page.goto`) och `ensureTestUser` (på `baseURL`-argument).

**Reviews:**
- security-auditor: Approved för Fas 1. `.test`-TLD är RFC-grundat val, hostname-parse eliminerar substring-bypass (`localhost.evil.com` etc.). Dev-fallback acceptabel — `assertSafeBaseURL` förhindrar att fallback når prod.
- code-reviewer: Minor-1 (URL-parse-omskrivning) fixad in-block samma commit.

**Restanmärkning (Nit, ej blocker):** `ensureTestUser` läser fortfarande
`body?.title` på 400 för Duplicate-detection. Test-helper-kod, ingen GDPR-
implikation (test-konton är e2e-genererade). OK att lämna.

---

## TD-41: Select-komponent-konvention — native vs shadcn Radix ✓ STÄNGD 2026-05-11
**Kategori:** UI / Component-konvention
**Severity:** Major
**Fas:** 1 (beslutas innan A3)
**Källa:** design-review Fas 1 Block A2 2026-05-10 (Major M1+M2)
**Status:** **STÄNGD 2026-05-11 (Batch B)** — shadcn-first-konvention etablerad.

`MeProfileForm` använde native `<select>` med Tailwind-styling kopierad
inline från `Input.tsx` (~110 tecken). Samtidigt fanns en fullskalig
shadcn/Radix-baserad `Select` redan installerad i `components/ui/select.tsx`
(193 rader). Inkonsekvens mellan formulär: `add-follow-up-form.tsx` använde
redan shadcn Select för `channel`-fältet, men `me-profile-form.tsx` använde
native för `language`.

**Leverans (Batch B):**

1. **`me-profile-form.tsx:111-141`** — native `<select name="language">` ersatt med shadcn `Select` wrapped i RHF `Controller`. `SelectTrigger` har `id="me-language"` (bevarad path-routing-target), `ref={field.ref}` (RHF-fokus-management), `className="w-full"` (matchar Input-bredd), `{...fieldA11y("language")}` (aria-invalid/describedby bevarade). `SelectValue` utan placeholder eftersom Controller alltid har värdet `"sv"` eller `"en"` (defaultValues + z.enum).
2. **Test-justering** — `me-profile-form.test.tsx` test 1 anpassad för Radix trigger-rendering (`.toHaveTextContent("Svenska")` istället för `.toHaveValue("sv")`). Test 5 ("TD-15 path-routing: language") borttaget — UI-vägen att trigga `path="language"`-fel är arkitektoniskt omöjlig med shadcn Select (endast giltiga `z.enum`-items kan väljas). `pathToElementId("language") → "me-language"` täcks redan av `lib/forms/me-path-routing.test.ts`.

**CTO-beslut (senior-cto-advisor 2026-05-11):** Unified beslut för TD-41 + TD-57 — "shadcn-first med Input-primitive som default". Variant (b) över (a) för TD-41.
Motivering: DRY (Hunt/Thomas 1999), SRP (Martin 2017 kap. 7), Component Cohesion CCP/REP (Martin 2017 kap. 13), Konsekvens (NN/g Heuristic #4), A11y WCAG 4.1.2 + 2.1.1. Variant (a) "native-select-primitiv" avvisad — skapar parallell komponent-hierarki och bryter "one obvious way".

**Framtida konvention (etablerad):**
- Text/number/datetime-local/email/etc. → `Input`-primitive
- Single-select dropdown → shadcn `Select` med `Controller`
- Multi-select/combobox/autocomplete → shadcn-pendang (vid behov)
- Native `<select>`/`<input>` med inline-styling som duplicerar Input-primitive → **anti-pattern**

**Reviews:**
- code-reviewer: 0 Blocker / 0 Major / 0 Minor / 1 Nit (FYI om SelectTrigger-id-coupling). Approved.
- design-reviewer: 1 Major (SelectTrigger för `channel` saknade `w-full` — pre-existing i `add-follow-up-form.tsx`) + 1 Minor (`disabled={isPending}` saknades) fixade in-block i samma batch per §9.6.

**Tester:** Vitest 227 → 226 (−1 test 5; +0 nya). tsc --noEmit grön.

---

## TD-57: Native form-controls divergerar från Input-primitive ✓ STÄNGD 2026-05-11
**Kategori:** Architecture / Consistency
**Severity:** Minor (cosmetic + a11y-attribute-gap)
**Fas:** 1 a11y-pass-completion
**Källa:** design-reviewer + code-reviewer Fas 1.5 a11y-pass 2026-05-11 (TD-42 M3 / Minor 1)
**Status:** **STÄNGD 2026-05-11 (Batch B)** — Input-primitive-konvention etablerad för native input-types.

Native `<input type="datetime-local">` i `add-follow-up-form.tsx:54-61`
hade EGEN inline-styling (`rounded-md`, `py-2`, `text-sm`) som divergerade
från `Input.tsx`-primitive (`rounded-sm`, `py-1`, `text-base md:text-sm`).
Saknade också `aria-invalid:`-styling, `dark:`-styling och `disabled:bg-input/50`.

`audit-log-filter.tsx:30-45` använde redan `<Input type="datetime-local" />`
korrekt — den var den etablerade konventionen som inte följdes i
`add-follow-up-form.tsx`.

**Leverans (Batch B):**

1. **`add-follow-up-form.tsx:55-61`** — native `<input type="datetime-local">` med inline-styling (90 tecken) ersatt med `<Input type="datetime-local" id="follow-up-date" name="scheduledAt" required disabled={isPending} />`. FormData-kontraktet (`scheduledAt`-name) bevarat.
2. **DRY-vinst:** 90 tecken inline-Tailwind borttagna — Input-primitive bär stilen.

**CTO-beslut (senior-cto-advisor 2026-05-11):** Variant C ("ersätt native med Input-primitive") över A (`inputBaseClasses`-helper) / B (NativeInput-wrapper).
Motivering: DRY-by-component-encapsulation > DRY-by-string-sharing (Fowler 2018, *Refactoring* kap. 6). Variant A avvisad — string-share-pattern är svagare än component-encapsulation. Variant B avvisad — wrapper-runt-native-imiterar-Input är "Middle Man"-anti-pattern (Fowler 2018) eftersom Input-primitive redan ÄR wrappern.

**Reviews:** Adresserade samtidigt med TD-41 (CTO-beslut unified). Se TD-41 review-block ovan.

**Tester:** ingår i Batch B-svit (Vitest 226/226 + tsc grön).

---

## TD-1: Skip-link saknas i (app)-layout ✓ STÄNGD 2026-05-11
**Kategori:** Accessibility (WCAG 2.4.1 Bypass Blocks)
**Severity:** Minor
**Fas:** 1 a11y-pass-completion
**Källa:** design-reviewer, 2026-05-07 (Turn 2)
**Status:** **STÄNGD 2026-05-11 (Batch C)** — skip-link implementerad.

`src/app/(app)/layout.tsx` saknade "Skip to main content"-länk.
Tangentbordsanvändare tvingades tabba igenom hela headern på varje sida.

**Leverans (Batch C):**

1. **Skip-link tillagd som första element** i `<div>`-container:
   ```tsx
   <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring">
     Hoppa till huvudinnehåll
   </a>
   ```
2. **`<main>` taggad** med `id="main" tabIndex={-1} className="... focus:outline-none"`. `tabIndex={-1}` gör targeten programmatiskt fokuserbar (för anchor-navigation) utan att förorena tab-ordningen. `focus:outline-none` undertrycker visuell ring på main-elementet — skip-länkens egen fokusring räcker som visuell signal.

**Standardmönster:** följer GOV.UK Design System skip-link-recipe.

**Reviews:**
- code-reviewer: Approved (0 Blocker / 0 Major / 1 FYI om JSDoc-dokumentation).
- design-reviewer: Approved. WCAG 2.4.1 + 2.4.7 uppfyllda. Kontrast >12:1 (AAA-nivå). Token-disciplin verifierad mot `globals.css`.

**Tester:** ingår i Batch C-svit (Vitest 226/226 + tsc grön).

---

## TD-2: CardTitle renderas utan heading-tag ✓ STÄNGD 2026-05-11
**Kategori:** Accessibility (heading-hierarki)
**Severity:** Minor
**Fas:** 1 a11y-pass-completion
**Källa:** design-reviewer, 2026-05-07 (Turn 2)
**Status:** **STÄNGD 2026-05-11 (Batch C)** — CardTitle default `<h3>` + `asChild` via Slot.Root.

Shadcn `CardTitle` renderade default som `<div>`. Heading-trädet på /mig
blev därmed `<h1>` ("Min profil") följt av `<div>` ("Kontoinformation") —
bröt h1→h2-hierarki som skärmläsare förlitar sig på (WCAG 1.3.1).

**Leverans (Batch C):**

1. **`ui/card.tsx` CardTitle** ändrad från `<div>` till `<h3>` default + `asChild` prop via `Slot.Root` från `radix-ui` (samma mönster som `button.tsx`):
   ```tsx
   function CardTitle({ className, asChild = false, ...props }: ComponentProps<"h3"> & { asChild?: boolean }) {
     const Comp = asChild ? Slot.Root : "h3"
     return <Comp data-slot="card-title" className={cn(...)} {...props} />
   }
   ```
2. **`mig/page.tsx` consumers** uppdaterade — två CardTitles direkt under h1 lyfta till h2 via `<CardTitle asChild><h2>...</h2></CardTitle>`. Slot.Root mergear klass+props ner i child-elementet.
3. **`(marketing)/page.tsx` consumer** lämnad oförändrad — Card ligger under h2-section, default `<h3>` är korrekt nesting (h1→h2→h3).

**Heading-hierarki efter Batch C:**
- /mig: h1 ("Min profil") → h2 ("Kontoinformation") + h2 ("Profil") ✓
- /(marketing): h1 ("JobbPilot") → h2 ("Designsystem") → h3 ("Civic-utility i praktiken") ✓

**Reviews:**
- code-reviewer: Approved. Pattern-fidelity mot `button.tsx` exakt. TS-strikthet ökad (h3 vs div).
- design-reviewer: Approved. Ingen visuell ändring (semantisk lyftning). 1177/Digg/GOV.UK-disciplin på heading-hierarki.

**Tester:** ingår i Batch C-svit (Vitest 226/226 + tsc grön).

---

## TD-40: Path-equality i `fieldA11y` — saknar regression-bevakning ✓ STÄNGD 2026-05-11 (retroaktivt)
**Kategori:** Accessibility / Robustness
**Severity:** Minor
**Fas:** 1 a11y-pass-completion (samma som TD-1, TD-2)
**Källa:** design-review Fas 1 Block A1 2026-05-10 (Minor m1)
**Status:** **STÄNGD 2026-05-11 (Batch C) — retroaktivt.** Regression-test redan implementerat före TD-allokering.

`ResumeContentForm.fieldA11y` använder strikt `serverError?.path === path`-jämförelse.
Schemat i `resume-schemas.ts` lägger idag alla `.refine()`-fel på barn-path
(t.ex. `experiences.0.endDate`), så strikt match fungerar för dagens output.

**Risk om refine framtida pekar på array-rot eller tomt path:** felet hamnar
på toppnivå-`<p>` utan `aria-invalid`-flaggat fält. Skärmläsare hör då
felmeddelandet via `role="alert"` men kan inte navigera till specifik fält.

**Discovery-fynd (Batch C):** `src/lib/actions/resume-schemas.test.ts:275-364`
innehåller redan en testsvit "resumeContentSchema – refine() leaf-path
regression (TD-40)" med 3 tester:

1. `experiences refine pekar på leaf-path 'experiences.0.endDate' → pathToElementId mappar non-null`
2. `educations refine pekar på leaf-path 'educations.0.endDate' → pathToElementId mappar non-null`
3. `refine path bevarar array-index → pathToElementId mappar rätt fält för icke-0-index`

**Designkvalitet:** Path-baserad assertion (`path.join(".") === "experiences.0.endDate"`)
istället för message-string. Skyddar invarianten utan att rödna vid framtida
copy-tweaks. Cross-validation mot `pathToElementId()` låser kontraktet
schemas ↔ path-routing ↔ DOM-id. Test-författare-kommentar på rad 290-291
dokumenterar valet.

**Lärdom (analog med TD-30):** TD-allokering måste alltid prefacas med
discovery — implementationen kan redan ha landat utan att TD-listan
uppdaterats. Per CLAUDE.md §9.7 — stäng retroaktivt så aktiv-listan inte
ljuger om verkligheten.

**Reviews:** code-reviewer + design-reviewer Approved Batch C. Inga ändringar krävdes — bevakning bedömd tillräcklig.

---

## TD-3: Tom-state-copy "Inga roller tilldelade" saknar next-action ✓ STÄNGD 2026-05-11
**Kategori:** UX
**Severity:** Minor
**Fas:** 1 UX-pass /mig
**Källa:** design-reviewer, 2026-05-07 (Turn 2)
**Status:** **STÄNGD 2026-05-11 (Batch D)** — Variant (a) stum tom-state.

På /mig visades "Inga roller tilldelade" om `user.roles` var tom array.
Användaren fick ingen vägledning om vad det betyder eller om de behövde
agera. Skapade kognitiv friktion utan informationsvärde för 99% av
användarna (vanlig user har `roles: []`).

**Leverans (Batch D):**

Hela `<dt>Roller</dt> + <dd>...</dd>`-paret wrappat i conditional:
```tsx
{user.roles && user.roles.length > 0 && (
  <div className="flex flex-col gap-1">
    <dt className="text-body-sm text-text-secondary">Roller</dt>
    <dd className="text-body text-text-primary">{user.roles.join(", ")}</dd>
  </div>
)}
```

Tom-state rendering = ingenting. Inkonsistent fält-uppsättning mellan
user-typer accepterad: civic-utility visar bara det som är relevant
för aktuell user.

**CTO-beslut (senior-cto-advisor 2026-05-11):** Variant (a) över (b).
Motivering: NN/g Heuristics #6 (Recognition vs Recall) + #8 (Aesthetic
+ Minimalist), GOV.UK Design Principles #2 "Do less", YAGNI (Hunt/Thomas
1999). Variant (b) "kontakta support om du förväntade dig roller här"
avvisad som cargo-cult-helpfulness — skapar oro där ingen ska finnas.

**Reviews:**
- code-reviewer: 0 Blocker / 0 Major / 0 Minor. Approved.
- design-reviewer: Approved. Civic-utility-konsekvent (GOV.UK/1177/Digg-pattern).

**Tester:** ingår i Batch D-svit (Vitest 226/226 + tsc grön).

---

## TD-4: userId visas i UI utan tydligt användarbehov ✓ STÄNGD 2026-05-11
**Kategori:** UX / Privacy hygiene
**Severity:** Minor
**Fas:** 1 UX-pass /mig
**Källa:** security-auditor, 2026-05-07 (Turn 2)
**Status:** **STÄNGD 2026-05-11 (Batch D)** — Variant (a) ta bort fältet.

mig/page.tsx visade `user.userId` (GUID, font-mono) som första fält i
Kontoinformation-Card. Slutanvändare hade inget direkt behov av Guid.
Möjligt support-värde — men då skulle syftet behöva kommuniceras
tydligt (vilket variant (b) "Support-ID" föreslog).

**Leverans (Batch D):**

Hela `<dt>Användar-id</dt> + <dd>{user.userId}</dd>`-paret borttaget.
Kontoinformation-Card visar nu E-postadress + ev. Roller.

**CTO-beslut (senior-cto-advisor 2026-05-11):** Variant (a) över (b).
Motivering: GDPR Art. 5(1)(c) data minimisation, NN/g #8 Aesthetic +
Minimalist, GOV.UK/1177-pattern (tekniska IDs visas vid felanmälan/support,
inte som default i profilvy). Variant (b) "Support-ID (för felanmälningar)"
avvisad som cargo-cult-transparens — löser ett problem användare inte har
idag (inget felanmälningsflöde i Fas 1) och skapar UI-element som varken
är actionable eller informativt. YAGNI.

Om support i framtiden behöver userId: exponera via dedikerat "Felanmäl"-
flow med explicit UX (kopiera-knapp, kontext) i Fas 2+.

**Reviews:**
- code-reviewer: 0 Blocker / 0 Major / 0 Minor. Approved. GDPR-rensning korrekt utförd.
- design-reviewer: Approved. Card-bredd `max-w-lg` bevarad för visuell rytm.

**Tester:** ingår i Batch D-svit. Inga test-träffar på "Användar-id"-label eller `userId`-UI-rendering.

---

## TD-5: Redundant getServerSession-anrop på /mig ✓ STÄNGD 2026-05-11
**Kategori:** Code hygiene
**Severity:** Minor
**Fas:** 1 UX-pass /mig
**Källa:** security-auditor, 2026-05-07 (Turn 2)
**Status:** **STÄNGD 2026-05-11 (Batch D)** — Variant (a) no-op + dokumentera pattern.

Både `(app)/layout.tsx:12` och `mig/page.tsx:15` anropade
`getServerSession()`. Funktionellt OK — funktionen är `React.cache()`-
wrappad så andra anropet träffar cache. Men kodflödet var otydligare
än nödvändigt — framtida läsare kunde tolka det som duplicering och
föreslå "fix".

**Discovery-fynd (Batch D):** ALLA 7 (app)-sidor anropar `getServerSession()`
direkt — det är etablerad pattern. Att specialfälla just /mig vore
arkitektoniskt inkonsekvent. Variant (b) (layout-prop-passing via Server
Component-context-trick) avvisad som scope-skred + KISS-brott.

**Leverans (Batch D):**

JSDoc tillagd ovan `getServerSession`-export i `lib/auth/session.ts`
som förklarar `React.cache()`-semantiken, att flera anrop per request
är intentional och cache-hit, och att layout-prop-passing avvisades
för SoC-konsistens med övriga (app)-sidor. Inkluderar TD-referens +
datum för granskningstrail.

**CTO-beslut (senior-cto-advisor 2026-05-11):** Variant (a) över (b)/(c).
Motivering: DRY tillämpad korrekt (Hunt/Thomas 1999 — `React.cache()` ÄR
knowledge piece), SoC (Dijkstra 1974), KISS, konsistens (Fowler 2018,
*Refactoring* kap. 3 "Bad Smells").

**Reviews:**
- code-reviewer: Approved. JSDoc-kvalitet föredömlig — "inline ADR för mikrobeslut".
- design-reviewer: ej tillämpligt (ingen UI-impact).

**Tester:** ingår i Batch D-svit.

---

## TD-6: Logout-backend-call utan fel-loggning ✓ STÄNGD 2026-05-11
**Kategori:** Observability
**Severity:** Minor
**Fas:** 1
**Källa:** security-auditor, 2026-05-07 (Turn 2)
**Status:** **STÄNGD 2026-05-11 (Batch E)** — strukturerad console.error med `!res.ok`-täckning.

`logoutAction` anropar backend `/auth/logout`. Vid network/500-fel raderades
cookien lokalt och användaren redirectades — men felet swallow:ades tyst.
Backend-session kvarstod tills Redis-TTL (14d).

**Leverans (Batch E):** `web/jobbpilot-web/src/lib/auth/actions.ts` — strukturerad
`console.error("logout.backend_call_failed", { event, status?, cause? })` på
både network-exception (`catch`) och HTTP-fail (`!res.ok`). PII (session-id,
email, IP) loggas inte — bara event + status + cause.message. Best-effort
semantik bevarad (cookien tas alltid bort lokalt).

**CTO-beslut (senior-cto-advisor 2026-05-11):** strukturerad console.error +
`!res.ok`-täckning. Motivering: YAGNI (frontend-logger-infrastruktur saknas
för enbart 1 call-site), Twelve-Factor §XI (logs som event-streams), Ford
2017 kap. 6 "Operability" (halv täckning är observability-teater). Variant
"retry-mekanism" avvisad — logout idempotent på klientsidan, Redis-TTL tar
hand om backend.

**Reviews:**
- code-reviewer: Approved. Strukturerad event/context-pattern, ingen PII.
- security-auditor: Approved. PII-fri logging verifierad.

**Tester:** ingår i Batch E-svit. Inga regressioner i befintliga tester.

---

## TD-28: Frontend typed-confirmation-UX + re-auth-prompt på DELETE /me ✓ STÄNGD 2026-05-11
**Kategori:** UX / Säkerhet (defense-in-depth)
**Severity:** Minor
**Fas:** 1 (frontend + backend-utökning)
**Källa:** TD-21 ursprungs-Major-2 punkt 3+4 (defererad från STEG 11)
**Status:** **STÄNGD 2026-05-11 (Batch E)** — fullstack leverans per Klas-Alt1-godkännande.

Backend-rate-limit (1 req/60s per UserId, TD-21) var hard ceiling, men UX:en
på frontend saknade defense-in-depth: typed-confirmation + re-auth innan
DELETE /me. Impulsivt klick kunde radera konto utan friktion.

**Leverans (Batch E) — Fullstack:**

**Backend:**
1. **`src/JobbPilot.Application/Auth/Queries/VerifyCredentials/`** — Ny `VerifyCredentialsQuery`, handler + validator. `IQuery<Result>` med `IAuthenticatedRequest`. Hämtar email via `IUserAccountService.GetEmailAsync(userId)` (SessionAuthenticationHandler sätter inte email-claim). Validerar credentials via befintlig `ValidateCredentialsAsync(email, password)`. Defense-in-depth userId-match. Alla failure-paths returnerar `Auth.InvalidCredentials` (oracle-skydd).
2. **`POST /api/v1/auth/verify`** i AuthEndpoints — RequireAuthorization + RequireRateLimiting(AuthWritePolicy 20/min/IP). Returnerar 204 vid success, 401 vid fail. INGEN session-mutation.
3. **5 unit-tester** (VerifyCredentialsQueryHandlerTests) — happy path, wrong password, userId-mismatch, no userId, no email.
4. **4 integration-tester** (VerifyCredentialsTests) — 204 valid, 401 wrong password, 401 utan auth, säkerhetsinvariant "verify ändrar inte session".

**Frontend:**
1. **`deleteMyAccountSchema`** i me-schemas.ts — Zod-validering av confirmEmail + password.
2. **`deleteAccountAction(input, currentEmail)`** i me.ts — server action: validera schema → server-trusted email-match (case-insensitive, trim) → POST /auth/verify (re-auth) → DELETE /me → deleteSessionCookie + redirect till /logga-in.
3. **`<DeleteAccountDialog>`** client component — Radix Dialog + RHF, typed-confirmation = användarens egen e-postadress (GitHub/Stripe-mönster), submit disabled tills email-match + password ifyllt. `aria-invalid` + `role="alert"` + `aria-describedby` för a11y. `autocomplete="off"` på email-fält förhindrar auto-fill-bypass.
4. **`<DeleteAccountSection>`** server component — "Farligt område"-rubrik under separator, beskrivande text, trigger-knapp (destructive variant).
5. **Integration i `mig/page.tsx`** — DeleteAccountSection sist på sidan.
6. **8 Vitest-tester** för DeleteAccountDialog — render, disable-states, email-case-insensitivity, mismatch-disable, action-anrop, server-error-display, PII-safety (verifierar att password/email inte läcker till console).

**CTO-beslut (senior-cto-advisor 2026-05-11 + Klas-Alt1):**
- **Re-auth via ny endpoint** (variant b) över återanvänd /auth/login eller lösenord-i-DELETE-body. Motivering: SRP (Martin 2017 kap. 7), ISP (kap. 10), command-cohesion (Evans 2003).
- **Typed-confirmation = email** (variant b) över magiskt ord "RADERA". Motivering: GitHub/Stripe-pattern, friktion = avsedd.
- **"Farligt område"-section** (variant b) över egen Card sist eller separat sida. Motivering: signalerar gravitet utan att gömma funktionen.
- **Modal via Radix Dialog** över separat sida. Motivering: focus-trap + escape-handling out-of-the-box (WCAG 2.1.2 + 2.4.3).
- **Vitest in-scope, Playwright E2E lyft som TD-65.** Motivering: E2E-auth-fixtures behöver verifieras separat.

Klas valde Alt 1 (utöka Batch E till fullstack) över Alt 2 (split-batch).

**Reviews:**
- code-reviewer: Approved. 0 Blocker / 0 Major / 5 Minor (dokumentations-natur, 2 fixade in-block). "Säkerhetsinvariant `POST_verify_does_not_create_or_change_session` exemplarisk." "PII-safety-test exakt rätt nivå."
- security-auditor: Approved. 0 Blocker / 0 Major / 0 Minor. "GDPR Art. 17-implementation defense-in-depth korrekt, PII-hygien vattentät på båda lager."

**TD-65 lyft:** Playwright E2E för delete-account-flow. Trigger: fixture-status-verifiering innan Fas 1-stängning.

**Tester:** Backend +9 (5 unit + 4 integration). Frontend Vitest 226 → 234 (+8). Architecture 32/32 oförändrat. tsc grön. dotnet format ren.

**Säkerhetskonsekvens:** Användare kan inte längre radera konto med impulsivt klick — kräver:
1. Lokal email-match-validation (UX-friktion)
2. Server-trusted email-jämförelse (auktoritativ)
3. Re-auth via POST /auth/verify (lösenordsverifiering)
4. Endast efter steg 3 → DELETE /me + cookie-cleanup + redirect

Kombo-attack (verify → delete) skyddad av två separata rate-limits (AuthWrite 20/min/IP + AccountDeletion 1/60s/UserId).

---

## TD-12: Saknad integration-test för cross-user isolation ✓ STÄNGD 2026-05-12
**Kategori:** Säkerhet / Test
**Severity:** Minor
**Fas:** 1
**Källa:** STEG 5 discovery 2026-05-08
**Status:** **STÄNGD 2026-05-12 (Batch F)** — 7 integration-tester för Application-aggregat-isolation.

Queries och commands för Application-aggregatet filtrerar korrekt på
`a.JobSeekerId == jobSeekerId` — user A kan inte se eller mutera user B:s
ansökningar. Men detta beteende saknade ett integration-test som verifierade
det explicit. Om filtret tagits bort eller refaktorerats bort i framtiden
hade det inte fångats av testerna förrän manuellt verifierat.

**Leverans (Batch F):** Ny fil `tests/JobbPilot.Api.IntegrationTests/Applications/ApplicationsCrossUserIsolationTests.cs` med 7 tester:

1. `User_B_GET_application_owned_by_user_A_returns_404` — TD-spec primärt scenario
2. `User_B_POST_transition_on_user_A_application_returns_404` — TD-spec primärt scenario
3. `User_B_GET_applications_list_does_not_include_user_A_applications` — list-filter-isolation
4. `User_B_GET_pipeline_does_not_include_user_A_applications` — pipeline-filter-isolation (strikt array-length-assert)
5. `User_B_POST_follow_up_on_user_A_application_returns_404` — sub-resource-isolation
6. `User_B_POST_note_on_user_A_application_returns_404` — sub-resource-isolation
7. `User_A_data_intact_after_user_B_attempted_cross_access` — defense-in-depth: cross-user-attack ger inga sidoeffekter på A:s data

**404-policy (säkerhetsmässig motivering):** alla cross-user-anrop returnerar 404 (inte 403). 403 skulle bekräfta existens av annan users resurs → enumeration-attack-yta. 404 är samma respons som "okänt id" — ingen information-läcka.

**Scope-utökning från TD-spec:** TD-12-spec specificerade 2 tester (GET + transition). Faktisk leverans täcker hela attack-ytan (6 endpoints + defense-in-depth state-test). Motivering per §5.4 + §9.6: cross-user-isolation är säkerhetskritisk invariant — smal täckning ger falsk trygghet.

**Pattern-fidelity:** matchar `ApplicationsTests.cs` (samma collection, AuthTestHelpers, factory.CreateClient). Skillnad: två separata HttpClient-instanser med olika Authorization-header — korrekt för multi-user-test.

**Reviews:**
- code-reviewer: 0 Blocker / 0 Major / 2 Minor (båda fixade in-block: stärkt pipeline-array-length-assert + borttagen oanvänd SessionId-tuple-element). Approved.
- security-auditor: 0 Blocker / 0 Major. "Defense-in-depth-test (test 7) är ovanligt välkonstruerad — fångar check-then-act bugs." Approved. Föreslår 2 nya TDs för parallell täckning (lyfts som TD-66 + TD-67).

**Tester:** 7/7 integration-tester gröna (10s körtid via Testcontainers). Inga ändringar i production-kod.

**Nya TDs lyfta:**
- TD-66: Cross-user-isolation-tester för Resume- och JobSeeker-aggregaten (parallell täckning för andra user-data-aggregates)
- TD-67: Audit-trail för failed cross-user-access-attempts (GDPR Art. 32 anomaly-detection)

---

## TD-65: Playwright E2E för delete-account-flow ✓ STÄNGD 2026-05-12
**Kategori:** Test / E2E
**Severity:** Minor
**Fas:** 1
**Källa:** Batch E split per senior-cto-advisor-triage 2026-05-11
**Status:** **STÄNGD 2026-05-12 (disciplinretur)** — Playwright auth-fixtures fanns redan, in-block-fix per §9.6.

Batch E levererade TD-28 med Vitest-coverage för `DeleteAccountDialog`-komponenten
(8 tester) och .NET integration-tester för `POST /api/v1/auth/verify` (4 tester).
Full E2E-flow var lyft som TD-65 — men på falsk premiss att Playwright auth-
fixtures saknades. Discovery 2026-05-12 visade att `loginAs` + `ensureTestUser`
finns i `tests/e2e/helpers/auth.ts` — TD borde fixats in-block i Batch E.

**Klas-feedback 2026-05-12:** "Varför lyftes hela tiden nya TDs utan att fixa
dom direkt?" — disciplinmiss erkänd, lärdom sparad
(`memory/feedback_td_lifting_discipline.md`).

**Leverans (disciplinretur):**

1. `web/jobbpilot-web/tests/e2e/helpers/auth.ts` — exporterat `TEST_PASSWORD`
   + `testEmail` (var privata) för återanvändning i delete-spec.
2. `web/jobbpilot-web/tests/e2e/delete-account.spec.ts` — 3 Playwright-tester:
   - Modal öppnas + submit-disabled-states (email-match required)
   - Happy path: typed-confirmation + password → POST /auth/verify + DELETE /me
     → redirect /logga-in. **Inkluderar direkt backend-call med fångad
     session-token efter delete → verifierar 401** (ADR 0024 D4
     Redis-session-revoke + GDPR Art. 17).
   - Fel lösenord → 401 → form-error visad utan session-mutation

Varje test skapar egen runId (destruktiv operation).

**Reviews:**
- code-reviewer: 0 Blocker / 0 Major / 3 Minor (1 fixad in-block — explicit
  HttpStatusCode, 2 noteringar). Approved.
- security-auditor: 1 Major (Redis-session-revoke explicit verification) fixad
  in-block. "Cookie-stöld-scenario nu täckt." Approved.

**Tester:** 3 E2E-tester (ej körda i CI — kräver lokal dev-stack med
`BACKEND_URL`-injection). tsc grön.

---

## TD-66: Cross-user-isolation-tester för Resume- och JobSeeker-aggregaten ✓ STÄNGD 2026-05-12
**Kategori:** Säkerhet / Test
**Severity:** Minor
**Fas:** 1
**Källa:** security-auditor Batch F-review 2026-05-12 (TD-12-leverans-utvidgning)
**Status:** **STÄNGD 2026-05-12 (disciplinretur)** — mekaniskt pattern-spegling, in-block-fix per §9.6.

Batch F levererade ApplicationsCrossUserIsolationTests men lyfte TD-66 för
parallell täckning av Resume + JobSeeker. **Klas-feedback:** disciplinmiss
— mekaniskt scope i samma fas borde fixats in-block.

**Leverans (disciplinretur):**

1. `tests/JobbPilot.Api.IntegrationTests/Resumes/ResumesCrossUserIsolationTests.cs`
   — 7 integration-tester:
   - GET /resumes/{A-id} från B → 404
   - PATCH /resumes/{A-id} från B → 404
   - PUT /resumes/{A-id}/master från B → 404
   - DELETE /resumes/{A-id}/versions/{fakeVid} från B → 404 (enumeration-skydd
     med slumpat versionId)
   - DELETE /resumes/{A-id} från B → 404
   - GET /resumes (PagedResult-list) från B → A:s resume inte i items
   - State-intact: A:s resume orörd efter B:s alla attack-försök

2. `tests/JobbPilot.Api.IntegrationTests/Me/MeProfileCrossUserIsolationTests.cs`
   — 3 integration-tester:
   - PATCH /me/profile från B muterar B:s profil, A:s orörd
   - Defense-in-depth: PATCH från B med extra `userId`/`jobSeekerId`-fält i
     payload (potentiell ID-injection) påverkar inte A:s profil
   - GET /me returnerar caller-userId (sanity)

Täckning per 2026-05-12: hela `/api/v1/resumes`-yta + `/api/v1/me` + `/api/v1/me/profile`.

**404-policy:** alla cross-user-anrop returnerar 404 (samma som ApplicationsCross
UserIsolationTests). Enumeration-attack-skydd per OWASP API1:2023 BOLA.

**Reviews:**
- code-reviewer: 0 Blocker / 0 Major / 3 Minor (1 fixad in-block, 1 skippad
  pattern-konsekvens med MeTests.cs, 1 noterad). Approved.
- security-auditor: 0 Blocker / 1 Major fixad in-block (Redis-session-revoke
  i TD-65), 3 Minor varav 2 fixade in-block (version-delete-test + state-
  intact-test för Me + scope-doc), 1 noterad. Approved.

**Tester:** 7/7 Resume + 3/3 Me integration-tester gröna. Inga ändringar i
production-kod.

**Lärdom (sparad i `memory/feedback_td_lifting_discipline.md`):**
TD-lyftningar måste pressas mot §9.6-kriterier — "scope-disciplin per batch"
eller "+1-2h CC-tid" är INTE legitima skäl. Default = in-block-fix.

---

## TD-67: Audit-trail för failed cross-user-access-attempts ✓ STÄNGD 2026-05-12
**Kategori:** GDPR / Observability / Anomaly-detection
**Severity:** Minor
**Fas:** 1
**Källa:** security-auditor Batch F-review 2026-05-12
**Status:** **STÄNGD 2026-05-12** via ADR 0031 — strukturerad logging + IFailedAccessLogger-port. CloudWatch-aggregat lyft som TD-68 (separat Terraform-leverans).

ADR 0022 etablerade audit-log pipeline behavior för success-mutationer.
Failed authorization-attempts (404 från ownership-filter — cross-user-access
mot annan users resurs) loggades inte — utan audit-aggregat var
BOLA-enumeration-attack (OWASP API1:2023) osynlig för anomaly-detection.

**Leverans (2026-05-12):**

1. **ADR 0031** — Failed cross-user access detection: strukturerad loggning + CloudWatch-aggregat. Hybrid F-strategi (ej audit_log-rad — ops-signal via ILogger). Bevarar ADR 0022 immutable.

2. **`IFailedAccessLogger`-port** (`Application/Common/Auditing/`) — `LogCrossUserAttempt(aggregateType, requestedAggregateId, requestingUserId, operation)`.

3. **`FailedAccessLogger`-impl** (`Infrastructure/Auditing/`) — `ILogger<T>`-baserad med `LoggerMessage`-source-gen. Strukturerade fält: `event_name=failed_access_attempt`, `aggregate_type`, `requested_aggregate_id`, `requesting_user_id`, `operation`. EventId 4001, LogLevel Warning.

4. **9 handlers modifierade** med inline-pattern:
   - Application: `GetApplicationByIdQueryHandler`, `TransitionToCommandHandler`, `AddFollowUpCommandHandler`, `AddNoteCommandHandler`
   - Resume: `GetResumeByIdQueryHandler`, `RenameResumeCommandHandler`, `UpdateMasterContentCommandHandler`, `DeleteResumeCommandHandler`, `DeleteResumeVersionCommandHandler`

   Pattern: vid ownership-mismatch (FirstOrDefault returnerar null) — gör extra existens-query (`AnyAsync` utan user-filter). Om aggregat finns men ägs av annan → logger anropas. Okänt id loggas INTE.

5. **Unit-tester** — befintliga 9 test-filer uppdaterade med `IFailedAccessLogger`-stub. 4 nya fokus-tester (1 query + 1 command × 2 fall: ownership-mismatch loggar, okänt id loggar inte).

6. **DI-registrering** — `IFailedAccessLogger` som singleton i `AddPersistence` (stateless wrapper).

7. **TD-68 lyft** — CloudWatch metric filter + SNS-alarm. Kriterium 2 (saknad Terraform-infrastruktur).

**CTO-beslut (senior-cto-advisor 2026-05-12):** Hybrid F + IFailedAccessLogger-port. Motivering: SoC (audit_log = compliance, failed-access = ops-signal — olika livscykler, olika konsumenter), ADR-immutabilitet (0022 bevaras), YAGNI (CloudWatch metric filter > inline-throttling), Clean Arch (säkerhets-logik i Application där informationen finns, inte i Api-middleware), GDPR Art. 32 proportionalitet (failed-access är inte "behandling" i Art. 5(2)-mening).

**Avvisade alternativ:** Alt A (utöka AuditBehavior — bryter ADR 0022 + differentierings-problem), Alt B (ny pipeline-behavior — samma problem), Alt C (domain event — saknar dispatcher, ej domän-händelse), Alt D (middleware — vet inte varför 404 → noise), Alt E (inline utan port — bryter testbarhet).

**Reviews:** (genomförs vid commit av denna batch).

**Tester:** 213 Application UnitTests gröna (+4 nya för logger-bevakning). Befintliga cross-user-integration-tester (TD-12, TD-66) bevakar fortsatt 404-beteende utan API-respons-skillnad.

---

## TD-25: HardDeleteAccountsJob per-konto try/catch (resilient loop) ✓ STÄNGD 2026-05-12
**Kategori:** Robusthet / Operations
**Severity:** Minor
**Fas:** 1+ (opportunistiskt — flyttad till Fas 1 in-block per CTO-beslut)
**Källa:** Code review STEG 10b 2026-05-08 (Code-Nit-5)
**Status:** **STÄNGD 2026-05-12 (commit `eed6cc2`)** — in-block-fix per §9.6.

`HardDeleteAccountsJob.RunAsync` saknade try/catch per konto i Steg 2-loopen.
Vid första exception bubblade den och avbröt loopen för alla efterföljande
konton. Hangfire retry:ar hela jobbet, men under retry-fönstret var de andra
moget-för-deletion-kontona blockerade.

**Risk:** låg i Fas 1 (få konton), medium vid skala. Asymmetri: 30 min CC-tid
nu vs produktions-blockering vid första error i Fas 2.

**Leverans (commit `eed6cc2`):**

1. **Steg 2 foreach-loop wrappad i try/catch:**
   - `OperationCanceledException` re-throws (shutdown-cancel sväljs INTE)
   - Generisk `Exception` fångas, `failed++`, `LogAccountFailed(jobSeekerId, ex)` med EventId 2502, Error-level
   - Loop fortsätter med nästa konto
2. **`LogComplete` utökad** med `failed`-parameter — slutlogg visar processed + failed för operativ insyn
3. **4 unit-tester** (`Application.UnitTests/Auth/Jobs/HardDeleteAccounts/HardDeleteAccountsJobTests.cs`):
   - Single account fail → andra processeras (isolation-invariant)
   - Account fail → jobbet kastar INTE (no-throw-invariant)
   - OperationCanceledException → propageras (cancel-disciplin per §3.5)
   - All accounts fail → jobbet kör färdigt ändå (idempotens-invariant)

**Pattern-fidelity:** matchar `DetectGhostedApplicationsJob` (ADR 0023) med
**motiverad avvikelse**: HardDeleteAccounts har multi-boundary cascade
(transactional cascade + Identity-DELETE som separat boundary) som kräver
per-konto isolation där MarkGhosted inte gör det. Dokumenterad i XML-doc.

**Idempotens-invariant (ADR 0024 D6) bevarad:** failade konton plockas upp
av nästa cron eftersom `processed++` ligger efter `await` — bara success
räknas. Jobbet markerar INTE failade konton som "klart".

**CTO-beslut (senior-cto-advisor 2026-05-12):** in-block-fix per §9.6
fas-regeln. TD-25 klassad "Fas 1+/Opportunistiskt" — scope ~30 min, ingen
dependency-blockare. Motivering: defensive programming (Hunt/Thomas 1999
kap. 4), risk-asymmetri (~30 min nu vs produktions-blockering i Fas 2).

**Reviews:**
- code-reviewer: 0 Blocker / 0 Major / 0 Minor. Approved. "Cancel-disciplin
  bevisas maskinellt via test, inte bara via kod-läsning."

**Tester:** 217 Application UnitTests gröna (+4 nya). dotnet format ren.
EventId 2502 unikt i repot (verifierat mot 1-5, 1001-1003, 2001, 4001, 999).

---

## TD-68: CloudWatch security-alarms för failed_access_attempt-events ✓ STÄNGD 2026-05-12
**Kategori:** Observability / Infrastructure (Terraform)
**Severity:** Minor
**Fas:** 1
**Källa:** TD-67-leverans 2026-05-12 (ADR 0031 — anomaly-detection-skiktet)
**Status:** **STÄNGD 2026-05-12 (commit `70ca42b` + dev-apply genomförd).** Prod-invokation defereras till prod-ECS-stack-leverans (utan blocker — ny inkrementell leverans när prod-stack landar).

ADR 0031 etablerade `IFailedAccessLogger`-strategin (TD-67). CloudWatch-
aggregat (metric filter + SNS-alarm) levererades som separat Terraform-
batch med dedikerad apply-disciplin per CLAUDE.md §9.2.

**Leverans (commit `70ca42b`):**

Modul `infra/terraform/modules/cloudwatch_security_alarms/`:
- `aws_cloudwatch_log_metric_filter` — unquoted substring-pattern `event_name=failed_access_attempt` mot api-log-gruppen. Pre-apply-verifierat via `aws logs test-metric-filter` (1 av 3 sample-events matchade — endast riktiga events, inga false-positives).
- `aws_sns_topic` `jobbpilot-dev-secops-anomaly` — KMS-encrypted (master-key).
- `aws_sns_topic_policy` — least-privilege: Service `cloudwatch.amazonaws.com` + `AWS:SourceAccount` + `AWS:SourceArn` (`${name_prefix}-*`). Defense-in-depth mot alarm-suppression-attack.
- `aws_cloudwatch_metric_alarm` failed-access — Sum-statistic, threshold 50/60s, `treat_missing_data=notBreaching`.
- `aws_cloudwatch_metric_alarm` log-pipeline-health — `IncomingLogEvents <= 0` över 15 min, `treat_missing_data=breaching`. Säkerhets-komplement: detekterar bruten log-pipeline (pipeline-fel gör anomaly-detection bevisbart icke-funktionell).
- `aws_sns_topic_subscription` (conditional på `alert_email != ""` — default tom, Klas opt-in manuellt vid behov).

Dev-env-invokation. Prod-invokation defereras tills prod-ECS-stack levereras (prod har inte `cloudwatch_logs`-modul än).

Runbook: `docs/runbooks/failed-access-anomaly.md` — klassificering vid alarm-trigger (BOLA-singel HIGH, distributed MEDIUM, false-positive LOW), respons-flöden per scenario, pre/post-apply-verifiering, SNS-rotation-procedur, GDPR Art. 33 risk-bedömning + IMY-notification-flöde, severity-klassificeringstabell.

**Reviews (3 rundor):**
- security-auditor #1: 2 Major + 6 Minor.
- security-auditor #2 (efter M1+M2+Minor 7 fix): Approved, 4 Minor kvar.
- security-auditor #3 (efter alla Minor + runbook): Approved, kosmetisk IMY-namnrättning fixad in-block.

Fixar in-block (alla per CLAUDE.md §9.6 + Klas-direktiv "ordentligt"):
- **M1**: filter-pattern `quoted` → `unquoted` (quoted=token-match missade termen mitt i text-message → tyst säkerhetshål).
- **M2**: explicit `aws_sns_topic_policy` med Service-principal + SourceAccount + SourceArn.
- **Minor 1**: KMS master-key blast-radius dokumenterad.
- **Minor 2**: threshold-tuning-recommendations per env (dev 50, prod initial 20, tuned 10).
- **Minor 3**: separat log-pipeline-health-alarm (IncomingLogEvents).
- **Minor 4**: runbook skapad.
- **Minor 7**: SourceArn-condition på SNS-topic-policy.
- **Kosmetisk**: IMY-namn + Art. 33 risk-bedömning i runbook.

**Apply-verifiering 2026-05-12:**
- `terraform plan -target=module.cloudwatch_security_alarms`: 5 to add, 0 change, 0 destroy.
- Klas-GO för apply.
- `terraform apply td68.tfplan` genomfört. State innehåller 5 resources + data.
- `aws cloudwatch describe-alarms` bekräftar båda alarm i `INSUFFICIENT_DATA`-state (förväntat — växlar till `OK` vid första data-punkt).

**CTO-beslut (senior-cto-advisor 2026-05-12):** in-block-fix per §9.6 + separat batch från TD-25 ("ordentligt" = rigorös batch i taget med full review-cykel). Klas-STOPP-punkter respekterade: före kod-commit, före plan, före apply.

**Säkerhetsinvariant:** BOLA-enumeration-attack (OWASP API1:2023) nu detekterbar i dev-trafik. Per-user-drill-down via CloudWatch Insights-query i runbook. Log-pipeline-health-alarm säkrar att detection-kedjan är funktionell (skyddar mot "tyst pipeline gör anomaly-detection osynlig").

---


## TD-69: SesEmailSender (AWS SES) — ersätter ConsoleEmailSender i prod

**Stängd:** 2026-05-12 (samma dag den lyftes — disciplinretur)
**Kategori:** Infrastructure / Email
**Severity:** Minor
**Fas:** 2

**Kort historik:** TD-69 lyftes initialt i F2-P0d 2026-05-12 med motiveringen
att AWSSDK.SimpleEmailV2-NuGet kräver Klas-GO + SES domain verification är
operations-side. Klas påpekade omedelbart att detta inte var legitim TD-lyft
per CLAUDE.md §9.6 + memory `feedback_td_lifting_discipline.md`:

1. NuGet-GO är en mikrostop (§9.2), inte "saknad funktion-dependency"
2. SES sandbox-mode räcker för klasskamrat-tester — domain verification kan
   skjutas till innan public launch men inte blockera kod-impl

**Disciplinretur 2026-05-12:**

- Klas gav GO på AWSSDK.SimpleEmailV2 4.0.5.8 + AWSSDK.Core 4.0.6.1
  (transitiv CVE-pinning för GHSA-9cvc-h2w8-phrp)
- `SesEmailSender.cs` impl mot `IAmazonSimpleEmailServiceV2`
- `EmailOptions.Provider="Ses"` aktiverar SES-binding i DI
- `EmailOptions.AwsRegion` default `eu-north-1`
- `ConsoleEmailSender` bibehållen som dev-/test-default

**Vad TD-69 lämnar kvar (operations, ej kod):**

Klas verifierar manuellt i AWS-konsolen innan första utskick:
- SES domain verification (eller individuella mottagar-emails i sandbox)
- DKIM-DNS-records hos domain-registrar
- SPF-record för `jobbpilot.se`
- Production access-ansökan innan `registrations_open=true` på publik URL

Detta är operations-arbete som Klas gör runt sin egen tid — inte CC-scope.

**Källa:** F2-P0d disciplinretur 2026-05-12 efter Klas-feedback om
TD-lyftnings-disciplin. Memory `feedback_td_lifting_discipline.md` validerad
i praktiken: TD-lyftningar måste pressas hårt mot §9.6 — "NuGet kräver GO"
är inte funktion-dependency, det är bara Klas-stopp.

**Lärdom:** Vid framtida TD-lyft som motiveras av "kräver Klas-GO för X" —
fråga Klas direkt om X istället för att lyfta TD. Default = in-block-fix.

---

## TD-29: Strict readiness-probe — separera liveness från readiness

**Kategori:** Observability / Deployment hygiene
**Severity:** Minor
**Fas:** 2
**Källa:** dotnet-architect, STEG 13b review (2026-05-09)
**Stängd:** 2026-05-12 (F2-P6)
**Stängning-commit:** kommande F2-P6-commit i denna session

### Ursprungsbeskrivning

`/api/ready`-endpoint i `src/JobbPilot.Api/Program.cs:128` returnerade 200 OK
utan DB/Redis-ping → namnet "ready" var missvisande. Det var liveness, inte
readiness i Kubernetes-konventions-mening. Konsekvens: ALB target-group
registrerade tasken som "healthy" innan `AppDbContext` var användbar — under
EF Core cold-start kunde första requests få 500.

För Fas 0/MVP räckte liveness (BUILD.md §15.4 säger inte explicit "readiness
inkluderar DB"). Vid Fas 2 trafikvolym behövdes strict readiness annars
dyker rolling-deploys 503:or under den ~10-30 sekunders DbContext-warmup-fönstret.

### Leverans 2026-05-12 (F2-P6)

**Kod:**

- `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.7
  lagt till i `Directory.Packages.props` (Microsoft-paket, inte third-party)
- `src/JobbPilot.Api/HealthChecks/RedisHealthCheck.cs` — custom IHealthCheck
  via IConnectionMultiplexer.IsConnected + PingAsync (10 rader). Undviker
  Xabaril-dep eftersom semantiken är trivial.
- `src/JobbPilot.Api/Program.cs` — `AddHealthChecks()` med
  `.AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"])` +
  `.AddCheck<RedisHealthCheck>("redis", tags: ["ready"])`
- `MapHealthChecks("/api/live", Predicate _ => false)` — bara process-status
- `MapHealthChecks("/api/ready", Predicate Tags.Contains("ready"))` —
  DB + Redis-ping
- Legacy `/health` och `MapGet /api/ready` borttagna

**Tester (6 nya integration-tester):**

`tests/JobbPilot.Api.IntegrationTests/HealthChecks/HealthCheckEndpointsTests.cs`:

1. `ApiLive_ReturnsHealthy_WhenProcessIsUp` — 200 OK på /api/live
2. `ApiReady_ReturnsHealthy_WhenDatabaseAndRedisAreReachable` — 200 OK med
   Testcontainers Postgres + Redis aktiva
3. `ApiLive_DoesNotEvaluateRegisteredChecks` — anti-regression: predicate
   _ => false så svaret kommer under 500ms (ingen DB-roundtrip)
4. `ApiReady_IsAnonymouslyAccessible` — anti-regression mot framtida
   RequireAuthorization-glömska
5. `ApiLive_IsAnonymouslyAccessible` — samma disciplin
6. `LegacyHealthEndpoint_IsRemoved` — anti-regression mot oavsiktlig
   återinförande av /health

**Test-suite:** 217 → 223 (+6) Api.IntegrationTests gröna.

### ALB-konsekvens

ALB target-group health-check-path är redan `/api/ready` (BUILD.md §15.4 +
`modules/alb/variables.tf` default). Ingen Terraform-ändring krävs.

Under rolling-deploys av denna ändring fortsätter gamla tasks köra med stale
/api/ready (200 alltid) → minst en healthy task hela tiden. Nya tasks får
inte trafik förrän DbContext + Redis-multiplexer initierats (typiskt 10-30s)
— exakt det önskade beteendet TD-29 motiverade.

### Beslut: ingen Xabaril-dep

Föreslagen åtgärd i ursprungs-TDn använde Xabaril `.AddRedis(redisCs, ...)`.
Bytte till custom RedisHealthCheck eftersom:

- Semantiken är 10 rader kod (IsConnected + PingAsync)
- Third-party-paket-yta minimerad
- Konsistent med STEG 13b-mönster "use platform features, not familiar tools"
- Microsoft AddDbContextCheck är official Microsoft, inte third-party

**Lärdom:** TD-foreslagen-åtgärd är inte alltid optimal — discovery + verifikation
mot CLAUDE.md-principer (anti-pattern §5: "Generiska 'Service'-suffix") kan
peka på enklare lösning.

---

## TD-56: ListJobAdsQuery full paginering — STÄNGD 2026-05-12 (F2-P7)
**Kategori:** Architecture
**Severity:** Minor
**Fas:** 2 (JobTech Integration)
**Källa:** TD-55-CTO-beslut, 2026-05-11
**Stängd:** 2026-05-12 (F2-P7)
**Decision-maker:** senior-cto-advisor 2026-05-12 (rond 1, F2-P7+P8-batterier)

### Ursprung
`ListJobAdsQueryHandler` var opaginerad med `.Take(500)` hard cap som
temporär defense-in-depth. Vid Fas 2 JobTech-integration skulle den retro-fittas
till full `PagedResult<JobAdDto>` med query-params som matchar JobTech-API:t.

**Ursprunglig föreslagen åtgärd:**
1. Lyft `MaxItems = 500`-konstant från handler till `JobAdOptions`-record
2. Refactor `ListJobAdsQuery` → `PagedResult<JobAdDto>` med PageNumber/PageSize
3. Bestäm anonym-vs-auth-policy för publik JobAd-katalog
4. Anpassa URL-kontrakt mot JobTech-API:s sök-params

### Leverans
CTO-beslut F2-P7 (granskningstrail: `docs/reviews/2026-05-12-f2-p7-p8-cto.md`):
- **A1 offset-based paginering** över cursor (REP/CCP, befintlig `PagedResult<T>`-mönster från TD-55)
- **B1 generic `PagedResult<T>`** återanvänd från `Application.Common`
- **C2 whitelisted `JobAdSortBy`-enum** (PublishedAtDesc default, PublishedAtAsc, ExpiresAtDesc, ExpiresAtAsc) per CLAUDE.md §5.1 "Magic strings förbjudet"
- **D1 endast paginering** (search/filter blir TD-70 efter P7 + P8)
- **E1 breaking byte av befintlig endpoint** (ingen frontend-konsument finns)

Anonym-vs-auth-frågan från ursprungs-TD blev redan beslutad i **ADR 0005**:
"JobAd-listning/sökning är auth-gated i Fas 2-start" — men endpoint exponeras
fortsatt utan `RequireAuthorization()` inline (CreateJobAd-endpoint är
admin-only via separate mekanism). Auth-gating-strikthet följer ADR 0005-policy
vid Fas 2 publik-exponering.

JobAdOptions-extraktion (ursprungs-åtgärd 1) blev **inte nödvändig** — hard cap
borttagen helt, `pageSize` validerad till 1–100 via FluentValidation matchar
samma defense-in-depth-syfte.

URL-kontrakt-anpassning mot JobTech (ursprungs-åtgärd 4) inkluderas i P8-design
(ADR 0032 — kommande). P7 etablerar JobbPilot-intern paginerings-yta;
JobTech-mapping görs i P8 via Refit-attributes.

### Kod
- **NY** `src/JobbPilot.Application/JobAds/Queries/ListJobAds/JobAdSortBy.cs` — enum
- **NY** `src/JobbPilot.Application/JobAds/Queries/ListJobAds/ListJobAdsQueryValidator.cs`
- `ListJobAdsQuery.cs` — `record(int Page=1, int PageSize=20, JobAdSortBy SortBy=PublishedAtDesc) : IQuery<PagedResult<JobAdDto>>`
- `ListJobAdsQueryHandler.cs` — separat count-query (CLAUDE.md §3.6), Skip/Take, ApplySort-dispatch med Id som tiebreaker för deterministisk ordning
- **NY** `src/JobbPilot.Api/Endpoints/JobAdsEndpoints.cs` — flyttad från inline Program.cs
- `Program.cs` — JobAds-inline-block borttaget, `app.MapJobAdsEndpoints()` istället

### Tester
- **9 nya unit-tester:** 6 handler-tester (paginering, sort-varianter, NULL-handling för ExpiresAt) + 4 validator-tester. Application.UnitTests: 249 → **258 PASS**.
- **3 nya integration-tester:** paged-result-shape, query-param-honoring, 400 vid invalid input. Api.IntegrationTests: 223 → **226 PASS**.
- **Arch-test:** `ListJobAdsQuery_returns_PagedResult` explicit regression-skydd. PagedResultContractTests: 3 → **4 PASS**.

### Beslut-rationalet (CTO)
- **Offset över cursor (A1):** UI är "Sida 1, 2, 3"-paginerad jobblista. Cursor-pagination optimerar mot concurrent-insert-skew som inte existerar med 10-min sync-cykler + `publishedAt DESC`. REP/CCP (Martin 2017 kap. 13) — generic `PagedResult<T>` etablerad genom TD-55, ingen ny mall behövs.
- **Whitelisted sort-enum (C2):** Magic-strings förbjudet (CLAUDE.md §5.1). OCP-extension via enum.
- **NULL-handling för ExpiresAt:** `ExpiresAt == null` sorteras sist (har inget slut-datum = pågående). Konsistent semantik mellan ASC och DESC sort.
- **Deterministisk ordning via Id tiebreaker:** garanterar att paginering inte tappar items vid lika `PublishedAt` mellan sidor.

### Source-files-state vid stängning
- `src/JobbPilot.Application/JobAds/Queries/ListJobAds/` — 4 filer
- `src/JobbPilot.Api/Endpoints/JobAdsEndpoints.cs`
- `tests/JobbPilot.Application.UnitTests/JobAds/Queries/ListJobAds/` — 2 test-filer
- `tests/JobbPilot.Api.IntegrationTests/JobAds/ListJobAdsTests.cs`
- `tests/JobbPilot.Architecture.Tests/PagedResultContractTests.cs` (uppdaterad)


---

## TD-73: JobTech raw_payload PII-stripping + retention ✓ STÄNGD 2026-05-13 (TD-73 prod-gating-batch)
**Kategori:** GDPR / Privacy
**Severity:** Major
**Fas:** 2 (P8b/P8c/prod-gating)
**Källa:** security-auditor F2-P8a-aggregat-review Sec-Major-1 (2026-05-12)
**Stängd:** 2026-05-13 — TD-73 prod-gating-batch (ADR 0035 + ADR 0032 amendment 2026-05-13)

### Bakgrund
`raw_payload` (jsonb på `job_ads`) lagrade ursprungligen oavkortat JobTech-svar inkl rekryterar-PII (namn, email, telefon, firmatecknare). GDPR Art. 5/17/30-implikationer adresserade i fyra etapper (P8b + P8c + prod-gating-batch).

### Leverans-stäger

- **Punkt 1 — PII-stripping vid ingest** (F2-P8b, 2026-05-13, commit `8c09191`)
  - `JobTechPayloadSanitizer` pure static allowlist (default-deny per Saltzer/Schroeder 1975)
  - 8 unit-tester verifierar att kontakt-PII strippas + publika fält bevaras
  - Sanering körs i `PlatsbankenJobSource.TryConvertToImportItem`
  - Architecture-test verifierar att Application aldrig ser osanerad payload

- **Punkt 2 — 30-dagars retention** (F2-P8c, 2026-05-13, commit `81dfab6`)
  - `PurgeStaleRawPayloadsJob` Hangfire recurring `30 4 * * *` UTC
  - `JobSourceRetentionOptions.RawPayloadRetentionDays` (Application-port, default 30, bind mot JobTech-section)
  - `ExecuteUpdateAsync` (CLAUDE.md §3.6 — LINQ-genererad SQL)

- **Punkt 3 — Processing-register-entry** (F2-P8b, 2026-05-13, commit `8c09191`)
  - `docs/runbooks/gdpr-processing-register.md` skapad
  - JobTech-entry med rättslig grund (Art. 6(1)(f)) + retention + sub-processor

- **Punkt 4 — Audit-wire α + right-to-erasure** (TD-73 prod-gating-batch, 2026-05-13)
  - **Audit-wire (a):** `ISystemEventAuditor`-port (Application/Common/Auditing) + `SystemAuditEvent`-hierarki (`JobAdsSynced`, `RawPayloadPurged`). `audit_log.payload` jsonb-kolumn aktiverad. Per-run-Guid AggregateId (CTO Q1=A). Idempotens-skydd via `(EventType, AggregateId)`-lookup. Arch-test mirror:ar IAuditTrailEraser-pattern. ADR 0035 etablerar mönstret. EF-migration `AddAuditLogPayload`.
  - **Right-to-erasure (b):** `RedactRecruiterPiiCommand` (Email-only) + admin-endpoint `POST /api/v1/admin/job-ads/redact-recruiter-pii`. Total null-out via `ExecuteUpdateAsync(SetProperty(j => j.RawPayload, _ => null))` (CTO Q2=A). En aggregerad audit-rad per request (CTO Q3=B, ADR 0024 D4-precedens). Name-typ defererad till TD-75. GIN-index defererad till TD-76.
  - **ADR-leverans:** Ny ADR 0035 + amendment till ADR 0032 §8 + cross-ref-amendment ADR 0024. Inget ADR 0022-amendment (ISystemEventAuditor parallell till pipeline-behavior, inte utvidgning).
  - **Runbooks:** `docs/runbooks/recruiter-pii-erasure.md` (auto-flöde Email + manuell-flöde Name).
  - **Reviewers INLINE:** dotnet-architect (design-skiss) + senior-cto-advisor (13 beslut entydigt mot principer — Martin/Evans/Fowler/Beck/Saltzer-Schroeder/GDPR). INGET Klas-STOPP behövdes per CLAUDE.md §9.6 punkt 5.

### Resultat-trail
- ADRs: 0035 (ny), 0032 amendment 2026-05-13 (§8 punkt 4 levererad), 0024 cross-ref-amendment 2026-05-13
- Domain: `AuditLogEntry.Payload` + `CreateSystemEvent`-factory (bevarar Guid.Empty-invariant)
- Application ports: `ISystemEventAuditor`, `IRecruiterPiiPurger`, `SystemAuditEvent`-hierarki, `RedactRecruiterPiiCommand`
- Infrastructure: `SystemEventAuditor`, `RecruiterPiiPurger`, EF-migration `AddAuditLogPayload`
- Audit-wire i alla 3 Hangfire-jobben (Stream, Snapshot, Purge)
- Admin-endpoint `POST /api/v1/admin/job-ads/redact-recruiter-pii`
- Architecture-tester: ISystemEventAuditor + IRecruiterPiiPurger konsumentlistor (4 nya)
- Unit-tester: Domain (+7), Application (+16)
- Integration-tester: Api +6 (audit-paritet + redact end-to-end mot riktig Postgres)
- Total backend: 837 → 870 (+33 nya)

### Cross-ref för follow-ups
- TD-75: Name-baserad rekryterar-PII-radering (trigger: första faktiska Name-begäran)
- TD-76: GIN-index på raw_payload (trigger: latens-trigger eller volym-skifte)

### Operativ konsekvens
v0.2-prod-tag är inte längre gated på TD-73. Sanitizer + retention + audit-wire + right-to-erasure tillsammans täcker GDPR Art. 5/17/30 för rekryterar-PII i raw_payload.

---

## TD-79: ECS-service.task_definition strukturell drift mellan Terraform och deploy-dev.yml ✓ STÄNGD 2026-05-13

**Kategori:** Infra / IaC-hygien
**Severity:** Minor (operativt — drift är synlig och förutsägbar, ingen säkerhets-/data-risk)
**Fas:** 2 (pipeline-hygien — separat ops-pillar från observability)
**Källa:** senior-cto-advisor 2026-05-13 rond 4 (terraform plan-discovery under A3-apply per ADR 0036)
**Stängd:** 2026-05-13 — D+A-session, in-block-fix per CLAUDE.md §9.6

`deploy-dev.yml` (GitHub Actions, tag-baserad deploy per BUILD.md §15.3)
uppdaterar `ECS-service.task_definition` via `aws ecs update-service
--task-definition :NEWREV` utanför Terraform vid varje `v*-dev`-tag.
Resultat: state-divergens växer per deploy.

**Verifierat i plan-output 2026-05-13 (HEAD `896dcf1`):**

- `module.ecs.aws_ecs_service.worker.task_definition: :8 → :1` (Terraform vill
  rolla tillbaka till initial deploy-version)
- `module.ecs.aws_ecs_task_definition.api` MUST BE REPLACED (cosmetic JSON-
  cleanup + AdminBootstrap-env-var-synk)

Worker-rollback :8 → :1 var **potentiellt destruktivt** — skulle ha förlorat
live-verifierade features (TD-73 audit-wire, ADR 0035 system-events, JobTech
v2-integration) tills nästa GitHub Actions-deploy.

**Leverans (TD-79-fix):**

`lifecycle { ignore_changes = [desired_count, task_definition] }` tillagt på:

- `module.ecs.aws_ecs_service.api`
- `module.ecs.aws_ecs_service.worker`

Pattern verifierat mot HashiCorp officiella docs ([aws_ecs_service registry](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/ecs_service)
+ [lifecycle meta-argument](https://developer.hashicorp.com/terraform/language/meta-arguments/lifecycle)).
Terraform sätter `task_definition` vid initial create; CI/CD-pipeline äger
uppdateringen därefter.

**Post-apply-verifiering (2026-05-13):**

| Resurs | Pre-fix plan | Post-fix plan |
|---|---|---|
| `aws_ecs_service.api.task_definition` | ~ update | ❌ no-op |
| `aws_ecs_service.worker.task_definition` | ~ :8 → :1 (rollback) | ❌ no-op |
| `aws_ecs_task_definition.api` | -/+ replace (cosmetic + env-var) | ✓ apply genomförd (revision :13 ny, service ignorerar) |
| `aws_db_parameter_group.this` | ~ apply_method cosmetic | ~ kvarstår (pre-existing, ej TD-79-scope) |

**Live-state efter apply:**
- `jobbpilot-dev-api`: TaskDef `:13` (CI/CD-ägd revision behållen)
- `jobbpilot-dev-worker`: TaskDef `:8` (NOT rolled back to `:1` — bevis att TD-79-fix fungerar)
- `https://dev.jobbpilot.se/api/ready` → HTTP 200 OK efter apply
- 3 CloudWatch-alarms fortsatt OK-state

**Bonus-effekt — AdminBootstrap-env-var-ägarskap löst:**
Task-def-resursen replaceras (ny revision skapas av Terraform med
`AdminBootstrap__InitialAdminEmail` synkad in) men service ignorerar
`task_definition` → Terraform äger task-def-content, CI/CD äger
revision-deployment. Pending operativt punkt borta.

**Cross-refs:**

- ADR 0036 (A3-apply 2026-05-13 — drift first observed)
- senior-cto-advisor 2026-05-13 rond 4 Q3 (TD-lyftning godkänd mot §9.6)
- HashiCorp `lifecycle.ignore_changes`-pattern

---

## TD-70: Search/filter-yta för JobAd-katalog (`?ssyk&?region&?q`) ✓ STÄNGD 2026-05-13

**Kategori:** Feature / API
**Severity:** Major (Fas 2 — userfacing search)
**Källa:** TD-56 stängning + ADR 0032 "Out of scope" 2026-05-12 (search/filter separat efter P7+P8)
**Stängd:** 2026-05-13 — F2-P9 D+A-batch

Utöka `GET /api/v1/job-ads` (auth-gated per ADR 0005) med:
- `?ssyk=<concept-id>` — JobTech occupation-concept-id filter
- `?region=<concept-id>` — JobTech location-concept-id filter
- `?q=<text>` — fritext-sökning på title+description
- Befintliga `?page`, `?pageSize`, `?sortBy` bibehållna

**Leverans (F2-P9):**

| Område | Innehåll |
|---|---|
| **CTO-rond (Q1-Q11)** | senior-cto-advisor 11 entydiga beslut mot principer (Beck YAGNI, Evans ACL, Martin REP, Knuth, OWASP API4:2023). 0 Klas-STOPP behövdes. |
| **Domain** | Oförändrad — Q1=B raw `string?` filter-params, ingen JobTech-taxonomi i Domain (Evans 2003 §14 ACL) |
| **Migration** | `F2P9JobAdSearchColumns` (`20260513111555_*`) — Postgres generated columns `ssyk_concept_id` + `region_concept_id` STORED + partial B-tree-index. Drift omöjlig (read-only). |
| **EF-config** | `JobAdConfiguration` shadow-properties via `HasComputedColumnSql(..., stored: true)` |
| **Application** | `ListJobAdsQuery` utvidgad med `Ssyk/Region/Q` (Q4=A optional params, OCP), validator regex `^[A-Za-z0-9_-]{1,32}$` + Q `MinLength(2).MaxLength(100)`, handler-filter via `EF.Property<string?>(j, "SsykConceptId")` + `EF.Functions.Like(.ToLower(), pattern)` |
| **Endpoint** | `RequireRateLimiting(ListReadPolicy)` på GET-routes (per CTO post-CTO-triage av security-auditor Major-fynd) |
| **Rate-limiting** | Ny `ListReadPolicy` (60/min per UserId-claim) — generisk för list/search-yta (Martin 2017 §13 REP). Skydd mot multi-query-DoS från komprometterat konto (OWASP API4:2023). |
| **Tester** | Validator: 31 nya cases (regex + length + null-bypass). Handler: 1 doc-test (filter testas integration-side). Integration: 13 filter-tester + 1 429-rate-limit-test mot dedikerad `ListReadRateLimitApiFactory` |

**Reviewers INLINE (CLAUDE.md §9.2):**

| Reviewer | Tidpunkt | Verdict |
|---|---|---|
| dotnet-architect | INNAN kod | Design-skiss, ~10 multi-approach → CTO |
| senior-cto-advisor | EFTER architect | 11 beslut entydigt mot principer. 0 Klas-STOPP |
| db-migration-writer | UNDER impl | Migration genererad (raw SQL → snapshot-sync till EF-fluent) |
| test-writer | EFTER impl | 31 validator + 13 integration + 1 doc-test |
| security-auditor | EFTER tests | APPROVED-WITH-CONDITIONS — 1 Major (rate-limit) → CTO-triage → in-block-fix |
| senior-cto-advisor (rond 2) | Triage auditor-Major | In-block-fix entydigt mot CLAUDE.md §9.6, OWASP API4. 0 Klas-STOPP. |
| code-reviewer | EFTER impl, INNAN commit | APPROVED. 0/0/2/2 (Minor: doc-test pattern, validation-meddelande svensk-format). Approve som-är. |

**Tester (full svit grön):**

- Domain.UnitTests: 225 (oförändrat)
- Application.UnitTests: 323 → **354** (+31)
- Architecture.Tests: 50 (oförändrat)
- Api.IntegrationTests: 240 → **254** (+14: 13 filter + 1 rate-limit)
- Worker.IntegrationTests: 26 (oförändrat)
- Migrate.UnitTests: 6 (oförändrat)

**Totalt backend: 870 → 915 grönt (+45 nya).**

**Web-search-källor (CLAUDE.md §9.5, verifierade 2026-05-13):**

- [JobTech JobSearch API v2](https://jobtechdev.se/en/components/jobsearch) — `occupation-concept-id`, `location-concept-id` hierarkiska taxonomi-strängar
- [Npgsql EF Core Full Text Search](https://www.npgsql.org/efcore/mapping/full-text-search.html) — `ToTsVector("swedish", ...).Matches(...)` för framtida tsvector-migration
- [PostgreSQL Generated Columns §5.3](https://www.postgresql.org/docs/current/ddl-generated-columns.html) — STORED-typ + indexerbar
- [Microsoft Learn — EF Core Computed Columns](https://learn.microsoft.com/en-us/ef/core/modeling/generated-properties) — `HasComputedColumnSql(stored: true)`
- [OWASP API Security Top 10 2023 — API4](https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/) — search/list-endpoints + wildcard-matching DoS

**TD-trigger-kandidater (lyfts EJ — CC pressade mot §9.6 vid CTO-rond):**

- Micro-ADR-amendment 0032 §10 "Derived columns från raw_payload" (om pattern återanvänds)
- tsvector + GIN för fulltext-search (trigger: Fas 3 UX-research för stemming ELLER prod-latens >100ms)
- `JobAdSearchIndex` CQRS read-model (trigger: >50k rader eller skriv/läs-skalning divergerar)
- DTO-utvidgning med Ssyk/Region per rad (trigger: UX visar behov)
- JobTech v2 proxy hybrid-search (Fas 3 dual-source-UX)
- Micro-ADR "när krävs Application-port för EF.Functions?" (om TD-73-precedens vs Q6-A behöver formaliseras)

**Cross-refs:**

- ADR 0032 (JobTech-integration + "Out of scope" TD-70-källa)
- ADR 0005 (auth-gated JobAd-katalog)
- TD-56 (paginering — föregående batch, F2-P7)
- senior-cto-advisor 2026-05-13 (Q1-Q11 + rate-limit-triage)
- security-auditor 2026-05-13 (Major + CTO-triage)
- OWASP API4:2023 Unrestricted Resource Consumption

**Operativ konsekvens:** v0.2-prod-tag är inte längre gated på TD-70. JobAd-katalog har search/filter-yta för end-users (frontend integrering i v0.2.x-patch).



---

## TD-80: JobAd.Url scheme-whitelist (http/https) i Domain.ValidateInputs ✓ STÄNGD 2026-05-13
**Kategori:** Säkerhet / XSS-defense
**Severity:** Major
**Fas:** 2
**Källa:** security-auditor F2-P10 frontend-review 2026-05-13 (Blocker → split: FE in-block, BE TD per §9.6 punkt 1 "annan fas")
**Stängd:** 2026-05-13 — backend Domain-tightening i samma CC-session som F2-P10 (Klas-GO efter TD-80-rek)

`JobAd.ValidateInputs` (`src/JobbPilot.Domain/JobAds/JobAd.cs:171`) använde
`Uri.TryCreate(url, UriKind.Absolute, out _)` som accepterar:

- `javascript:alert(1)` → XSS vid render i autentiserad session
- `data:text/html,<script>...</script>` → XSS
- `vbscript:msgbox(1)` → legacy XSS (IE-rester)
- `file:///etc/passwd` → path-disclosure-yta
- `ftp:`/`gopher:` → exfiltrations-vektorer

Vid render via `<a href={jobAd.url}>` blir klick = JS-exekvering i
autentiserad session-kontext (cookies, fetches mot `/api/v1/*`). Cookie-stöld
är GDPR Art. 32-överträdelse (säker behandling).

**FE-mitigering vid F2-P10 (commit 70e1505):** `jobAdDtoSchema.url` har Zod
refine `/^https?:\/\//i.test(u)` — DTO-parse misslyckas vid icke-http(s) →
`kind: "error"` → ingen render. Defense-in-depth FE-side.

**BE-fix (denna stängning):** `ValidateCore` utvidgad till:

```csharp
if (string.IsNullOrWhiteSpace(url)
    || !Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
    || (parsedUri.Scheme != Uri.UriSchemeHttp
        && parsedUri.Scheme != Uri.UriSchemeHttps))
    return Result.Failure(
        DomainError.Validation("JobAd.UrlInvalid",
            "URL måste vara en giltig http(s)-URL."));
```

`Uri.UriSchemeHttp`/`UriSchemeHttps`-konstanter (typsäkert + case-insensitive
via Uri-klassens normalisering). Whitelist (default-deny) > blacklist per
Saltzer/Schroeder 1975 + OWASP A01:2021. Same `JobAd.UrlInvalid`-error-code
(API-bakåtkompatibilitet).

**Skydd genom alla 3 entry-points:** `Create` (Manual admin-flöde), `Import`
(JobTech upsert-flöde), `UpdateFromSource` (JobTech Stream-update). Alla delar
`ValidateCore`. `UpsertExternalJobAdCommandHandler` hanterar Import-failure
via befintlig `Skipped`-outcome + `LogSkippedValidation` — ingen schadlig URL
persisteras från JobTech-payload.

**Migrations:** Ingen schema-ändring krävs. Domain-validering är vid persist,
inte vid load — befintliga rader påverkas inte. Risk för existing rader med
non-http(s) URL bedömd noll: backend `Manual`-källa har strikt CreateJobAd-
form i admin (skulle ha krävts att admin medvetet skrev javascript:); JobTech
har aldrig observerats returnera non-http(s) URLs i payload.

**Tester (TDD, FIRST):** 4 nya `[Theory]`-metoder med 13 InlineData-cases:
- `Create_WithHttpOrHttpsUrl_ReturnsSuccess` (4 cases — http/https + uppercase)
- `Create_WithNonHttpScheme_ReturnsUrlInvalid` (7 cases — javascript/JAVASCRIPT/data/vbscript/file/ftp/gopher)
- `Import_WithNonHttpScheme_ReturnsUrlInvalid` (4 cases — primär palett)
- `UpdateFromSource_WithNonHttpScheme_ReturnsUrlInvalid` (2 cases + bevarar original-URL post-fail)

**Backend full svit grön:** Domain 242 (+17) + Application 354 + Architecture
50 + Api Integration 254 + Worker Integration 26 + Migrate 6 = **932 grönt**.

**Reviewers INLINE:** security-auditor (re-audit av egen Blocker), code-reviewer.

**Cross-refs:**
- security-auditor F2-P10 frontend-review 2026-05-13 (lyfte TD-80)
- F2-P10 commit `70e1505` (FE Zod-refine + TD-80-lyft)
- ADR 0032 §4 (JobAd.Import + UpdateFromSource)
- OWASP A01:2021 Broken Access Control / Saltzer-Schroeder 1975 default-deny

**Operativ konsekvens:** v0.2-prod-tag är inte längre gated på TD-80. Defense-
in-depth uppfylld FE + BE. Fas 2 Major-sektionen krymper till TD-13 (PII-
encryption, kvarstår per CTO-decision Q5 i D+A-session 2026-05-13 — defer Fas
2-stängning per EDPB CEF 2025).
