# Tech Debt — JobbPilot

Aktiva TD-poster organiserade i **Severity × Fas-matris** per
senior-cto-advisor-triage 2026-05-11. Stängda TDs i separat
[`tech-debt-archive.md`](./tech-debt-archive.md) (kronologisk ordning).

ID-konvention: TD-{nummer}. Items hänvisas till via ID i ADR:er,
PR-beskrivningar och commits.

**Refactor-policy (CLAUDE.md §9.6, uppdaterad 2026-05-11):** TD lyfts endast
om fyndet hör till annan fas eller saknar funktion-dependency. Ingen
tidsbegränsning per touch — fas-tillhörighet styr. Default = fixa in-block.

---

## Översiktstabell — aktiva TDs

| ID | Titel | Severity | Fas | Kategori |
|---|---|---|---|---|
| TD-30 | Domänköp + Route53 + ACM-cert (deadline 2026-06-08) | **Major (tidsbundet)** | **Nu** | Infra/Security |
| TD-10 | PII-läckage via `body?.detail` i Server Actions | **Major** | 1 | Säkerhet |
| TD-11 | Hårdkodad E2E-lösenord och testemail på produktionsdomän | **Major** | 1 | Säkerhet |
| TD-41 | Select-komponent-konvention — native vs shadcn Radix | **Major** | 1 | UI/Component |
| TD-13 | Encryption av PII-kolumner | **Major** | 2 | Säkerhet/GDPR |
| TD-26 | AI-kostnadstak: token-limit + per-user spend-cap | **Major** | 4 (AI) | Säkerhet/Kostnad |
| TD-1 | Skip-link saknas i (app)-layout | Minor | 1 | A11y (WCAG 2.4.1) |
| TD-2 | CardTitle renderas utan heading-tag | Minor | 1 | A11y (heading-hierarki) |
| TD-3 | Tom-state-copy "Inga roller tilldelade" saknar next-action | Minor | 1 | UX |
| TD-4 | userId visas i UI utan tydligt användarbehov | Minor | 1 | UX/Privacy |
| TD-5 | Redundant getServerSession-anrop på /mig | Minor | 1 | Code hygiene |
| TD-6 | Logout-backend-call utan fel-loggning | Minor | 1 | Observability |
| TD-12 | Saknad integration-test för cross-user isolation | Minor | 1 | Säkerhet/Test |
| TD-28 | Frontend typed-confirmation-UX + re-auth-prompt på DELETE /me | Minor | 1 | UX/Säkerhet |
| TD-40 | Path-equality i `fieldA11y` — saknar regression-bevakning | Minor | 1 | A11y/Robusthet |
| TD-57 | Native form-controls divergerar från Input-primitive | Minor | 1 | Architecture/Consistency |
| TD-19 | Worker orchestrator + DI-pattern: defense-in-depth | Minor | 2 | Code quality |
| TD-23 | RedisSessionStore atomicitet via MULTI/EXEC eller Lua | Minor | 2 | Säkerhet/Robusthet |
| TD-24 | DeleteAccountCommand cascade-paginering vid power-user | Minor | 2 | Skalbarhet |
| TD-25 | HardDeleteAccountsJob per-konto try/catch (resilient loop) | Minor | 2 | Robusthet |
| TD-27 | EmailHash → HMAC med roterande nyckel | Minor | 2 | Säkerhet/GDPR |
| TD-29 | Strict readiness-probe — separera liveness från readiness | Minor | 2 | Observability |
| TD-56 | ListJobAdsQuery full paginering (Fas 2 JobTech-integration) | Minor | 2 | Architecture |
| TD-62 | OpenAPI-codegen som supersession av manuella Zod-DTO-schemas | Minor | 2+ | Architecture/Tooling |
| TD-14 | DeleteResumeVersion: VersionInUse-check inaktiv | Minor | 4 (AI) | Säkerhet/Data integrity |
| TD-51 | Admin-läs-aktioner ska audit-loggas (GDPR Art. 30) | Minor | 6 | GDPR compliance |
| TD-52 | Admin-endpoint saknar dedikerad rate-limit-policy | Minor | 6 | Säkerhet |
| TD-58 | `IAccountHardDeleter` blandar 3 ansvar (ISP-split) | Minor | 6 | Architecture/SOLID |
| TD-59 | `ICurrentJobSeeker`-port för user→JobSeekerId-resolution | Minor | 6 | Architecture/DRY |
| TD-8 | GetPipeline saknar fullständig paginering | Minor | Efter MVP | Skalbarhet |
| TD-18 | Stale-detektering: utökning till intervju-states | Minor | Trigger | UX/Domain |
| TD-20 | `AuditPartitionMaintainer.DropPartitionsOlderThanAsync` defensiv refactor | Minor | Opportunistiskt | Code quality |
| TD-39 | Error-summary-mönster för stora formulär | Minor | Trigger | A11y/UX |

