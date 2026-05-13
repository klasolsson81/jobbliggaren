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
| TD-13 | Encryption av PII-kolumner | **Major** | 2 | Säkerhet/GDPR |
| TD-26 | AI-kostnadstak: token-limit + per-user spend-cap | **Major** | 4 (AI) | Säkerhet/Kostnad |
| TD-19 | Worker orchestrator + DI-pattern: defense-in-depth | Minor | 2 | Code quality |
| TD-23 | RedisSessionStore atomicitet via MULTI/EXEC eller Lua | Minor | 2 | Säkerhet/Robusthet |
| TD-24 | DeleteAccountCommand cascade-paginering vid power-user | Minor | 2 | Skalbarhet |
| TD-27 | EmailHash → HMAC med roterande nyckel | Minor | 2 | Säkerhet/GDPR |
| TD-74 | Strikta DML-GRANTs på public + identity istället för GRANT ALL | Minor | 2 (opportunistisk) | Säkerhet/Least Privilege |
| TD-72 | Auto-trigga Migrate bootstrap-mode i deploy-dev.yml | Minor | Trigger | Operations/CI-CD |
| TD-75 | Name-baserad rekryterar-PII-radering (multi-path jsonb + full-text) | Minor | Trigger | GDPR/Privacy |
| TD-76 | GIN-index på raw_payload jsonb (latens-trigger) | Minor | Trigger | Performance |
| TD-77 | Backend 5xx-rate-alarm (1% över 5 min) | Minor | 8 (Klass-launch) | Observability/SLA |
| TD-78 | DB CPU > 80% i 10 min-alarm | Minor | 8 (Klass-launch) | Observability/Capacity |
| TD-62 | OpenAPI-codegen som supersession av manuella Zod-DTO-schemas | Minor | 2+ | Architecture/Tooling |
| TD-63 | ActionResult kind-union för writes (ADR 0030-symmetri) | Minor | 2+ | Architecture |
| TD-64 | i18n-migration av inline svenska error-strängar | Minor | Trigger | i18n |
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

*(Sektionen tom 2026-05-11 — TD-30 stängd, ADR 0026 superseded av ADR 0027.)*


## Major — Fas 1

*(Sektionen tom 2026-05-11 — TD-41 stängd i Batch B per CTO-beslut shadcn-first.)*


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
- `job_ads.raw_payload` (JSONB) — JobTech-payload kan innehålla rekryterar-PII (namn, email, telefon, firmatecknare). Tillagd 2026-05-12 efter security-auditor F2-P8a-aggregat-review Sec-Major-1. Cross-ref: `JobAdConfiguration.cs:25` + ADR 0032 §8-amendment 2026-05-12.

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

*(Sektionen tom 2026-05-12 — TD-68 stängd efter dev-apply.)*


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

## TD-63: ActionResult kind-union för writes (ADR 0030-symmetri)
**Kategori:** Architecture / Frontend
**Severity:** Minor
**Fas:** 2+ (efter Fas 1-stängning eller naturlig konsument-touch)
**Källa:** TD-10 CTO-triage 2026-05-11 — Variant C-scope separerat från säkerhets-fix

ADR 0030 etablerade `ApiResult<T>` kind-union för read-endpoints. Server
Actions (writes) använder fortfarande egen `ActionResult = { success: true } | { success: false; error: string }`. CTO-beslut 2026-05-11 valde Variant B
(central error-helper) för TD-10 över Variant C (kind-union för writes) eftersom
C kräver konsument-rework i 8+ komponenter och bryter commit-SRP för
säkerhets-TD.

**Föreslagen åtgärd:**

1. Migrera `ActionResult` → discriminated kind-union:
   ```ts
   type ActionResult =
     | { kind: "ok" }
     | { kind: "validation"; fieldErrors?: Record<string, string> }
     | { kind: "unauthorized" }
     | { kind: "forbidden" }
     | { kind: "conflict" }
     | { kind: "error"; message: string };
   ```
2. Uppdatera konsumenter (RHF + `useActionState` i 8+ komponenter) till
   exhaustive switch via `assertNever`.
3. Speglar `ApiResult<T>` för symmetri reads/writes.
4. Backend börjar exponera `ValidationProblemDetails.errors` → kan mappas
   till `fieldErrors` (typed disambiguation).

**Risk i Fas 1:** noll (TD-10 säkerhets-fix levererad utan kind-union).

