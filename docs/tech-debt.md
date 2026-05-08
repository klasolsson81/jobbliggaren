# Tech Debt — JobbPilot

Items deferrade från reviews och arkitektur-flaggor. Adressering
planerad till Fas 1 om inte annat anges. Källa och severity
dokumenteras för spårning.

ID-konvention: TD-{nummer}. Items hänvisas till via ID i framtida
ADR:er, PR-beskrivningar och commits.

---

## TD-1: Skip-link saknas i (app)-layout
**Kategori:** Accessibility (WCAG 2.4.1 Bypass Blocks)
**Severity:** Minor
**Källa:** design-reviewer, 2026-05-07 (Turn 2)

`src/app/(app)/layout.tsx` har `<header>` och `<main>` men ingen
"Skip to main content"-länk. Tangentbordsanvändare måste tabba
igenom hela headern på varje sida.

**Föreslagen åtgärd:** Lägg till `<a href="#main">Hoppa till
huvudinnehåll</a>` som första element i layout-body, dolt visuellt
men synligt vid tangentbordsfokus (`sr-only focus:not-sr-only`).
Tagga `<main id="main">`.

---

## TD-2: CardTitle renderas utan heading-tag
**Kategori:** Accessibility (heading-hierarki)
**Severity:** Minor
**Källa:** design-reviewer, 2026-05-07 (Turn 2)

Shadcn `CardTitle` renderar default som `<div>`. Heading-trädet på
/mig blir därmed `<h1>` ("Min profil") följt av `<div>`
("Kontoinformation") — bryter h1→h2-hierarki som skärmläsare
förlitar sig på.

**Föreslagen åtgärd:** Passa `as="h2"`-prop om shadcn-versionen
stödjer det, annars wrap manuellt med `<h2>` eller customize
CardTitle-komponenten centralt.

---

## TD-3: Tom-state-copy "Inga roller tilldelade" saknar next-action
**Kategori:** UX
**Severity:** Minor
**Källa:** design-reviewer, 2026-05-07 (Turn 2)

På /mig visas "Inga roller tilldelade" om `user.roles` är tom array.
Användaren får ingen vägledning om vad det betyder eller om de
behöver agera.

**Föreslagen åtgärd:** Antingen (a) gör tom-state stum — visa inte
fältet alls om listan är tom — eller (b) ge context: "Inga roller
tilldelade än — kontakta support om du förväntade dig roller här."
Beslut hör hemma i Fas 1 UX-pass när roles-konceptet konkretiseras
produktmässigt.

---

## TD-4: userId visas i UI utan tydligt användarbehov
**Kategori:** UX / Privacy hygiene
**Severity:** Minor
**Källa:** security-auditor, 2026-05-07 (Turn 2)

mig/page.tsx visar `user.userId` (Guid) som första fält. Slutanvändare
har inget direkt behov av Guid. Möjligt support-värde — men då bör
syftet kommuniceras tydligt ("Support-id för felanmälningar").

**Föreslagen åtgärd:** Antingen ta bort fältet ur UI eller omformulera
label så syftet är klart. Beslut i Fas 1 UX-pass.

---

## TD-5: Redundant getServerSession-anrop på /mig
**Kategori:** Code hygiene
**Severity:** Minor
**Källa:** security-auditor, 2026-05-07 (Turn 2)

Både (app)/layout.tsx och mig/page.tsx anropar getServerSession().
Funktionellt OK — funktionen är `React.cache()`-ad så andra anropet
träffar cache. Men kodflödet är otydligare än nödvändigt.

**Föreslagen åtgärd:** Förmodligen acceptera duplikationen som
dokumenterad pattern (cache är billig, läsbarhet vinner) snarare
än att fixa. Alternativ: refaktorera så att layout passerar `user`
via context eller layout-prop. Inte trivialt i Server Components
— pragmatiskt rätt är troligen "no-op + dokumentera pattern".

---

## TD-6: Logout-backend-call utan fel-loggning
**Kategori:** Observability
**Severity:** Minor
**Källa:** security-auditor, 2026-05-07 (Turn 2)

`logoutAction` anropar backend `/auth/logout`. Om anropet misslyckas
(network, 500) raderas cookien lokalt och användaren redirectas —
men backend-session blir kvar i Redis tills TTL.