---

## Adresseringsstrategi

- **Fas-regeln (CLAUDE.md §9.6):** TDs i nuvarande fas ska fixas innan fas-stängning.
- **A11y/UX/Observability-passes:** items inom samma kategori adresseras gruppvis
  i dedikerade Fas 1-passes (a11y-pass, UX-pass, observability-pass) om scope tillåter.
- **Touch-opportunistik:** vid arbete med berörda filer i andra ärenden — adressera
  relevanta TD-items in-block om scope tillåter.
- **TD-5** kan landas som "no-op + dokumentera" i layout-kommentar utan separat fas.

---

## Major — Fas Nu

Tidsbunden eller akut. Bör vara tom när möjligt — om sektionen växer signalerar
det att fas-regeln bryts (TDs lyfts som dumpning istället för att fixas in-block).

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


## Major — Fas 1

## TD-10: PII-läckage via `body?.detail` i Server Actions

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

## TD-11: Hårdkodad E2E-lösenord och testemail på produktionsdomän

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


## Major — Fas 2

## TD-13: Encryption av PII-kolumner (Fas 2)

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


## Major — Fas 3+

## TD-26: AI-kostnadstak: token/tecken-limit pre-Bedrock + per-user spend-cap

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


## Minor — Fas 1

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

## TD-12: Saknad integration-test för cross-user isolation

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

## TD-28: Frontend typed-confirmation-UX + re-auth-prompt på DELETE /me

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


## Minor — Fas 2

## TD-19: Worker orchestrator + DI-pattern: defense-in-depth-förbättringar

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

## TD-23: RedisSessionStore atomicitet via MULTI/EXEC eller Lua-script

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

## TD-24: DeleteAccountCommand cascade-paginering vid power-user

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

## TD-25: HardDeleteAccountsJob per-konto try/catch (resilient loop)

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

## TD-27: EmailHash → HMAC med roterande nyckel (Fas 2)

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


## Minor — Fas 3+

## TD-14: DeleteResumeVersion: VersionInUse-check är inaktiv tills Application får ResumeVersionId

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


## Minor — Efter MVP / Trigger-baserade

Adresseras vid faktisk användarsignal, skala-tröskel eller opportunistisk touch.

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

## TD-18: Stale-detektering: utökning till intervju-states

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

## TD-20: `AuditPartitionMaintainer.DropPartitionsOlderThanAsync`: SqlQueryRaw + format-string-escape (defensiv refactor)

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

## Stängda TDs

Full kropp för varje stängd TD finns i [`tech-debt-archive.md`](./tech-debt-archive.md).
Kronologisk ordning (äldsta först) i arkivet bevarar audit-trail för
ADR-cross-references och granskningsbevis.