**Risk vid Fas 2+:** Minor (arch-konsistens mellan reads/writes; konsumenter
kan inte typed-diskriminera unauthorized/conflict/validation utan att parsa
error-strängar — fragil).

**Scope:** ~5h CC-tid (kriterium 3). 8+ konsument-touch.

**Trigger:** (a) Backend börjar exponera fält-nivå validation errors, (b) 3:e
konsument efterfrågar typad disambiguation, eller (c) naturlig komponent-touch
som ändå rör flera action-call-sites. Föreslås parallellt med eventuell
i18n-migration (TD-64) för att samla error-stränghantering på ett ställe.

**Beroenden:** Ingen blockerare — kan göras opportunistiskt.


---

## TD-64: i18n-migration av inline svenska error-strängar
**Kategori:** i18n / `next-intl`
**Severity:** Minor
**Fas:** Efter MVP / Trigger
**Källa:** TD-10 CTO-triage 2026-05-11 — i18n-readiness skjuten

`_action-error.ts` + action-files innehåller idag inline svenska
fallback-strängar. CLAUDE.md §5.2 säger `next-intl` med `messages/sv.json`
är slutläget — men befintliga inline-svenska är redan spridda över hela
actions-lagret + komponent-trädet. Att lyfta enbart de 11 error-strängarna
från denna touch skulle skapa inkonsekvens (dessa 11 i messages, resten
inline).

**Föreslagen åtgärd:** helhets-omnibus-migration när minst en av triggers
infaller:

1. Andra språk på roadmap
2. Klas/test-användare upptäcker inkonsekvens
3. Komponent-bibliotek refactoreras ändå
4. Naturligt nästa stora frontend-pass (kan kombineras med TD-63
   kind-union-migration för att samla error-hantering på ett ställe)

Scope inkluderar: alla `_action-error.ts`-strängar, action-fallback-strängar,
komponent-strängar, formulär-validation-strängar. Inte bara error-paths.

**Risk i Fas 1:** noll (svenska-only, inga externa språk).

**Scope:** stort omnibus-pass — egen ADR för i18n-strategy (namespace-design,
message-key-konvention, fallback-policy) före implementation.

**Trigger:** se ovan.

**Beroenden:** Egen i18n-strategy-ADR.


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


---

## TD-77: Backend 5xx-rate-alarm (1% över 5 min)
**Kategori:** Observability / SLA
**Severity:** Minor
**Fas:** 8 (Klass-launch)
**Källa:** senior-cto-advisor 2026-05-13 (v0.2-prod-tag-readiness-rond, Q2-deferral)

BUILD.md §14.4 specar `Backend 5xx-rate > 1% över 5 min → PagerDuty/email`.
Inte konfigurerat idag — saknas i `modules/cloudwatch_security_alarms` och
finns ingen ALB target-group-baserad alarm-yta heller.

**Risk i v0.2-prod-tag-fas:** noll. JobbPilot har 1 användare (Klas) första
prod-veckorna. 1%-tröskel mot lågvolym ger ingen meningsfull signal:
- 100 requests/dag × 1% = 1 5xx → trigger redan vid enstaka transient fel
- Tröskeln är meningsfull först vid multi-user volym (Fas 7 internal beta /
  Fas 8 Klass-launch där 20+ aktiva användare ger statistiskt stabila samples)

**CTO-motivering (Beck 1999 YAGNI + Fowler 2002):** SLA-tröskel utan
volym = teater. Tröskeln aktiveras meningsfullt först vid Klass-launch.

**Föreslagen åtgärd vid Fas 8:**

1. Utöka `modules/cloudwatch_security_alarms` (eller skapa
   `modules/cloudwatch_ops_alarms`) med:
   - Metric filter på ALB `HTTPCode_Target_5XX_Count` / `RequestCount`
   - Composite-alarm: `5XX/RequestCount > 0.01` över `5 min` med `evaluation_periods = 1`
   - SNS-topic kan delas med secops-anomaly eller separat ops-topic
2. Email-notification till klas@jobbpilot.se
3. Verifiera mot CloudWatch Insights vid Fas 7 internal beta för att tuna tröskel

**Trigger:** Fas 8 Klass-launch eller faktiskt observerat 5xx-mönster i
Fas 7 internal beta.

**Beroenden:** ALB target-group metric-attachment (befintlig via `modules/alb`).
Inga blockerare.


---

## TD-78: DB CPU > 80% i 10 min-alarm
**Kategori:** Observability / Capacity
**Severity:** Minor
**Fas:** 8 (Klass-launch)
**Källa:** senior-cto-advisor 2026-05-13 (v0.2-prod-tag-readiness-rond, Q2-deferral)