**Föreslagen åtgärd:** Lägg till strukturerad loggning vid logout-fel.
Övervägning för Fas 1: ska klienten retry:a, eller är "best-effort
logout"-semantik acceptabel? Beslut beror på threat-model.

---

## TD-7: Zod runtime-validering för DTOs från backend
**Kategori:** Type safety / Architecture
**Severity:** Major (latent)
**Källa:** security-auditor, 2026-05-07 (Turn 2) — extension

Frontend tar emot DTOs från backend och deklarerar matchande
TypeScript-typer manuellt. `tsc` verifierar inte att backend
faktiskt returnerar det typen säger — mismatch sker tyst (det var
Major 1 i Turn 2: `roles?: string[]` vs `IReadOnlyList<string>`).

**Föreslagen åtgärd:** Introducera Zod-schema per DTO i frontend
(t.ex. `lib/dto/current-user.ts`). `getServerSession()` och andra
backend-konsumtioner validerar via schema vid `res.json()`. Vid
mismatch: throw + log. Schema bör genereras från backend OpenAPI-
spec eller en delad source of truth om sådan etableras.

Egen ADR krävs (placeholder: **ADR 0020**) eftersom valet påverkar
samtliga frontend-DTO-konsumtioner.

---

## TD-8: GetPipeline saknar fullständig paginering
**Kategori:** Scalability
**Severity:** Minor
**Källa:** code-reviewer, STEG 5 (2026-05-07)

`GetPipelineQueryHandler` läser alla ansökningar per användare i ett anrop.
Pipeline är en kanban-vy designad att visa hela stadiet — traditionell
paginering motverkar det UX-målet. Nuvarande lösning: hård övre gräns
`.Take(500)` som skyddsventil.

**Föreslagen åtgärd:** Ingen förändring föreslagen inom överskådlig tid,
men om en användare någonsin når hundratals aktiva ansökningar behöver
pipeline UI:t designas om (virtualisering, lazy-load per status). Beslut
i det skedet.

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

### TD-10 — PII-läckage via `body?.detail` i Server Actions

**Kategori:** Säkerhet  
**Fas:** 0 (nu, web)  
**Prioritet:** Hög  
**Källa:** Security audit 2026-05-08 (Major 1, öppen)

Server Actions i `src/lib/actions/applications.ts` exponerar `body?.detail`
direkt till UI-lagret. Beroende på hur backend formaterar feldetaljer kan
känslig intern information (stacktraces, SQL-felmeddelanden, användardata)
läcka till klientens felmeddelande.

**Risk:** PII eller interna systemdetaljer visas för användaren — bryter GDPR
Art. 5(1)(f) om integritet och konfidentialitet.

**Föreslagen åtgärd:** Ersätt `body?.detail ?? "Okänt fel."` med ett
whitelistat-felmeddelande. Tillåt bara förväntade HTTP-statuskoder att
mappas till specifika svenska felmeddelanden — allt annat returnerar
ett generiskt "Något gick fel. Försök igen." utan interna detaljer.

---

### TD-11 — Hårdkodad E2E-lösenord och testemail på produktionsdomän

**Kategori:** Säkerhet  
**Fas:** 0 (nu, web/e2e)  
**Prioritet:** Medium  
**Källa:** Security audit 2026-05-08 (Major 3, öppen)

`tests/e2e/helpers/auth.ts` innehåller hårdkodat lösenord `TestPassword123!`
och genererar testmail på `@jobbpilot.se` (produktionsdomän). E2E-testkonton
skapas mot produktionsdatabasen vid pipeline-körning om miljövariabler inte
separeras tydligt.

**Risk:** Testanvändare hamnar i produktionsdatabasen om E2E körs mot fel
miljö; lösenordet är läsbart i klartext i repot.

**Föreslagen åtgärd:** (1) Flytta lösenord till `TEST_USER_PASSWORD`
miljövariabel i `.env.test`. (2) Ändra testdomain till `@test.jobbpilot.internal`
eller liknande non-resolvable domän. (3) Lägg guard i `ensureTestUser` som
validerar att `PLAYWRIGHT_BASE_URL` innehåller `localhost` eller `staging`.

---

### TD-12 — Saknad integration-test för cross-user isolation