| ID | Titel | Stängd | Källa/Commit |
|---|---|---|---|
| TD-9 | Audit log Application-domänhändelser | 2026-05-08 | STEG 8 (ADR 0022) |
| TD-16 | Audit-log retention + GDPR Art. 17-anonymisering | 2026-05-08 | STEG 10a+10b (ADR 0024 D1–D6) |
| TD-22 | App-logg-retention + IP/UA-redaction | 2026-05-08 (delvis) | STEG 11 (ADR 0024 D7) |
| TD-17 | Hangfire prod-härdning | 2026-05-09 (kod-delen) | STEG 11+12 |
| TD-21 | Rate-limiting på DELETE /me + auth-endpoints | 2026-05-09 | STEG 11 |
| TD-15 | Resume-formulär: Zod-issue path → aria-invalid | 2026-05-10 | Block A1 |
| TD-31 | Test för UseHttpsRedirection env-gate | 2026-05-10 | Block A3 |
| TD-37 | Backend Integration tests fail i CI | 2026-05-10 | STEG 14c (run 25637996682) |
| TD-7 | Zod runtime-validering för DTOs från backend | 2026-05-11 | ADR 0020 |
| TD-38 | Trust Server Certificate=true persistens | 2026-05-11 | Block A4 (`ebb7550` + `7cde3c7`) |
| TD-42 | Touch-target projektbrett under WCAG 2.5.5 | 2026-05-11 | Väg B a11y (`f2b179a` + `1b0b9ec`) |
| TD-43 | Komponent-test-strategi för forms (Vitest + RTL) | 2026-05-11 | Block A4 parallell |
| TD-44 | HSTS-header-anti-regression-test | 2026-05-11 | - |
| TD-45 | LoginForm focus-flytt vid `state.error` | 2026-05-11 | TD-43 follow-up |
| TD-46 | Exportera `pathToElementId` för isolated unit-test | 2026-05-11 | TD-43 follow-up |
| TD-47 | RDS CA-bundle-rotation-bevakning | 2026-05-11 | Väg C Block B.2 (`f9313af`) |
| TD-48 | Architecture-test för Trust=true-läckage | 2026-05-11 | Väg C Block B.1 (`9f33897`) |
| TD-49 | HstsOptions.EnsureSafeForEnvironment unit-test | 2026-05-11 | Väg E (redan implementerad pre-TD) |
| TD-50 | AdminBootstrap__InitialAdminEmail dokumenteras | 2026-05-11 | Väg C Block C (`a9ca126`) |
| TD-53 | Frontend API-resultatformat kind-union | 2026-05-11 (ersatt) | split → TD-53a + TD-53b |
| TD-53a | Frontend kind-union — helper + detail-endpoints | 2026-05-11 | `7e90b36` (ADR 0030) |
| TD-53b | Frontend kind-union — list-endpoints + admin.ts | 2026-05-11 | `aac9b2f` |
| TD-54 | `text-text-tertiary` empty-state WCAG AA | 2026-05-11 | Väg B (`8cfbde4` + `52f3b45`) |
| TD-55 | PagedResult + ApplicationsQuery hardening | 2026-05-11 | Väg C Block A (`c2f539e` + `0b0886d` + `5784120`) |
| TD-60 | ADR för auth-pipeline-ordning + IClaimsTransformation | 2026-05-11 | ADR 0029 |
| TD-61 | Audit-trail-evidence-test för IdempotentAdminRoleSeeder | 2026-05-11 | Väg B (`47f8deb`) |

---

## Kända TD-ID-luckor (cross-ref-disclaimer)

Discovery under tech-debt.md refactor 2026-05-11 fann att **TD-32 till TD-36**
refereras i `docs/decisions/0027-https-aktiverat-supersession.md` (rad 128–132)
som "Out-of-scope-TDs som lyfts" — men de skapades aldrig som faktiska poster
i tech-debt.md. Det är en pre-existing cross-ref-defekt i ADR 0027, inte i
denna refactor.

| Ej-skapad TD | ADR 0027-beskrivning | Status |
|---|---|---|
| TD-32 | TLS-policy uppgrade till `ELBSecurityPolicy-TLS13-1-2-2025-09` (post-quantum) | ADR-planerad, ej allokerad |
| TD-33 | HSTS pipeline-gating-test via `WebApplicationFactory<Program>` | Sannolikt subsumerad av TD-44 (HSTS-anti-regression, stängd 2026-05-11) |
| TD-34 | DNSSEC aktivering vid Fas 1-trigger | ADR-planerad, ej allokerad |
| TD-35 | Apex (`jobbpilot.se`) + `www` ACM-cert + ALB-cert-association | ADR-planerad, ej allokerad |
| TD-36 | mTLS / in-VPC-encryption (ALB → ECS) vid Fas 2 multi-tenant | ADR-planerad, ej allokerad |

**Beslutskandidat för nästa session:** allokera TD-32, TD-34, TD-35, TD-36 som
faktiska poster i denna fil (med ADR 0027 som källa) eller amenda ADR 0027 så
referenserna pekar på en "planerade-uppgrader"-lista istället för fiktiva
TD-IDs. TD-33 bör verifieras om den är samma som TD-44 (i så fall: notera i
arkivet att TD-44 stängde TD-33-scope).