BUILD.md §14.4 specar `Databas CPU > 80% i 10 min → email`. Inte
konfigurerat idag — RDS-metrics finns automatiskt i CloudWatch via AWS/RDS-
namespace, men ingen alarm-resurs är skapad.

**Risk i v0.2-prod-tag-fas:** noll. `db.t4g.medium` (eller `db.t4g.micro` för
dev) med 1 användares trafik (Klas + occasional smoke-test) genererar inget
CPU-tryck > 80%. Hangfire-jobben (var 10:e min) toppar kortvarigt men
hanteras inom default-tröskeln.

**CTO-motivering (Beck 1999 YAGNI):** Capacity-alarm utan trafikvolym =
teater. Aktiveras meningsfullt vid Fas 7-Fas 8 när 20+ aktiva användare
genererar reell DB-load.

**Föreslagen åtgärd vid Fas 8:**

1. Utöka observability-modulen med:
   ```hcl
   resource "aws_cloudwatch_metric_alarm" "rds_cpu" {
     alarm_name          = "${var.name_prefix}-rds-cpu-high"
     metric_name         = "CPUUtilization"
     namespace           = "AWS/RDS"
     statistic           = "Average"
     dimensions          = { DBInstanceIdentifier = module.rds.identifier }
     comparison_operator = "GreaterThanThreshold"
     threshold           = 80
     period              = 300
     evaluation_periods  = 2  # 10 min total
     alarm_actions       = [aws_sns_topic.ops_alarms.arn]
   }
   ```
2. Email-notification till klas@jobbpilot.se
3. Vid Fas 8: tuna mot faktisk capacity (kan kräva uppgrade till
   `db.t4g.large` om Klass-launch ger sustained pressure)

**Trigger:** Fas 8 Klass-launch eller observerat CPU-mönster > 60%
sustained i Fas 7 internal beta (tidig varning-signal).

**Beroenden:** Inga.


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

## TD-72: Auto-trigga JobbPilot.Migrate bootstrap-mode i deploy-dev.yml vid Identity-schema-change
**Kategori:** Operations / CI-CD
**Severity:** Minor
**Fas:** Trigger
**Källa:** dotnet-architect F2-P8a-aggregat-review 2026-05-12 + senior-cto-advisor rond 4 2026-05-12

ADR 0033 amendment 2026-05-12 auto-triggar `schema`-mode i deploy-dev.yml.
`bootstrap`-mode (ADR 0034 — Identity-context via master-creds) körs INTE
automatiskt — ADR 0034 §"Out of scope" markerar detta som TD-72-kandidat.
Risk: F2-P0b-mönstret upprepar sig nästa gång Identity-schema-migration
levereras (Identity-schema-change är planerad event ~0-1×/år men cadence-
asymmetri vs AppDb gör mekanisk garanti över-försäkring).

**Trigger för fix:** ett av:
1. Planerad Identity-schema-migration läggs på roadmap → fixa TD-72 i samma
   batch som migrationen
2. F2-P0b-mönstret återupprepar sig (Identity-schema-change glöms vid
   deploy) → fixa retroaktivt + post-mortem

**Föreslagen åtgärd:** Implementera Variant A från F2-P8a-aggregat-review:
- Ny GitHub Actions-step i `deploy-dev.yml` efter "Run Migrate schema-task"
- IAM-utbyggnad i `modules/github_oidc/main.tf` (utvidga RunMigrate-statement
  med bootstrap-task-def-ARN eller verifiera täckning)
- ADR 0034-amendment dokumenterar pipeline-skifte

**Severity-motivering Minor:** trigger-baserad utan tidsbundenhet. Identity-
schema är stabil i Fas 2 (no planerad change). Fas-stängning gating inte på
detta.

**Beroenden:** Inga — kan implementeras isolerat när trigger uppfyllt.


---

## TD-75: Name-baserad rekryterar-PII-radering (multi-path jsonb + full-text)
**Kategori:** GDPR / Privacy
**Severity:** Minor
**Fas:** Trigger
**Källa:** TD-73 prod-gating-batch CTO-decision 2026-05-13 (R-Risk2 — Email-only nu, Name som TD)

TD-73 prod-gating-batch levererade Email-baserad rekryterar-PII-radering
(`RedactRecruiterPiiCommand` med `RecruiterIdentifierType.Email`) per ADR 0032
§8 amendment 2026-05-13. Name-baserad sökning är defererad eftersom den kräver:

