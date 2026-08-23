# Konto-radering — JobbPilot

Operativ runbook för GDPR Art. 17-flödet (Right to erasure). Implementerad
i STEG 10b per [ADR 0024](../decisions/0024-audit-retention-and-art17-cascade.md)
delbeslut 3-6. Stänger del 2 av TD-16.

---

## 1. Översikt

Användaren raderar sitt konto via `DELETE /api/v1/me`. Flödet har två
faser:

| Fas | När | Vad | Vem |
|---|---|---|---|
| **Soft-delete** | Direkt vid `DELETE /me` | `DeletedAt` sätts på `JobSeeker` + alla `Application` + alla `Resume`. Audit-rad `Account.Deleted` skrivs. Sessioner invalideras. | Användaren via API |
| **Hard-delete** | Daily 04:00 UTC, efter 30 dagar | Cascade hard-delete (FK CASCADE). Audit-rader anonymiseras. ApplicationUser raderas från Identity. | `HardDeleteAccountsJob` (Hangfire) |

**Restore-fönster:** 30 dagar mellan soft-delete och hard-delete. Inom
fönstret kan kontot återställas (admin-yta planerad till Fas 6 — manuell
SQL-procedur i §4.1 tills dess).

---

## 2. Flöde steg för steg

### 2.1 Soft-delete (`DELETE /me`)

Användaren skickar `DELETE /api/v1/me` med giltigt session-token. Backend:

1. `DeleteAccountCommand` triggas (Mediator-pipeline)
2. Authorization-behavior verifierar `IAuthenticatedRequest`
3. Handler hämtar `JobSeeker` via `currentUser.UserId` (`IgnoreQueryFilters`)
4. **Idempotens-check:** om `DeletedAt is not null` → return Success (ingen ny audit-rad)
5. Cascade `SoftDelete(clock)` på alla `Application`, `Resume` (deras barn cascadar internt) och `JobSeeker`
6. `UnitOfWorkBehavior` committar alla soft-deletes + audit-rad atomic
7. **Post-commit (Layer 2 backstop, PR2c-0):** `ISessionStore.MarkUserDeletedAsync(userId)` planterar en per-user `jobbliggaren:user:{userId}:deleted`-tombstone (TTL = 30-dagars restore-fönstret) FÖRE invalideringen — `GetAsync` fail-closed-avvisar (och self-heal-evicerar) varje session som överlever en partiell invalidering (Redis-blip / race), så läs-vägens Art. 17-radering håller även om fast-path-invalideringen delvis failar
8. **Post-commit (fast path):** `ISessionStore.InvalidateAllForUserAsync(userId)` invaliderar alla aktiva sessioner via Redis secondary-set
9. Klient får `204 No Content`

### 2.2 Login under restore-fönstret

Användaren försöker logga in inom 30 dagar:

1. `LoginCommandHandler.Handle` validerar credentials via `UserManager`
2. Hämtar `JobSeeker` (`IgnoreQueryFilters`) för userId
3. Om `DeletedAt is not null`: returnerar `Auth.InvalidCredentials` (401) — **inte** "account-pending-deletion"
4. Audit-rad `LoginFailed` skrivs

**Viktigt:** felmeddelandet är identiskt med "okänd email" / "fel lösen"
för att undvika information disclosure (security-auditor STEG 10b Sec-1).
Användaren får ingen indikation att kontot är raderat — kontaktar support
out-of-band om de vill återställa.

### 2.3 Hard-delete (`HardDeleteAccountsJob`)

Hangfire-jobb kör 04:00 UTC daily. Tre steg:

**Steg 0 — Orphan-cleanup (#508 grace-fönster + reverse-orphan-detektor):**
- Hitta `ApplicationUser` utan matchande `JobSeeker` (varken aktiv eller soft-deletad)
- **Grace-fönster (1 h):** sopa ENDAST en JobSeeker-lös Identity-user som är ÄLDRE än
  `now − 1h` (`ApplicationUser.CreatedAt`). En yngre presumeras vara mid-registrering —
  registrering commit:ar Identity-raden FÖRE JobSeeker-raden (ADR 0024 två-boundary, med en
  Redis-roundtrip i fönstret), så en helt färsk JobSeeker-lös Identity-user är förväntad och
  transient. Utan fönstret raderades ett live-konto mitt i registrering permanent (#508 TOCTOU).
- För varje mogen orphan: `UserManager.DeleteAsync`
- Plockar upp Identity-rader som hängde kvar från tidigare körning där Steg 2 h failade
- **Reverse-orphan-detektor (defense-in-depth, log-only):** en `JobSeeker` vars `UserId` saknar
  Identity-user (spegelbilden av samma race — ett utelåst konto som ej kan utöva Art. 17) LOGGAS
  (Warning, count-only) men RADERAS ALDRIG här. Remediation (åter-länkning/radering) ägs av #1409. (#524 stängdes 2026-07-10 och handlade om sentinel-kolliderande klartextrader från #500-fixen — en annan sak; pekaren var död, mätt 2026-08-19.)

**Steg 1 — Hämta mogna konton:**
- `JobSeeker WHERE deleted_at < (UTC.Now - 30 days)` (`IgnoreQueryFilters`)
- Returnerar lista av JobSeeker-IDs

**Steg 2 — Per JobSeeker (transactional):**
1. `BeginTransactionAsync`
2. `IAuditTrailEraser.AnonymizeUserAuditTrailAsync(userId)` — UPDATE audit_log SET user_id/ip_address/user_agent = NULL WHERE user_id = userId
3. Hard-delete `Application` + `Resume` (FK CASCADE tar barnen)
4. Hard-delete `JobSeeker`
5. `SaveChangesAsync` + `Commit`
6. **Separat boundary:** `UserManager.DeleteAsync(applicationUser)` — om denna failar plockas raden upp av Steg 0 nästa körning

---

## 3. Övervakning

### 3.1 Hangfire dashboard

Recurring job: `hard-delete-accounts`. Körtid varierar med antal mogna
konton (typiskt < 1s/konto för Fas 1-volym).

### 3.2 Strukturerad logg (Seq i dev och i prod)

Filtrera på sourcecontext:

- `Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts.HardDeleteAccountsJob`
- `Jobbliggaren.Application.Auth.Commands.DeleteAccount.DeleteAccountCommandHandler`

Förväntade meddelanden vid lyckad körning (HardDeleteAccountsJob):

```
HardDeleteAccountsJob: rensade {N} Identity-orphans (Steg 0)
HardDeleteAccountsJob: hittade {N} konton mogna för hard-delete (cutoff YYYY-MM-DD)
HardDeleteAccountsJob: klart — {N} konton hard-deletade
```

Vid reverse-orphan (defense-in-depth, `AccountHardDeleter`, EventId 2503, Warning):

```
CleanupIdentityOrphansAsync: {N} reverse-orphan JobSeeker(s) saknar Identity-user (utelåst
konto, kan ej utöva Art. 17) — loggas för utredning, raderas ej här (#1409)
```

Vid misslyckad orphan-radering (`AccountHardDeleter`, EventId 2504, Warning):

```
AccountHardDeleter: kunde inte radera Identity-orphan {OrphanId} ({ErrorCodes}) - raden ligger
kvar och ingår inte i 'cleaned'-talet
```

Raden fanns inte före #1349: sveptets `DeleteAsync` kastade sitt `IdentityResult`, så N
systematiskt misslyckade raderingar syntes som "rensade 0 Identity-orphans" — omöjligt att
skilja från "hittade inga". Till skillnad från 2503 är denna **inte** count-only; den bär
`{OrphanId}` just för att remedieringen är nycklad på id:t.

⚠ **En av de två populationerna bakom raden är en ofullbordad Art. 17-radering.** Domän-erasure
och DEK-destruktion committas i en transaktion, Identity-raden raderas på en separat boundary,
och faller den plockas raden upp av Steg 0 nästa körning. Ser du 2504 för samma `{OrphanId}` två
körningar i rad har den retryn också fallit, och **raderingen enligt Art. 17(1) är då inte
fullbordad**. Art. 12(3):s månadsfrist räknas från BEGÄRAN (soft-delete), inte från den här
loggraden, och är efter 30-dagarsfönstret i normalfallet redan förbrukad — eskalera direkt,
förläng inte tyst.

⚠ Kör §3.3:s **Identity-orphan-query**, inte reverse-orphan-queryn. 2504 fyrar på `orphanIds`,
alltså en `ApplicationUser` UTAN `JobSeeker` — reverse-orphan-queryn selekterar spegel-
populationen (2503:s) och kan per konstruktion inte innehålla raden. En operatör som kör fel
query hittar inte id:t och riskerar att stänga ärendet som "raden är borta".

### 3.3 Verifiera flöde-state

```sql
-- Soft-deletade konton (väntar på hard-delete-fönster eller restore)
SELECT
    js.id, js.user_id, js.deleted_at,
    EXTRACT(DAY FROM (NOW() - js.deleted_at)) AS days_since_delete
FROM job_seekers js
WHERE js.deleted_at IS NOT NULL
ORDER BY js.deleted_at;

-- Audit-rader anonymiserade (post-hard-delete)
SELECT COUNT(*) AS anonymized_rows
FROM audit_log
WHERE user_id IS NULL AND aggregate_type = 'JobSeeker';

-- Identity-orphans (ApplicationUser utan JobSeeker — bör vara 0)
-- OBS #508 grace-fönster: en rad < 1h gammal (created_at) är förväntad mid-registrering,
-- INTE en orphan. Filtrera bort den för att matcha vad sweepen faktiskt agerar på:
-- KÖR i AppIdentityDbContext-schemat:
SELECT u.id, u.user_name, u.created_at
FROM identity.asp_net_users u
LEFT JOIN public.job_seekers js ON js.user_id = u.id
WHERE js.id IS NULL
  AND u.created_at <= NOW() - INTERVAL '1 hour';

-- Reverse-orphans (#508): JobSeeker vars UserId saknar Identity-user (utelåst konto).
-- Motsvarar Warning-loggens count (EventId 2503). En LITEN count kan vara TRANSIENT: en
-- samtidig registrering som racear sweepens två snapshot-läsningar ger en spurios rad utan
-- att något är fel → UTRED, behandla inte som incident. En ihållande/växande count = en
-- verklig lucka (#1409).
SELECT js.id, js.user_id
FROM public.job_seekers js
LEFT JOIN identity.asp_net_users u ON u.id = js.user_id
WHERE u.id IS NULL;
```

---

## 4. Manuella åtgärder utanför appen

### 4.1 Restore inom 30-dagars-fönstret

**Fas 6 admin-yta saknas tills vidare** — restore sker manuellt via SQL.

Användare kontaktar support inom 30 dagar. Support verifierar identitet
out-of-band (fysisk legitimation, eller verifierings-email till
alternative address). Sedan:

```sql
BEGIN;

-- 1. Hitta soft-deletad JobSeeker
SELECT id, user_id, display_name, deleted_at
FROM job_seekers
WHERE user_id = '<userId>'::uuid
  AND deleted_at IS NOT NULL;

-- 2. Restore JobSeeker + alla soft-deletade aggregat
UPDATE job_seekers SET deleted_at = NULL
WHERE user_id = '<userId>'::uuid;

UPDATE applications SET deleted_at = NULL
WHERE job_seeker_id IN (
    SELECT id FROM job_seekers WHERE user_id = '<userId>'::uuid
);

UPDATE follow_ups SET deleted_at = NULL
WHERE application_id IN (
    SELECT a.id FROM applications a
    JOIN job_seekers js ON js.id = a.job_seeker_id
    WHERE js.user_id = '<userId>'::uuid
);

UPDATE application_notes SET deleted_at = NULL
WHERE application_id IN (
    SELECT a.id FROM applications a
    JOIN job_seekers js ON js.id = a.job_seeker_id
    WHERE js.user_id = '<userId>'::uuid
);

UPDATE resumes SET deleted_at = NULL
WHERE job_seeker_id IN (
    SELECT id FROM job_seekers WHERE user_id = '<userId>'::uuid
);

UPDATE resume_versions SET deleted_at = NULL
WHERE resume_id IN (
    SELECT r.id FROM resumes r
    JOIN job_seekers js ON js.id = r.job_seeker_id
    WHERE js.user_id = '<userId>'::uuid
);

-- 3. Verifiera state
SELECT 'jobseeker' AS tbl, COUNT(*) FROM job_seekers WHERE user_id = '<userId>'::uuid AND deleted_at IS NULL
UNION ALL
SELECT 'applications', COUNT(*) FROM applications a JOIN job_seekers js ON js.id = a.job_seeker_id
WHERE js.user_id = '<userId>'::uuid AND a.deleted_at IS NULL;

COMMIT;
```

**Redis-tombstone (PR2c-0 Layer 2) — OBLIGATORISKT vid restore:** soft-delete
planterade en `jobbliggaren:user:<userId>:deleted`-tombstone som `GetAsync`
fail-closed-avvisar ALLA sessioner mot (inklusive färska sessioner efter en ny
login — `GetAsync` kollar tombstonen, `CreateAsync` gör det inte). Rensa den efter
SQL-restore, annars kan den återställda användaren inte hålla sig inloggad förrän
tombstonen självdör (≤30 dagar): `DEL jobbliggaren:user:<userId>:deleted`. En
framtida `AccountRestored`-command (Fas 6) MÅSTE anropa motsvarande rensning.

**Audit-trail:** restore-händelsen skrivs INTE automatiskt (saknas
`AccountRestored`-command i Fas 1). Logga manuellt i ops-channel.

---

### 4.2 Radera en reverse-orphan (#1409)

En **reverse-orphan** är en `JobSeeker` vars `UserId` saknar Identity-user. Den upptäcks av Steg 0:s
detektor (EventId 2503, Warning, count-only) och **raderas aldrig av något jobb**: ingen kodväg sätter
`deleted_at` på den, så 30-dagarsfönstret startar aldrig och `HardDeleteAccountsJob` plockar aldrig upp
den. Utan proceduren nedan besvaras en Art. 17-begäran med ad-hoc-SQL.

⚠ **Skriv INTE en egen raderingskaskad i SQL.** `HardDeleteAccountAsync` raderar varje användarägt
aggregat plus DEK:erna plus audit-anonymiseringen, och att listan är komplett maskinkontrolleras av
`AccountHardDeleteCascadeFitnessTests`. Proceduren **återinträder i den vaktade vägen** i stället.

**Steg 1 — identifiera raden.** Använd reverse-orphan-queryn i §3.3. Den ger `js.id`, som är det
`jobSeekerId` stegen nedan tar. ⚠ En LITEN count kan vara transient (en samtidig registrering som
racear sweepens två snapshot-läsningar) — utred före du raderar.

**Steg 2 — sätt raderingstriggern.** Det är hela ingreppet; jobbet gör resten.

```sql
-- Utan Art. 17-begäran (Art. 5.1 e, lagringsminimering): NOW().
-- 30-dagarsfönstret kostar ingenting här (ingenting är återställbart ändå) och skyddar mot
-- oåterkallelig radering av en felidentifierad rad.
-- AND deleted_at IS NULL: §3.3:s query varken filtrerar eller selekterar deleted_at, så raden kan
-- redan ha en löpande klocka. Utan villkoret nollställs den och Art. 17 fördröjs 30 dagar till.
-- NOT EXISTS: rubriken "reverse-orphan" är dokumentation, inte en grind. Utan den skulle ett
-- felklistrat id kunna soft-deleta ett LEVANDE konto. Villkoret är §3.3:s egen definition,
-- körbar. NOLL rader betyder ANTINGEN att klockan redan går, ATT raden inte är en
-- reverse-orphan, ELLER att id:t är fel. Det är inget misslyckande, och den backdaterade
-- satsen nedan är inte botemedlet. Läs av vilket det är:
--   SELECT deleted_at, user_id FROM job_seekers WHERE id = '<jobSeekerId>'::uuid;
--   (ingen rad = fel id · deleted_at satt = klockan går · annars: kör §3.3 igen)
UPDATE job_seekers SET deleted_at = NOW()
WHERE id = '<jobSeekerId>'::uuid AND deleted_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM identity.asp_net_users u WHERE u.id = job_seekers.user_id);
```

```sql
-- Vid VERIFIERAD Art. 17-begäran: backdatera förbi cutoff så raderingen körs nästa pass
-- (Art. 17.1, "utan onödigt dröjsmål" — annars väntar den registrerade 30 dagar i onödan).
-- Samma grind: satsen KORTAR ett restore-fönster, så ett felklistrat id skulle kunna korta ett
-- vanligt soft-deletat kontos fönster till nästa pass, utan begäran bakom sig. Inget
-- deleted_at IS NULL här: en verifierad Art. 17-begäran ska kunna korta en redan löpande klocka.
UPDATE job_seekers SET deleted_at = NOW() - INTERVAL '31 days'
WHERE id = '<jobSeekerId>'::uuid
  AND NOT EXISTS (SELECT 1 FROM identity.asp_net_users u WHERE u.id = job_seekers.user_id);
```

⚠ **"Verifierad" har en högre tröskel här än i §4.1, och §4.1:s båda metoder är otillgängliga.** Det
finns inget konto att legitimera sig mot (Identity-raden är per definition borta) och ingen alternativ
adress på fil — domäntabellerna bär ingen. Kvar på raden är `display_name` i klartext, som varken är
unikt eller verifierat, och DEK-krypterat CV-innehåll som **inte** får dekrypteras för att avgöra vems
det är.

**Går kopplingen inte att verifiera: radera inte.** Det är rätt svar, inte ett undantag — Art. 11.2
(kan den personuppgiftsansvarige inte identifiera den registrerade gäller Art. 15–20 inte) och
Art. 12.6 (begär kompletterande uppgifter). Backdatering på en overifierad matchning förstör en
**annan** registrerads CV och DEK inom ett dygn, utan återvändo.

⚠ **Det backdaterade `deleted_at` är en operativ trigger, inte ett sant faktum om när raderingen
begärdes.** När kolumnen medvetet görs osann är begäransposten den enda riktiga uppgiften om det
faktum Art. 12.3:s månadsfrist och Art. 17.1:s "utan onödigt dröjsmål" mäts mot (Art. 5.2).
**Hem:** den personuppgiftsansvariges ärendeakt, utanför systemet — samma hem som ROPA-posten
*"Art. 17-raderingsbegäran från rekryterare"* redan namnger för klartext-identifierare.
**Läsare:** Klas, som personuppgiftsansvarig. En chattkanal som åldras ut är inget hem.

**Steg 3 — vänta in jobbet. Tidslinjen skiljer sig mellan varianterna.** Jobbet kör dagligen; tiden
står i §2.3.

- **`NOW()`-varianten:** raden raderas vid första passet **efter 30 dagar**, inte i natt — Steg 1 i
  §2.3 kräver ett `deleted_at` som är äldre än fönstret. Att trigga jobbet manuellt gör därför
  ingenting före dess. Under fönstret ligger raden kvar i §3.3:s query och **EventId 2503 fortsätter
  fyra varje natt**. Det är förväntat, inte ett fel.
  ⚠ Anteckna raden du armerat så nästa läsare kan räkna bort den.
- **Backdaterade varianten:** raden raderas vid nästa pass, och en manuell körning via
  Hangfire-dashboarden (§3.1) är meningsfull.

**Verifiera** när fönstret för din variant löpt ut: §3.3:s reverse-orphan-query ska inte längre
returnera raden.

**Redis-tombsten: normalt INGEN åtgärd — men läs villkoret, det är inte ovillkorligt.** Till skillnad
från §4.1, som **kräver** att tombsten rensas, behöver den här proceduren normalt ingen alls: varje
producent av en reverse-orphan som finns i `src/` uppstår **innan registreringen slutförts** — sweepen
i Steg 0 tar en mid-registrerings-rad vars `JobSeeker` sedan commit:as, antingen vid klockskevhet
eller när registreringen stannar längre än grace-fönstret. Ingen inloggning har skett, så ingen
session kan finnas.

⚠ **Det gäller producenterna i koden, inte en Identity-rad som raderats direkt i databasen.**
`GetAsync` kontrollerar tombsten och **läser aldrig Identity-raden**, och tombsten planteras av exakt
ett ställe (kontoraderingen i §2.1) — `AccountHardDeleter` planterar ingen. En sådan rad kan därför ha
en **levande session kvar**. Vet du inte hur raden uppstod, plantera tombsten före Steg 2. TTL:en ska
matcha `SessionStoreOptions.DeletionTombstoneTtl`, som är sanningskällan (30 dagar som standard):

```
SET jobbliggaren:user:<userId>:deleted 1 EX 2592000
EXISTS jobbliggaren:user:<userId>:deleted   -- ska ge 1
```

⚠ Redis-nycklar är skiftlägeskänsliga och koden bygger nyckeln ur en GUID i gemener. En versaliserad
`<userId>` planterar en nyckel ingen läser, utan felmeddelande — därav `EXISTS`-raden.

---

## 5. Failure-scenarier

### 5.1 DELETE /me 5xx vid Redis-fel

**Symptom:** `DELETE /me` returnerar 500 efter `204` har "borde returnerats".

**Orsak:** `DeleteAccountCommand` lyckades (DB committad), men
`InvalidateAllForUserAsync` failade med `SessionStoreUnavailableException`.

**Påverkan:**
- Kontot ÄR soft-deletat (DB-state korrekt)
- Sessioner kan kvarstå tills sliding-expiry (default 14 dagar)
- D5-blockering hindrar ny login → ingen säkerhetsrisk inom samma session
- **Läs-vägen är dock stängd av Layer 2-tombstonen (PR2c-0):** `MarkUserDeletedAsync`
  körs FÖRE `InvalidateAllForUserAsync`, så i detta scenario (invalideringen failade)
  är `jobbliggaren:user:<userId>:deleted` redan planterad → `GetAsync`
  fail-closed-avvisar (och evicerar) kvarvarande sessioner. Endast om Redis var nere för
  BÅDA anropen (500:an kommer då från `MarkUserDeletedAsync`) saknas tombstonen — se Åtgärd steg 3.

**Åtgärd:**
1. Verifiera DB-state (§3.3) — JobSeeker.DeletedAt ska vara satt
2. Manuellt rensa Redis: `DEL jobbliggaren:user:<userId>:sessions` + iterera
   och radera individuella `jobbliggaren:session:*`-keys (om kända)
3. Om Redis var helt nere vid delete (tombstonen planterades aldrig): plantera den
   manuellt så läs-vägen stängs — `SET jobbliggaren:user:<userId>:deleted 1 EX 2592000` (30 dagar)
4. Eller acceptera och vänta på TTL — säkerhetsrisken är låg eftersom
   aktiv session bara har user:s egna data och D5 blockerar ny inloggning

### 5.2 HardDeleteAccountsJob failar mid-loop

**Symptom:** Hangfire-dashboard visar "Failed". Vissa konton hard-deletade,
andra kvar.

**Orsak:** Per-konto exception (DB-lock, FK-violation, etc.) bubblar och
avbryter loopen för alla efterföljande konton (TD-25 — per-konto try/catch
saknas).

**Åtgärd:**
1. Hangfire retry:ar automatiskt (default 10 retries)
2. Vid persistent failure: undersök stack-trace i logg, åtgärda root-cause
3. Re-trigger jobbet manuellt via Hangfire-dashboard

### 5.3 Identity-DELETE failar (Steg 2 h)

**Symptom:** Domain-aggregat hard-deletade men ApplicationUser kvarstår.

**Påverkan:** Email kvarstår som UNIQUE i Identity → user kan INTE
re-registrera under cleanup-fönstret. Audit-trail anonymiserad.

**Åtgärd:**
- Steg 0 (orphan-cleanup) i nästa daily-run plockar upp orphanen automatiskt
- Inget manuellt ingripande krävs förrän det blir > 24h gammalt
- Vid permanent fail: manuell `DELETE FROM identity.asp_net_users WHERE id = '<userId>'`

### 5.4 Audit-anonymisering failar

**Symptom:** Hard-delete kommit men audit-rader kvar med user_id.

**Orsak:** `IAuditTrailEraser.AnonymizeUserAuditTrailAsync` failade i
transactionen → hela transactionen rollback:as → JobSeeker kvar.

**Åtgärd:** Hangfire retry plockar upp i nästa körning. Inget manuellt
ingripande krävs.

---

## 6. GDPR-noter

- **Art. 17 (Right to erasure):** uppfylls via 30-dagars-fönster + hard-
  delete + audit-anonymisering
- **Art. 17(3)(b) + Art. 5(2) (accountability):** anonymiserade audit-
  rader bevaras 90 dagar för legal-process-krav. Efter 90 dagar tar
  `AuditLogRetentionJob` bort dem via partition-DROP
- **Art. 5(1)(c) (data minimization):** anonymisering sätter user_id,
  ip_address, user_agent till NULL. Behåller correlation_id, event_type,
  aggregate_type, aggregate_id, occurred_at för accountability
- **Anonymiserings-tidpunkt:** vid hard-delete (efter 30 dagar), inte vid
  soft-delete. Skäl: under restore-fönstret ska användaren kunna se sin
  egen audit-historik om kontot återställs
- **Re-registration:** blockerad i 30 dagar (UNIQUE email i Identity
  bevaras tills hard-delete). Skyddar mot email-recycling-attacks och
  bevarar audit-trail-länken

---

## 7. Tech-debt-länkar

- **TD-16** (audit-retention + Art. 17) — del 1 stängd via STEG 10a, del 2 stängd via STEG 10b
- **TD-21** — rate-limiting på DELETE /me + auth-endpoints (innan prod-deploy)
- **TD-22** → [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170) — app-logg-retention. Seq-benet levererat 2026-08-23 (30-dagarspolicy på produktionslådan); `json-file` är fortfarande åldersobundet och är vad som håller issuen öppen. IP/UA-redaction är levererad (`IIpAnonymizer`, ADR 0024 D7 policy 2)
- **TD-23** — Redis MULTI/EXEC för CreateAsync atomicitet (Fas 2)
- **TD-24** — DeleteAccountCommand cascade-paginering (Fas 4)
- **TD-25** — HardDeleteAccountsJob per-konto try/catch (opportunistiskt)

---

## 8. Referenser

- ADR 0017 — Frontend Authentication Pattern (sessions deferred-not stängd)
- ADR 0022 — Audit-log pipeline-behavior + Art. 17-policy
- ADR 0023 — Worker-pipeline + Hangfire-infrastruktur
- [ADR 0024](../decisions/0024-audit-retention-and-art17-cascade.md) D3-D6 — denna runbook implementerar
- BUILD.md §7.3 — soft delete-strategi
- BUILD.md §13.3 — GDPR-flöden
- [`audit-retention.md`](audit-retention.md) — relaterad runbook (90-dagars retention)