**Kategori:** Säkerhet / Test  
**Fas:** 0 (backend)  
**Prioritet:** Medium  
**Källa:** STEG 5 discovery 2026-05-08

Queries och commands för Application-aggregatet filtrerar korrekt på
`a.JobSeekerId == jobSeekerId` — user A kan inte se eller mutera user B:s
ansökningar. Men detta beteende saknar ett integration-test som verifierar
det explicit.

**Risk:** Om filtret tas bort eller refaktoreras bort i framtiden fångas
det inte av testerna förrän manuellt verifierat.

**Föreslagen åtgärd:** Lägg till ett integration-test i `ApplicationsTests.cs`:
två separata användare registreras, user A skapar en ansökan, user B försöker
`GET /{id}` och `POST /{id}/transition` — förväntat utfall: 404 resp. 404.

---

### TD-13 — Encryption av PII-kolumner (Fas 2)

**Kategori:** Säkerhet / GDPR
**Fas:** 2 (after Fas 1 milestone)
**Prioritet:** Hög
**Källa:** Security audit STEG 7a 2026-05-08 (Major M1) + befintliga TODOs i ApplicationConfiguration

Flera kolumner lagrar PII-känsligt innehåll (BUILD.md §13.1 "Känsligt") som klartext-JSONB/TEXT
i Postgres. RDS har AES-256 disk-encryption via KMS, men app-side envelope encryption saknas.

Berörda kolumner:
- `applications.cover_letter` (TEXT)
- `application_notes.content` (TEXT)
- `follow_ups.note` (TEXT)
- `resume_versions.content` (JSONB) — innehåller `PersonalInfo`, `Experiences`, `Educations`, `Skills`

**Risk:** vid backup-läckage, snapshot-export eller intern obehörig DB-access exponeras PII
i klartext. RDS-disk-encryption skyddar bara mot fysisk stöld av disk.

**Föreslagen åtgärd:** Implementera KMS-backed `ValueConverter<T, string>` med envelope
encryption (DEK per rad eller per aggregate). Migration är icke-destruktiv (encrypt-on-write,
decrypt-on-read; befintliga klartext-rader migreras lazy vid nästa write eller via
back-fill-job). Designval och nyckel-rotationsstrategi får egen ADR i Fas 2.

**Övervägning — cryptographic erasure för Art. 17-tillämpning på backups:**
Standardpraxis för GDPR Art. 17 är att radera från live-system + dokumentera att RDS
automated backups (default 7d, max 35d) skrivs över naturligt — bekräftad acceptabel av
EDPB CEF 2025-rapporten (2026-02). Crypto-erasure-pattern (per-user data encryption key,
DEK kastas vid kontoradering → backups blir omedelbart olesbara) är ett alternativ som
kan bakas in i Fas 2-impl. Tradeoff: extra komplexitet i restore-flöden + key-rotation,
men ger omedelbar Art. 17-täckning av backup-data. ADR i Fas 2 ska ta ställning.

---

### TD-14 — DeleteResumeVersion: VersionInUse-check är inaktiv tills Application får ResumeVersionId

**Kategori:** Säkerhet / Data integrity
**Fas:** 4 (AI Layer)
**Prioritet:** Medium
**Källa:** Code review STEG 7a 2026-05-08 (N8) + dotnet-architect design-validering

`DeleteResumeVersionCommandHandler` (`src/JobbPilot.Application/Resumes/Commands/DeleteResumeVersion/`)
hårdkodar `isReferencedByOpenApplication = false`. Domänen `Resume.DeleteVersion` har redan
checken implementerad — handlern saknar bara databas-uppslaget.

I Fas 1 kan ingen Tailored-version skapas (BUILD.md §18 milstolpe "manuell CV") och
Application-aggregatet har ingen `ResumeVersionId`-referens ännu, så funktionellt sett är
checken icke-applicerbar. Master-versionen blockas separat via egen invariant
(`Resume.MasterCannotBeDeleted`).

**Risk i Fas 1:** noll (ingen kod-väg där en refererad version kan raderas).

**Föreslagen åtgärd vid Fas 4:** När `Application.ResumeVersionId` införs (BUILD.md §5.3):

1. Uppdatera `DeleteResumeVersionCommandHandler` så att `isReferencedByOpenApplication`
   beräknas via `db.Applications.AnyAsync(a => a.ResumeVersionId == versionId &&
   !a.Status.IsTerminal, ct)`.
