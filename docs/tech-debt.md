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

### TD-28 — Frontend typed-confirmation-UX + re-auth-prompt på DELETE /me

**Kategori:** UX / Säkerhet (defense-in-depth)
**Fas:** 1 (frontend)
**Prioritet:** Medium
**Källa:** TD-21 ursprungs-Major-2 punkt 3+4 (defererad från STEG 11)

Backend-rate-limit (1 req/60s per user) är hard ceiling, men UX:en på frontend
ska göra ett misstag — eller en kompromettera-session-attacker — verkligen
medvetet. Två kompletterande UX-skydd:

1. **Typed-confirmation:** modal som kräver att användaren skriver ordet
   "RADERA" (eller email-adress) innan submit-knappen aktiveras. Civic-
   utility-ton enligt DESIGN.md — ingen rosa text, ingen "Hoppsan!"
2. **Re-auth-prompt:** lösenordsfält som måste fyllas i innan DELETE /me-
   anropet skickas. Backend kan validera via `IUserAccountService.ValidateCredentialsAsync`.
   Skyddar mot kompromettera-session-radera-konto-attack (angripare med
   stulen cookie kan inte radera utan lösenord).

**Risk i Fas 1 (utan TD-28):** låg — backend-rate-limit räcker som hard
floor. Men UX:en idag är "klicka, ångra dig, för sent" → impulsivt klick
kan radera konto.

**Föreslagen åtgärd:**
1. Komponent `<DeleteAccountModal>` i `src/components/me/` med RHF + Zod
   typed-confirmation
2. Server Action `deleteAccountAction` i `src/lib/actions/me.ts` som tar
   email + lösenord, validerar credentials via `/auth/login`-anrop först,
   sen DELETE /me
3. Tester: Vitest + Playwright E2E för knapp-aktivering + 429-respons-mappning

**Beroenden:** Frontend STEG-7b mönster (Server Actions + Zod). Inga
backend-ändringar krävs.

---

### TD-27 — EmailHash → HMAC med roterande nyckel (Fas 2)

**Kategori:** Säkerhet / GDPR
**Fas:** 2
**Prioritet:** Medium
**Källa:** ADR 0024 D7 deferral 2026-05-08

`LoginCommandHandler.HashEmail` använder rå SHA-256 (`Convert.ToHexString`
av lower-cased email). Determinism gör hash-värdet korrelerbart över tid:
samma email → samma hash, så app-loggen visar bestående pseudonym även
efter Art. 17-anonymisering av audit-tabellen.

HMAC med roterande nyckel bryter korrelationen — varje rotation gör
historiska hashar omöjliga att matcha mot framtida login-events. Men
kräver:
1. KMS-baserat nyckel-arkiv (för att verifiera historiska hashar vid
   restore eller audit-export)
2. Rotations-strategi (kvartal/halvår)
3. Migration av befintliga `EmailHash`-värden i logg/audit (oklart om
   möjligt — om rotation sker kan gamla hashar inte längre tolkas)

**Risk i Fas 1:** mitigerad av 30d CloudWatch-retention (TD-22 D7).
**Risk i Fas 2:** medium (utökad audit-yta + AI-jobb-loggar ger längre
korrelations-fönster).

**Föreslagen åtgärd vid Fas 2:**
1. Bunta ihop med TD-13 (PII-encryption) — båda kräver KMS + envelope-
   encryption-mönster. Egen ADR.
2. Statisk HMAC-key från Secrets Manager som Fas 2 bas-nivå (ingen
   rotation än) — räcker för att blockera dictionary-attack mot
   email-domain-rymden
3. Full nyckel-rotation defererad till när KMS-key-rotation-mönstret
   är etablerat (Fas 4+)

**Beroenden:** TD-13 (KMS-integration). Bör adresseras tillsammans.

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

## TD-29: Strict readiness-probe vid Fas 2 — separera liveness från readiness
**Kategori:** Observability / Deployment hygiene
**Severity:** Minor
**Källa:** dotnet-architect, STEG 13b review (2026-05-09)