1. **Multi-path jsonb-sökning** — namn kan stå i `employer.contact_name`,
   `recruiter.name`, eller fritext i `description.text`. Email-flödet har
   en strikt path; Name behöver disjunktion över flera paths.
2. **Full-text-search på `description.text`** — fritext-rester av rekryterar-namn
   kan bara hittas via Postgres `tsvector` + GIN-index för rimlig latens.
3. **False-positive-hantering** — vanliga namn ger många träffar; manuell
   granskning av kandidater behövs innan radering.

GDPR Art. 17 säger inte explicit att rekryterare måste kunna identifieras
*via namn* — email är primär identifier. Manuell DB-procedur dokumenterad i
`docs/runbooks/recruiter-pii-erasure.md` täcker edge-case tills auto-flödet
levereras.

**Trigger för fix:** första faktiska Name-baserade radering-begäran från en
rekryterare som inte kan tillhandahålla e-post.

**Föreslagen åtgärd:**

1. Utöka `RedactRecruiterPiiCommand`-handler med Name-branch — slå upp över
   flera jsonb-paths via `EF.Functions.JsonContains` med disjunktion.
2. Lägg till full-text-search-stöd för `description.text` via `tsvector`-kolumn
   + GIN-index (separat EF-migration).
3. Utvidga unit + integration-tester för Name-flöde.
4. Uppdatera `docs/runbooks/recruiter-pii-erasure.md` med auto-flödes-procedur.

**Beroenden:** Inga blockerare; auto-flöde kan implementeras isolerat när
trigger uppfyllt.


---

## TD-76: GIN-index på raw_payload jsonb (latens-trigger)
**Kategori:** Performance
**Severity:** Minor
**Fas:** Trigger
**Källa:** TD-73 prod-gating-batch CTO-decision 2026-05-13 (Q5 — YAGNI, defer index)

TD-73 prod-gating-batch levererade `RedactRecruiterPiiCommand`-endpoint som
söker `job_ads.raw_payload` via `EF.Functions.JsonContains` mot probe-jsonb.
CTO-decision Q5=B (defer GIN-index) motiverat med:

- Admin-endpoint är låg-frekvens (manuell trigger vid faktisk radering-request)
- Sekunder för seq-scan på ~5–10k rader är acceptabel latens för operations-handling
- GIN-index på `jsonb_path_ops` har reell daglig write-overhead på
  stream-cron-tick (~5–15k INSERTs/dygn netto efter UpdateFromSource)
- Cost-benefit är entydigt mot index vid F2-volym (Knuth 1974 — premature optimization)

**Trigger för fix:** ett av:

1. `POST /api/v1/admin/job-ads/redact-recruiter-pii` överstiger 5s response
   (mätbar via CloudWatch latens-metrik)
2. Stream-INSERT-volym växer 10× från P8c-baseline (~5–15k INSERTs/dygn → ~50–150k)
3. Search/filter-yta i Fas 2+ utvidgar `raw_payload`-läs-frekvens markant

**Föreslagen åtgärd:**

```sql
CREATE INDEX CONCURRENTLY ix_job_ads_raw_payload_gin
ON job_ads USING GIN (raw_payload jsonb_path_ops);
```

`jsonb_path_ops` är mer performant för `@>`/`@?`/`@@`-operatorer än default
`jsonb_ops` (web-verifierat 2026-05-13). `CONCURRENTLY` undviker write-lock
under index-bygge.

EF-migration: ny migration med raw SQL (EF Core 10 har ingen native GIN-stöd).
EXPLAIN ANALYZE-verifiering före och efter (mätbar speedup på admin-endpoint).

**Beroenden:** Inga blockerare; opportunistisk vid trigger.


---

## TD-74: Strikta DML-GRANTs på public + identity istället för GRANT ALL
**Kategori:** Säkerhet / Least Privilege
**Severity:** Minor
**Fas:** 2 (opportunistisk vid nästa Phase A-touch)
**Källa:** security-auditor F2-P8a-aggregat-review Sec-Minor-2 2026-05-12

`jobbpilot_app` får `GRANT ALL ON ALL TABLES` på `public` + `identity`-
schema. `ALL` inkluderar: SELECT, INSERT, UPDATE, DELETE, **TRUNCATE,
REFERENCES, TRIGGER**. För runtime är TRUNCATE (mass-delete utan WHERE) +
TRIGGER (DDL-yta) onödigt. Phase C `hangfire.*` till worker använder redan
korrekt strikt `GRANT SELECT, INSERT, UPDATE, DELETE`.

