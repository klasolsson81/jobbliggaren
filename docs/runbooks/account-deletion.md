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
SQL-procedur i §4 tills dess).

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
7. **Post-commit:** `ISessionStore.InvalidateAllForUserAsync(userId)` invaliderar alla aktiva sessioner via Redis secondary-set
8. Klient får `204 No Content`

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

**Steg 0 — Orphan-cleanup:**
- Hitta `ApplicationUser` utan matchande `JobSeeker` (varken aktiv eller soft-deletad)
- För varje orphan: `UserManager.DeleteAsync`
- Plockar upp Identity-rader som hängde kvar från tidigare körning där Steg 2 h failade

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

### 3.2 Strukturerad logg (Seq i dev / CloudWatch i prod)

Filtrera på sourcecontext:

- `JobbPilot.Application.Auth.Jobs.HardDeleteAccounts.HardDeleteAccountsJob`
- `JobbPilot.Application.Auth.Commands.DeleteAccount.DeleteAccountCommandHandler`

Förväntade meddelanden vid lyckad körning (HardDeleteAccountsJob):

```
HardDeleteAccountsJob: rensade {N} Identity-orphans (Steg 0)
HardDeleteAccountsJob: hittade {N} konton mogna för hard-delete (cutoff YYYY-MM-DD)
HardDeleteAccountsJob: klart — {N} konton hard-deletade
```

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
-- KÖR i AppIdentityDbContext-schemat:
SELECT u.id, u.user_name
FROM identity.asp_net_users u
LEFT JOIN public.job_seekers js ON js.user_id = u.id
WHERE js.id IS NULL;
```

---

## 4. Manuell restore inom 30-dagars-fönstret

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

**Audit-trail:** restore-händelsen skrivs INTE automatiskt (saknas
`AccountRestored`-command i Fas 1). Logga manuellt i ops-channel.

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
- Men: aktiv session-token kan fortsätta auth:as för read-operationer tills den expirerar

**Åtgärd:**
1. Verifiera DB-state (§3.3) — JobSeeker.DeletedAt ska vara satt
2. Manuellt rensa Redis: `DEL jobbpilot:user:<userId>:sessions` + iterera
   och radera individuella `jobbpilot:session:*`-keys (om kända)
3. Eller acceptera och vänta på TTL — säkerhetsrisken är låg eftersom
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
- **TD-22** — app-logg-retention + IP/UA-redaction (innan prod-deploy)
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