2. Eller introducera dedikerad domän-port `IResumeVersionUsageChecker` för testbarhet.

Exempel-test (idag inaktiv): `DeleteResumeVersion_WhenTailoredVersionReferencedByOpenApplication_ReturnsVersionInUse`.

---

### TD-15 — Resume-formulär: koppla Zod-issue path till `aria-invalid` per fält

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

### TD-17 — Hangfire prod-härdning (multi-faceted, blocker för Fas 1 prod-deploy)

**Kategori:** Säkerhet / Operations
**Fas:** 1 (innan prod-deploy)
**Prioritet:** Hög (blocker för Fas 1 prod-deploy, tillsammans med TD-16)
**Källa:** Security audit STEG 9 2026-05-08 (MAJ-1, MAJ-2, MIN-3, MIN-4) + ADR 0023

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

### TD-18 — Stale-detektering: utökning till intervju-states

**Kategori:** UX / Domain-modell
**Fas:** 1+ (vid första rapporterade fall)
**Prioritet:** Låg (idag); Medium (när första fall rapporteras)
**Källa:** ADR 0023 §"Definition of stale" (Klas tillägg #2 till STEG 9)

ADR 0023 låser stale-detektering till snäv `{Submitted, Acknowledged}` för Fas 1. Definition: "transient-states där företaget förväntas svara". Intervju-states (`InterviewScheduled`, `Interviewing`) betraktas active oavsett kalendertid — antagandet är att användaren själv hanterar intervju-flödet och inte vill att jobbet auto-ghostas.

**Risk i Fas 1:** noll (bara om användaren själv missar att markera "intervju ställdes in" och appen fastnar i `InterviewScheduled`-state utan vidare aktivitet).

**Trigger för utökning:** Första rapporterade fallet av "intervju ställdes in, app fastnade i InterviewScheduled/Interviewing utan auto-progression".

**Föreslagen åtgärd vid trigger:**
1. Utöka `StaleApplicationSpecification.CandidateStatusFilter()` till att inkludera `InterviewScheduled` + `Interviewing` (eventuellt med längre `GhostedThresholdDays` per app/state)
2. Eller: dedikerad `IsInterviewStaleNow(...)`-metod med separata thresholds (t.ex. 60 dagar för intervju-states vs 21 för Submitted/Acknowledged)
3. Uppdatera ADR 0023 med "Definition of stale v2"-sektion
4. Uppdatera DetectGhostedApplicationsJob-tester med nya scenarios

**Inga nya migrations** — fältet `GhostedThresholdDays` finns redan per app.

---

### TD-19 — Worker orchestrator + DI-pattern: defense-in-depth-förbättringar

**Kategori:** Code quality / Robusthet
**Fas:** 2 (när Worker-jobb-yta växer)
**Prioritet:** Medium
**Källa:** Code review STEG 9 2026-05-08 (M1, M5, M6) + Security audit (MIN-1)

Tre defensive förbättringar som inte blockerar STEG 9 men bör adresseras när Worker-jobb-yta växer (Fas 2 JobTech-sync, Fas 4 AI-jobb):

**M5 — Max-batch-size-guard i `DetectGhostedApplicationsJob`:**

Per-id `mediator.Send`-loop är medvetet val (audit-paritet, isolering). Acceptabel för Fas 1-volym (50–100 stale/dag). Men vid oväntad scale-stigning (t.ex. backfill efter migration-bug eller ovanligt långt downtime) kan loop:en köra över tusentals apps.

**Åtgärd:** Lägg explicit guard i `RunAsync(...)`:

```csharp
const int MaxBatchSize = 500;
if (staleIds.Count > MaxBatchSize)
{
    logger.LogWarning(
        "DetectGhostedApplicationsJob: oväntat stort kandidat-set ({Count}). Avbryter — manuell granskning krävs.",
        staleIds.Count);
    return;
}
```

Hangfire-dashboard kommer att visa jobbet som "Succeeded" med warning-logg → kräver manuell triage.

**M1 — Smoke-test för correlation-ID-unikhet:**

`DetectGhostedApplicationsJobIntegrationTests` verifierar audit-paritet (audit-rad skapas) men inte att `correlation_id` är unikt per Hangfire-job-execution. Efter K1-fixen (instans-fält Guid i `WorkerCorrelationIdProvider`) är detta värt regression-skydd.

**Åtgärd:** Lägg till test som dispatchar två separata MarkGhostedCommand i två olika scope:s och verifierar att audit-raderna har **olika** `correlation_id`. Och samma command i samma scope → samma `correlation_id`.

**M6 + MIN-1 — Architecture-test-utökning:**

`WorkerLayerTests.cs` förbjuder Worker-beroende på `Microsoft.AspNetCore.Http`/`Identity`/`Authentication.JwtBearer`/`Identity.EntityFrameworkCore`. Listan är defensiv men inte uttömmande:
- `Microsoft.AspNetCore.Authentication.Cookies`
- `Microsoft.AspNetCore.Authorization`
- `Microsoft.AspNetCore.Hosting`

**Åtgärd:** Antingen utöka explicit lista eller flippa till **allow-list-pattern**: "Worker får INTE bero på `Microsoft.AspNetCore.*`". Tradeoff: arch-test blir mindre brittle på vissa NuGet-uppdateringar men kan ge falska positiver.

Plus: arch-test som verifierar att alla `IAuditableCommand`-impls antingen har `IAuthenticatedRequest` eller dokumenterar avsiktlig avsaknad i XML-doc (regression-skydd för `MarkGhostedCommand`-mönstret).

**Beroenden:** Ingen — kan adresseras opportunistiskt vid Fas 2 Worker-jobb-tillägg.

---

### TD-20 — `AuditPartitionMaintainer.DropPartitionsOlderThanAsync`: SqlQueryRaw + format-string-escape (defensiv refactor)

**Kategori:** Code quality / Robusthet
**Fas:** 1+ (defensiv förbättring)
**Prioritet:** Låg
**Källa:** Code review STEG 10.6 2026-05-08 (M2)

`AuditPartitionMaintainer.DropPartitionsOlderThanAsync` använder
`SqlQueryRaw<string>` med `string.Format`-syntax. Regex-quantifier `{8}`
i pattern `^audit_log_[0-9]{8}$` måste escapas till `{{8}}` för att
inte tolkas som format-placeholder argument 8 (vilket failer run-time
med `FormatException`). Buggen fångades av smoke-test i 10.6.

**Risk:** Tyst run-time-fall vid framtida regex-justering (t.ex. om
`{1,8}` läggs till) eller om en till parameter-position introduceras.
`dotnet build` flaggar inte format-string-mismatch.

**Föreslagen åtgärd:** Migrera till `SqlQuery<T>` med `FormattableString`-
overload. Då escapas curly braces inte (regex blir verbatim) och
parametrar binds som SQL-parameters automatiskt.

Försök gjordes i 10.6: `SqlQuery<string>` returnerade tom rad-set mot
`pg_class.relname`-kolumnen (sannolikt EF Core 10 shadow-projection-issue
mot `name`-typen, inte `text`). Möjlig workaround: casta i SQL via
`c.relname::text AS "Value"`. Behöver verifieras + smoke-test-pass innan
landning.

**Risk i Fas 1:** noll (smoke-test täcker den primära kodvägen och fångar
regression).

**Beroenden:** Ingen — kan adresseras opportunistiskt vid touch på
`AuditPartitionMaintainer` eller om EF Core-versionsuppdatering ändrar
`SqlQuery<T>`-projection-beteende.

---

### TD-21 — Rate-limiting på DELETE /me + andra känsliga auth-endpoints

**Kategori:** Säkerhet / DoS-skydd
**Fas:** 1 (innan prod-deploy)
**Prioritet:** Hög (blocker för Fas 1 prod-deploy)
**Källa:** Security audit STEG 10b 2026-05-08 (Major-2)

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

### TD-22 — App-logg-retention + IP/UA-redaction

**Kategori:** Säkerhet / GDPR / Data retention
**Fas:** 1 (innan prod-deploy)
**Prioritet:** Hög (urholkar Art. 17-anonymisering annars)
**Källa:** Security audit STEG 10b 2026-05-08 (Major-3)

Audit-tabellen anonymiseras efter Art. 17 (user_id/ip/user_agent → NULL
efter 30 dagars restore-fönster). MEN app-loggen (CloudWatch sink i prod,
Seq i dev) innehåller IP + UA + email-hash vid login-failures, oberoende
av audit-tabellen. Även efter Art. 17-anonymisering kan en angripare
re-identifiera användare via app-logg-data.

Kombinationen email-hash + IP + UA är **pseudonym** (GDPR Art. 4(5)).
Samma user kan korreleras över tid via app-loggen även efter audit-
anonymisering.

**Risk i Fas 1:** noll (dev-loggar, ingen prod-data).
**Risk vid Fas 1 prod-deploy:** Major (Art. 32 + Art. 5(1)(c)).

**Föreslagen åtgärd:**
1. **Policy-beslut (Klas):** retention för app-loggar (≤ 30 dagar
   rekommenderat för PII-konsistens med Art. 17-fönstret)
2. **CloudWatch retention** sätts till 30 dagar via AWS-konfig
3. **Seq lokal dev**: ingen retention nödvändig (utvecklarmiljö)
4. **HashEmail** kan omvandlas till HMAC med roterande nyckel istället för
   rå SHA-256 (mindre korrelerbart men kostnaden är ops-komplexitet)
5. **ADR-tillägg** till ADR 0024 om kopplingen mellan audit-anonymisering
   och app-logg-retention

**Beroenden:** AWS CloudWatch-konfig vid första prod-deploy.

---

### TD-23 — RedisSessionStore atomicitet via MULTI/EXEC eller Lua-script

**Kategori:** Säkerhet / Robusthet
**Fas:** 2 (efter MVP)
**Prioritet:** Medium
**Källa:** Code review STEG 10b 2026-05-08 (Code-Nit-3) +
Security audit STEG 10b 2026-05-08 (Sec-Minor-3)

`RedisSessionStore.CreateAsync` gör SADD secondary-index → SET main-key i
två separata Redis-anrop. Om första lyckas men andra failar kvarstår
orphan-membership i secondary-set (no-op vid InvalidateAllForUserAsync).
Mitigerat i STEG 10b genom att vända ordningen (SADD-först), men full
atomicitet kräver MULTI/EXEC eller Lua-script.

**Risk i Fas 1:** mycket låg. Worst-case är harmless orphan-membership i
secondary-set som plockas upp via TTL.

**Föreslagen åtgärd vid Fas 2:**
1. Migrera CreateAsync + InvalidateAsync till `IBatch` (StackExchange.Redis)
   eller Lua-script för atomisk multi-key-operation
2. Eventuellt: använd Redis Streams istället för secondary-set för
   bättre observability vid DELETE-cascade

**Beroenden:** Ingen — opportunistiskt vid touch på RedisSessionStore.

---

### TD-24 — DeleteAccountCommand cascade-paginering vid power-user

**Kategori:** Skalbarhet / DoS-skydd
**Fas:** 2 (vid första rapporterad prestanda-incident)
**Prioritet:** Låg (idag), Medium (vid skala)
**Källa:** Security audit STEG 10b 2026-05-08 (Sec-Minor-1)

`DeleteAccountCommandHandler` laddar `db.Applications.ToListAsync()` +
`db.Resumes.Include(r => r.Versions).ToListAsync()` utan paginering.
För en power user med 10 000 applications laddas hela trädet i minnet
i en request-thread.

**Risk i Fas 1:** noll (få users, små portföljer).
**Risk vid Fas 4-volym:** Medium (DoS-vektor).

**Föreslagen åtgärd:**
1. Paginerings-loop: hämta + soft-delete batches om 500 applications/resumes
2. Eller: utför soft-delete via direct UPDATE-SQL (audit-bypass-mönstret)
   istället för att ladda + mutera + spara via EF
3. Behåll JobSeeker.SoftDelete via aggregate-mutation (rotaggregatet är
   alltid en rad)

**Beroenden:** Behöver paginering-mönster verifieras mot audit-paritet —
ska EN audit-rad skrivas (Account.Deleted) eller flera (per batch)?
Rek: en rad. Audit-rad skrivs sist, efter alla cascade-batches.

---

### TD-25 — HardDeleteAccountsJob per-konto try/catch (resilient loop)

**Kategori:** Robusthet / Operations
**Fas:** 1+ (opportunistiskt)
**Prioritet:** Medium
**Källa:** Code review STEG 10b 2026-05-08 (Code-Nit-5)

`HardDeleteAccountsJob.RunAsync` har ingen try/catch per konto i Steg 2-loopen.
Vid första exception bubblar den och avbryter loopen för **alla** efterföljande
konton. Hangfire retry:ar hela jobbet, men under retry-fönstret är de andra
moget-för-deletion-kontona också blockerade.

**Risk i Fas 1:** låg (få konton att hard-deleta).
**Risk vid skala:** medium (en korrupt rad blockerar alla andra).

**Föreslagen åtgärd:**
1. Lägg per-konto try/catch som loggar fel och `continue`:ar
2. Konsekvens-checka mot DetectGhostedApplicationsJob (ADR 0023)-mönstret —
   om DetectGhosted också låter exception bubbla bör båda förändras tillsammans
   för konsistens
3. Eventuellt: aggregera failed-id:s och rapportera vid slutet av jobbet

**Exempel:**
```csharp
foreach (var jobSeekerId in jobSeekerIds)
{
    cancellationToken.ThrowIfCancellationRequested();
    try
    {
        await hardDeleter.HardDeleteAccountAsync(jobSeekerId, cancellationToken);
        processed++;
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        LogAccountFailed(logger, jobSeekerId, ex);
    }
}
```

**Beroenden:** Konsekvens-decision om DetectGhosted-mönstret också ska
ändras. Diskutera vid Fas 2-touch.

---

### TD-26 — AI-kostnadstak: token/tecken-limit pre-Bedrock + per-user spend-cap

**Kategori:** Säkerhet / DoS-skydd / Kostnad
**Fas:** 4 (när AI-Layer byggs)
**Prioritet:** Hög (innan AI-features släpps till slutanvändare)
**Källa:** Extern review-input 2026-05-08

BUILD.md §8.3 specar fritt-tier "50 AI-operationer per kalendermånad per
användare". Operation som mätenhet är otillräckligt: en 1-sidig CV-extraktion
kostar bråkdel jämfört med 25-sidig PDF med ostrukturerad text — Anthropic
prissätter per token, inte per anrop. 50 "operationer" kan drevas till
mass-poison-pill-attacker där varje anrop maximerar input-context.

**Risk i Fas 1:** noll (inga AI-features byggda).
**Risk vid Fas 4-launch:** kostnads-DoS + budget-blowout.

**Föreslagen åtgärd (Fas 4):**

1. **Pre-Bedrock token/tecken-limit i C#-lagret:** efter PDF-extraktion via
   PdfPig (BUILD.md §3.1), validera total-input-tecken < t.ex. 15 000 (≈
   3 750 input-tokens). Vid överskott: kasta `ValidationException("Filen
   är för stor för att analyseras — komprimera CV:t eller lägg till egen
   API-nyckel via BYOK")`. Kommunicera limit i UI.

2. **Per-user kostnadstak (USD/månad):** komplettera operation-räknare
   med running-cost-summa från `ai_operations.tokens_used × pris-per-modell`.
   När user når t.ex. 95% av budget: stoppa nya AI-anrop, visa varning.
   Hård cut vid 100%.

3. **Token-tracking-pipeline-behavior:** AI-commands som `IAiOperation`-
   marker triggar centraliserad token + cost logging post-call (analog
   med AuditBehavior).

4. **Budget-alert via AWS CloudWatch:** alarm vid daglig spend > X USD
   (Klas-konfigurerbart). Manuell ops-procedur i runbook.

5. **BYOK-användare** påverkas inte av tak — egen budget gäller (BUILD.md
   §13.2).

**Beroenden:** Fas 4 AI-Layer + ADR för IAiProvider-port. Skapa egen ADR
för cost-cap-design när AI-features designas.

---

## Adresseringsstrategi

- Items i kategorierna a11y, UX och observability adresseras
  gruppvis i Fas 1 i dedikerade passes (ett a11y-pass, ett
  UX-pass, ett observability-pass).
- TD-7 (Zod) får egen ADR och egen implementations-fas — den
  arkitekturella payoffen är hög och förändringen rör många filer.
- TD-5 utvärderas vid första touch — kan landas som "no-op,
  dokumentera" i layout-kommentar utan separat fas.
- Vid touch på berörda filer i andra ärenden: addressera relevanta
  TD-items opportunistiskt om scope tillåter.