**Saltzer/Schroeder 1975:** strictare scope = mindre blast-radius vid
SQL-injection eller credential-läckage.

**Föreslagen åtgärd:** Ersätt `GRANT ALL` med `GRANT SELECT, INSERT, UPDATE,
DELETE` + `GRANT USAGE, SELECT, UPDATE ON SEQUENCES` i `Program.cs`
Phase A + Phase Bootstrap (rad ~448 + ~340). Kräver Phase A re-run vid
nästa creds-rotation eller bootstrap-fas. Sekvenser separat hantering.

**Severity-motivering Minor:** defense-in-depth-refactor utan tidsbundenhet.
TRUNCATE/TRIGGER-yta är teoretisk attack-surface, inte exploit-väg idag.

**Trigger:** nästa Phase A-touch (creds-rotation eller schema-bootstrap-
mutation).


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
| TD-30 | Domänköp + Route53 + ACM-cert | 2026-05-10 (retroaktivt stängd 2026-05-11) | STEG 13c + ADR 0027 |
| TD-10 | PII-läckage via `body?.detail` i Server Actions | 2026-05-11 | Batch A (`0560718`) |
| TD-11 | Hårdkodad E2E-lösenord och testemail på produktionsdomän | 2026-05-11 | Batch A (`0560718`) |
| TD-41 | Select-komponent-konvention — native vs shadcn Radix | 2026-05-11 | Batch B |
| TD-57 | Native form-controls divergerar från Input-primitive | 2026-05-11 | Batch B |
| TD-1 | Skip-link saknas i (app)-layout | 2026-05-11 | Batch C |
| TD-2 | CardTitle renderas utan heading-tag | 2026-05-11 | Batch C |
| TD-40 | Path-equality i `fieldA11y` — saknar regression-bevakning | 2026-05-11 (retroaktivt) | Batch C |
| TD-3 | Tom-state-copy "Inga roller tilldelade" saknar next-action | 2026-05-11 | Batch D |
| TD-4 | userId visas i UI utan tydligt användarbehov | 2026-05-11 | Batch D |
| TD-5 | Redundant getServerSession-anrop på /mig | 2026-05-11 | Batch D |
| TD-6 | Logout-backend-call utan fel-loggning | 2026-05-11 | Batch E |
| TD-28 | Frontend typed-confirmation-UX + re-auth-prompt på DELETE /me | 2026-05-11 | Batch E (fullstack) |
| TD-12 | Saknad integration-test för cross-user isolation | 2026-05-12 | Batch F |
| TD-65 | Playwright E2E för delete-account-flow | 2026-05-12 | disciplinretur |
| TD-66 | Cross-user-isolation-tester för Resume + JobSeeker | 2026-05-12 | disciplinretur |
| TD-67 | Audit-trail för failed cross-user-access-attempts | 2026-05-12 | ADR 0031 + IFailedAccessLogger |
| TD-25 | HardDeleteAccountsJob per-konto try/catch (resilient loop) | 2026-05-12 | `eed6cc2` |
| TD-68 | CloudWatch security-alarms för failed_access_attempt-events | 2026-05-12 | `70ca42b` + dev-apply |
| TD-69 | SesEmailSender (AWS SES) — disciplinretur, lyft + stängd samma dag | 2026-05-12 | F2-P0d disciplinretur (Klas-feedback) |
| TD-29 | Strict readiness-probe — separera liveness från readiness | 2026-05-12 | F2-P6 (6 nya integration-tester) |
| TD-56 | ListJobAdsQuery full paginering | 2026-05-12 | F2-P7 (TD-56 stängd, +9 unit + +3 integration-tester) |
| TD-73 | JobTech raw_payload PII-stripping + retention + audit-wire + right-to-erasure | 2026-05-13 | TD-73 prod-gating-batch (ADR 0035 + ADR 0032 amendment 2026-05-13) |
| TD-79 | ECS-service.task_definition strukturell drift mellan Terraform och deploy-dev.yml | 2026-05-13 | D+A-session (`lifecycle.ignore_changes` på api+worker services) |
| TD-70 | Search/filter-yta för JobAd-katalog (?ssyk&?region&?q) | 2026-05-13 | F2-P9 D+A-session (generated columns + ListReadPolicy rate-limit) |

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