`/api/ready`-endpoint i `src/JobbPilot.Api/Program.cs:128` returnerar 200 OK
utan DB/Redis-ping → namnet "ready" är missvisande. Det är liveness, inte
readiness i Kubernetes-konventions-mening. Konsekvens: ALB target-group
registrerar tasken som "healthy" innan `AppDbContext` är användbar — under
EF Core cold-start kan första requests få 500.

För Fas 0/MVP räcker liveness (BUILD.md §15.4 säger inte explicit "readiness
inkluderar DB"). Vid Fas 2 trafikvolym behövs strict readiness annars dyker
rolling-deploys 503:or under den ~10-30 sekunders DbContext-warmup-fönstret.

**Föreslagen åtgärd:**
```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"])
    .AddRedis(redisCs, "redis", tags: ["ready"]);

app.MapHealthChecks("/api/live", new HealthCheckOptions {
    Predicate = _ => false  // bara process-status
});
app.MapHealthChecks("/api/ready", new HealthCheckOptions {
    Predicate = check => check.Tags.Contains("ready")
});
```

ALB target-group ska peka på `/api/ready`. ECS task-def kan optionellt få
`/api/live` som container-level liveness (men Fargate respekterar inte
Docker HEALTHCHECK ändå — så bara ALB-check är auktoritativ).

**Beroenden:** Fas 2 trafikvolym + frontend rolling-deploy-känslighet.
Adresseras i Fas 2 prereq-stängning (samma round som ADR 0005:s
go-to-market-beslut + rate-limiting-utvidgning).

---

## TD-30: Domänköp + Route53 + ACM-cert (kopplad till ADR 0026-trigger)
**Kategori:** Infra / Security
**Severity:** Major (tidsbundet — hard deadline 2026-06-08)
**Källa:** security-auditor STEG 13b Sec-Major-1 + ADR 0026

ADR 0026 accepterar ALB HTTP-only under Fas 0 med tidsfönster 30 dagar
(deadline **2026-06-08**) och 5 triggers för supersession. Trigger 1
(domän + ACM-cert) är aktivitet som måste utföras före deadline för att
undvika tvångs-trigger 3 (tidsgräns).

**Operativa steg när Klas är redo:**

1. Registrera `jobbpilot.se` (eller alternativ domän) hos svensk registrar
   (~80 kr/år hos t.ex. Loopia/Binero/Glesys). Cirka 1 timme + DNS-
   propagering.
2. Skapa Route53 hosted zone i AWS:
   ```hcl
   resource "aws_route53_zone" "this" {
     name = "jobbpilot.se"
   }
   ```
   Delegera från registrar till AWS NS-records (4 nameservers från
   `aws_route53_zone.this.name_servers`).
3. Begär ACM-cert via DNS-validering:
   ```hcl
   resource "aws_acm_certificate" "this" {
     domain_name       = "dev.jobbpilot.se"
     validation_method = "DNS"
   }
   ```
4. Skapa A-ALIAS-record `dev.jobbpilot.se → ALB-DNS`.
5. I `environments/dev/terraform.tfvars`:
   ```hcl
   alb_https_enabled       = true
   alb_acm_certificate_arn = "arn:aws:acm:..."
   ```
6. `terraform apply` — ALB konverterar HTTP-listenern till
   HTTPS-redirect via dynamic-block (befintlig modul-kod).
7. Skriv supersession-ADR (ADR 0027 eller liknande) som flippar
   ADR 0026:s status → Superseded.
8. Update `current-work.md` + `steg-tracker.md`.

**Konsekvens om INTE adresserat innan 2026-06-08:**
- ADR 0026 trigger 3 (tidsgräns) aktiveras automatiskt → krav på
  ny ADR med uttryckligen förlängt fönster ELLER `terraform destroy`
  på alb + ecs-modulerna (dev tas ner).

**Beroenden:** Klas väljer registrar + domän + betalar. Inga tekniska
hinder. Kan göras parallellt med STEG 13b-apply (ALB skapas först
HTTP-only, HTTPS adderas senare via samma modul).

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

## TD-39: Error-summary-mönster för stora formulär (Resume + framtida)

**Kategori:** Accessibility / UX
**Fas:** 2+ (eller vid faktisk användarsignal)
**Prioritet:** Låg
**Källa:** design-review Fas 1 Block A1 2026-05-10 (Minor m2)

`ResumeContentForm` visar endast första `parsed.error.issues[0]` när Zod-validering
fallerar. För 20+-fälts-formulär ger detta klick-validera-fix-klick-validera-fix-loop
om användaren har fler än ett fel samtidigt.

`jobbpilot-design-a11y` §10 beskriver ett "error summary"-mönster: lista alla fel
i en samlad `<div role="alert">` med ankarlänkar till respektive fält. Skalar
bättre för stora formulär.

**Bedömning Fas 1:** acceptabelt att skjuta upp tills faktisk användarsignal
finns. Kräver multi-path state, ankarlänkar, fokus-strategi.

**Föreslagen åtgärd:** vid faktisk friktion-rapport — implementera error summary
ovanför submit-knappen, behåll per-fält `aria-invalid` (TD-15-arvet).

---

## TD-40: Path-equality i `fieldA11y` — saknar regression-bevakning vid parent-path-refines

**Kategori:** Accessibility / Robustness
**Fas:** 1 a11y-pass-completion (samma som TD-1, TD-2)
**Prioritet:** Låg
**Källa:** design-review Fas 1 Block A1 2026-05-10 (Minor m1)

`ResumeContentForm.fieldA11y` använder strikt `serverError?.path === path`-jämförelse.
Schemat i `resume-schemas.ts` lägger idag alla `.refine()`-fel på barn-path
(t.ex. `experiences.0.endDate`), så strikt match fungerar för dagens output.

**Risk:** om framtida `.refine()` på `z.object()` lämnar path tomt eller
pekar på array-rot (`experiences.0` utan fält-suffix), hamnar felet på
toppnivå-`<p>` utan `aria-invalid`-flaggat fält. Skärmläsare hör då
felmeddelandet via `role="alert"` men kan inte navigera till specifik fält.

**Föreslagen åtgärd:** lägg till regression-test som validerar att alla
`.refine()` i `resume-schemas.ts` pekar på leaf-path. Ingen kodändring i
formuläret krävs idag — bevakning räcker.

---

## TD-41: Select-komponent-konvention — native vs shadcn Radix

**Kategori:** UI / Component-konvention
**Fas:** 1 (beslutas innan A3)
**Prioritet:** Medium
**Källa:** design-review Fas 1 Block A2 2026-05-10 (Major M1+M2)

`MeProfileForm` använder native `<select>` med Tailwind-styling kopierad
inline från `Input.tsx` (~110 tecken). Samtidigt finns en fullskalig
shadcn/Radix-baserad `Select` redan installerad i `components/ui/select.tsx`
(193 rader). Inkonsekvens mellan formulär.

**Risk:**
- **Drift-risk:** När `Input.tsx`-tokens uppdateras driftar inline-stilen
  i select-elementet
- **Konsistens-risk:** Andra formulär (login/register/resume-content) använder
  Input + Textarea, men nästa form med dropdown blir ännu en native-implementation
  om mönstret inte fastställs

**Föreslagen åtgärd:** beslut Klas/design — antingen
- (a) Behåll native för 2-opt-listor, lyft inline-stilen till
  `ui/native-select.tsx`-primitiv. Lägg kommentar i `MeProfileForm` om
  varför native valdes.
- (b) Migrera `MeProfileForm` till shadcn `Select` (kräver Controller från RHF).
  Etablera "shadcn Select är default"-konvention.

Beslut behöver tas innan A3 så framtida formulär följer en linje.

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

## TD-51: Admin-läs-aktioner ska audit-loggas (GDPR Art. 30)
**Kategori:** GDPR compliance / Audit
**Severity:** Sec-Minor (Fas 6-utbyggnad)
**Källa:** security-auditor, 2026-05-11 (Fas 1-stängning admin-audit)

GET `/api/v1/admin/audit-log` skriver INGEN audit-rad — Fas 1-modellen
auditerar bara success-mutationer (ADR 0022). Men admin-läs-aktioner mot
PII-innehållande data (audit-trail med IP/UA) är i sig en behandlingsaktivitet
per GDPR Art. 30 (record of processing).

När impersonation och admin-suspend införs i Fas 6 bör samma ADR-revision
lägga till `IAuditableQuery`-mönster så admin → audit-rad `Admin.AuditLogViewed`
skrivs.

**Föreslagen åtgärd:** Lyfts vid Fas 6 ADR-extension. Inte STEG-fix för Fas 1.

**Scope:** Hör till annan fas (kriterium 1 i 4-timmarsregeln).

---

## TD-52: Admin-endpoint saknar dedikerad rate-limit-policy
**Kategori:** Security (DoS-skydd)
**Severity:** Sec-Minor (Fas 6-utbyggnad)
**Källa:** security-auditor, 2026-05-11 (Fas 1-stängning admin-audit)

`AdminEndpoints` har `.RequireAuthorization(AuthorizationPolicies.Admin)`
men ingen `.RequireRateLimiting(...)`. För Fas 1 (en admin = Klas) ingen
praktisk DoS-vektor. För Fas 6 när admin-roll kan utvidgas till support-personal
eller om en admin-session kompromitteras bör en separat `AdminLoosePolicy`
(t.ex. 60/min per UserId-partition) införas.

**Föreslagen åtgärd:**
1. Skapa `AdminLoosePolicy` med 60/min per UserId
2. Applicera på `/api/v1/admin/*`-group i `AdminEndpoints.cs`

**Scope:** Hör till Fas 6 admin-yta-utbyggnad (kriterium 1).

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

## TD-53a: Frontend kind-union — Helper + ADR 0030 + detail/profile-endpoints
**Kategori:** Code consistency / Frontend / Architecture
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

## TD-53b: Frontend kind-union — list-endpoints
**Kategori:** Code consistency / Frontend
**Severity:** Minor
**Fas:** 1
**Källa:** TD-53 split per senior-cto-advisor-triage 2026-05-11

Färdigställer kind-union-konsistens på list-endpoints:
- `getPipeline()` — refactor från `PipelineGroupDto[]` (tom-fallback) till `ApiResult<PipelineGroupDto[]>`
- `getApplications(page, pageSize, status?)` — refactor från `T | null`
- `getResumes(page, pageSize)` — refactor från `T | null`

Konsumenter: `app/(app)/ansokningar/page.tsx`, `app/(app)/cv/page.tsx`.

**Scope:** ~4-6h CC-tid. Lyfts efter TD-53a är pushed.

**Beroenden:** TD-53a (helper + ADR 0030) måste vara klart först.

**Trigger:** Efter TD-53a-stängning.

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

## TD-56: ListJobAdsQuery full paginering (Fas 2 JobTech-integration)
**Kategori:** Architecture
**Severity:** Minor
**Fas:** 2 (JobTech Integration)
**Källa:** TD-55-CTO-beslut, 2026-05-11

`ListJobAdsQueryHandler` är opaginerad idag med `.Take(500)` hard cap som
temporär defense-in-depth. Vid Fas 2 JobTech-integration ska den retro-fittas
till full `PagedResult<JobAdDto>` med query-params och URL-kontrakt som matchar
JobTech-API:t.

**Föreslagen åtgärd:**
1. Lyft `MaxItems = 500`-konstant från handler till `JobAdOptions`-record
   bunden via `IOptions<JobAdOptions>` (CLAUDE.md §5.1)
2. Refactor `ListJobAdsQuery` → `PagedResult<JobAdDto>` med PageNumber/PageSize
3. Bestäm anonym-vs-auth-policy för publik JobAd-katalog
4. Anpassa URL-kontrakt mot JobTech-API:s sök-params

**Scope:** > 4h CC-tid när det görs (kriterium 3) — kräver design-arbete mot
JobTech-spec. Defereras till Fas 2 där JobTech-integration är primärt fokus.

**Trigger:** Fas 2-uppstart, ADR 0005 (go-to-market) eller JobTech-integration.

---

## TD-57: Native form-controls divergerar från Input-primitive
**Kategori:** Architecture / Consistency
**Severity:** Minor (cosmetic + a11y-attribute-gap)
**Fas:** 1 a11y-pass-completion
**Källa:** design-reviewer + code-reviewer Fas 1.5 a11y-pass 2026-05-11 (TD-42 M3 / Minor 1)

Native form-controls (datetime-local i `add-follow-up-form.tsx:60` + native
language-select i `me-profile-form.tsx:116`) styled manuellt och saknar
Input-primitive-defaults:

| Attribut | Input-primitive | Native form-controls |
|---|---|---|
| `rounded-*` | `rounded-sm` | `rounded-md` |
| `py-*` | `py-1` | `py-2` |
| `text-*` | `text-base md:text-sm` | `text-sm` eller `text-base` |
| `aria-invalid-styling` | ✓ | saknas |
| `dark-mode-styling` | ✓ | saknas |
| `disabled bg-färg` | `disabled:bg-input/50` | saknas |

Höjden alignar nu (h-9 via TD-42), men övriga defaults divergerar.
Pre-existing inkonsekvens som blev synlig när skill-doc:en blev
auktoritativ för field-height.

**Föreslagen åtgärd (kräver design-beslut):**
1. **Variant A:** Lyft `inputBaseClasses` till `lib/forms/input-base.ts`
   och referera från native-element via `cn(inputBaseClasses, ...)`
2. **Variant B:** Skapa wrapper-komponenter `<NativeInput>` / `<NativeSelect>`
   som inheritar Input.tsx-klasser
3. **Variant C:** Ersätt native med shadcn-pendang (Input type="datetime-local"
   där möjligt, shadcn Select där meningsfullt — kräver eventuellt 3rd-party
   datepicker)

**Scope:** ~2-3h CC-tid (kriterium 3: ej >4h men kräver design-beslut). Kan
lyftas in i nästa a11y-pass eller paras med eventuell datepicker-introduktion.

**Trigger:** Nästa a11y-pass eller designgenomgång av form-system.

---

## TD-58: `IAccountHardDeleter` blandar 3 ansvar (ISP-split)
**Kategori:** Architecture / SOLID (ISP)
**Severity:** Minor
**Fas:** 6 admin-impersonation
**Källa:** arch-audit `docs/reviews/2026-05-11-arch-audit-discovery.md` §H-1

Porten `IAccountHardDeleter` exponerar 3 operationer av olika natur:
`CleanupIdentityOrphansAsync` (idempotency-städ-loop),
`GetAccountsReadyForHardDeleteAsync` (read-side query) och
`HardDeleteAccountAsync` (transactional cross-context mutation).

`HardDeleteAccountsJob` är enda konsumenten idag → ISP-skadan teoretisk men
porten blockerar framtida återbruk (admin-vy som vill köra
`CleanupIdentityOrphans` standalone får hela porten på köpet).

**Föreslagen åtgärd:** Split i `IIdentityOrphanCleaner` +
`IExpiredAccountReader` + `IAccountHardDeleter`. Arch-test uppdateras
till tre separata konsumentlistor.

**Scope:** ~2h CC-tid (kriterium 3). Defereras till Fas 6 admin-impersonation
eftersom admin-yta då naturligt introducerar andra konsumenter — splittas
opportunistiskt när Fas 6 öppnar admin-purge-knapp.

**Trigger:** Fas 6 admin-impersonation eller annan admin-feature som behöver
en av del-portarna standalone.

---

## TD-59: `ICurrentJobSeeker`-port för user→JobSeekerId-resolution
**Kategori:** Architecture / DRY + SoC
**Severity:** Minor
**Fas:** 6 admin-impersonation (eller tidigare om scope tillåter)
**Källa:** arch-audit `docs/reviews/2026-05-11-arch-audit-discovery.md` §H-2

Identisk uppslagslogik (user → JobSeekerId) i 13 handlers:

```csharp
if (!currentUser.UserId.HasValue)
    throw new UnauthorizedException(); // eller return Failure(...)

var jobSeekerId = await db.JobSeekers
    .AsNoTracking()
    .Where(js => js.UserId == currentUser.UserId.Value)
    .Select(js => js.Id)
    .FirstOrDefaultAsync(cancellationToken);
```

Sömlösa subtila skillnader: vissa kastar `UnauthorizedException`, andra
returnerar `Result.Failure<T>(...)`. `CreateApplicationCommandHandler` saknar
dessutom `.AsNoTracking()`. Inte Clean Arch-brott men klassiskt DRY-läckage
som biter vid impersonation-retrofit.

**Föreslagen åtgärd:** Introducera `ICurrentJobSeeker`-port (Application-lager)
som omsluter user→JobSeekerId-resolutionen + lyfter felhanterings-policyn till
en plats. Infrastructure-impl wrappar `IAppDbContext` + `ICurrentUser`.
Handlers krymper till 1 rad:
`var jobSeekerId = await currentJobSeeker.RequireIdAsync(ct);`

**Scope:** ~2-3h CC-tid (kriterium 3). Defereras till impersonation-feature
där JobSeekerId-resolutionen naturligt behöver utvidgas med
`ImpersonatedJobSeekerId`-claim.

**Trigger:** Fas 6 admin-impersonation eller dedikerad refactor-batch.

**Berörda filer (13):** `CreateApplicationCommandHandler`,
`AddNoteCommandHandler`, `TransitionToCommandHandler`,
`AddFollowUpCommandHandler`, `GetPipelineQueryHandler`,
`GetApplicationsQueryHandler`, `GetApplicationByIdQueryHandler`,
`CreateResumeCommandHandler`, `RenameResumeCommandHandler`,
`DeleteResumeCommandHandler`, `DeleteResumeVersionCommandHandler`,
`UpdateMasterContentCommandHandler`, `GetResumesQueryHandler`.

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

## TD-62: OpenAPI-codegen som supersession av manuella Zod-DTO-schemas (Fas 2+)
**Kategori:** Architecture / Tooling
**Severity:** Minor
**Fas:** 2+ (när backend OpenAPI-export etableras)
**Källa:** senior-cto-advisor-triage 2026-05-11 i TD-7-stängning (variant B-lyft)

ADR 0020 accepterar manuella Zod-schemas i `lib/dto/*.ts` som Fas 1-lösning.
Variant B (OpenAPI-codegen från backend-spec) är arkitekturellt överlägsen
men kräver infrastruktur som inte finns:

- Backend `/openapi/v1.json` är inte etablerad som versionerad artefakt
- CI-pipeline för codegen (`openapi-zod-client` eller motsvarande) saknas
- Generated-files-policy + commit-strategi inte beslutad
- BUILD.md placerar `docs/api/openapi.yaml` "post-Fas 0"

**Risk i Fas 1:** noll (manuella schemas fungerar, refactor löste original-buggen).

**Risk vid Fas 2+ tillväxt:** Medium-Minor. Varje ny endpoint kräver manuellt
Zod-schema → backend-shape kan drifta från frontend-schema utan att tsc fångar
det. Mitigerat av schema-tester (happy + mismatch) men beror på discipline.

**Föreslagen åtgärd vid Fas 2+:**

1. Etablera backend OpenAPI-export som versionerad artefakt (egen ADR)
2. Bygga CI-pipeline för `lib/dto/generated/*.ts` (codegen-tool TBD)
3. Migrera `lib/dto/*.ts` till generated — single source of truth = backend
4. ADR-supersession av ADR 0020 (manuella schemas → generated)

**Beroenden:** Backend OpenAPI-export-pipeline + CI-step + generated-files-
policy. Trigger = Fas 2 JobTech-integration när endpoint-ytan växer markant.

**Trigger:** Fas 2-uppstart eller ADR 0005 (go-to-market) ger volym-incentiv
för pipeline-etablering.

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