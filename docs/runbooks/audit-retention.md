# Audit-log retention — JobbPilot

Operativ runbook för `audit_log`-tabellens partitions-rotation och 90-dagars
retention. Implementerad i STEG 10a per [ADR 0024](../decisions/0024-audit-retention-and-art17-cascade.md)
delbeslut 1 + 2. Stänger del 1 av TD-16 (Art. 5(1)(e) Storage Limitation).

---

## 1. Översikt

`audit_log` är en native PostgreSQL-partitionerad tabell (`PARTITION BY RANGE
(occurred_at)`) med en partition per dag plus en default-partition.

**Två mekanismer håller retention:**

1. **`AuditLogRetentionJob`** — Hangfire `RecurringJob` som kör 03:00 UTC dagligen.
   Skapar morgondagens partition + droppar partitions vars datum är <
   `UTC.Now - 90 days`. Idempotent.
2. **Default-partition** — säkerhetsnät. Om jobbet failar fångas nya rader i
   default-partitionen tills nästa lyckade jobb-körning eller manuell move-
   procedur (se §4).

**Retention:** 90 dagar per [BUILD.md §7.1](../../BUILD.md) + ADR 0022 GDPR Art. 5(1)(e).

---

## 2. Normal drift

| Vad | Var | Frekvens |
|---|---|---|
| Skapa morgondagens partition | `AuditLogRetentionJob.RunAsync` (Worker) | 03:00 UTC daily |
| Droppa partitions > 90 dagar | samma jobb | 03:00 UTC daily |
| Bootstrap-partitions vid första deploy | Migration `20260508152351_AddAuditLogPartitioning` | Engångs |

Partitions namnges `audit_log_YYYYMMDD` (8-tecken fixed-width, lexikografiskt
sorterbart som datum).

---

## 3. Övervakning

### 3.1 Operatörsytan `/admin/jobb`

Hangfire-dashboardet exponeras inte — och kommer inte att göra det. #204 levererade
en egen civic operatörsyta i stället (`/admin/jobb`, PR #246; ADR 0082), vilket var ett
medvetet val framför den inbyggda dashboarden. Leta där efter recurring job
`audit-log-retention`:

- **Lyckat:** raden visar senaste körning + status **Lyckades** (rått Hangfire-state
  `Succeeded` i `GET /api/v1/admin/jobs/recurring`). **Körtiden syns inte
  på ytan** — läs den i den strukturerade loggen (§3.2); typiskt < 100 ms vid normal
  volym.
- **Fail:** status **Misslyckades** (rått `Failed`), och körningen listas i
  failed-tabellen med **undantagets
  TYPNAMN** som felkategori. Det finns **ingen stack-trace, inget meddelande och inga
  argument** — den frånvaron är en avsiktlig PII-kontroll (ADR 0082: undantagstexter
  kan bära personnummer, e-post eller DEK-dekrypterad CV-text), inte en lucka.
  **Lägg inte till dem.** Orsak utreds i loggen (§3.2) eller via direkt
  `hangfire.*`-SQL.
- **Requeue/trigger** finns som API-endpoints — `POST /api/v1/admin/jobs/failed/{jobId}/retry`
  och `POST /api/v1/admin/jobs/recurring/{id}/trigger` (PR #248) — **inte** som
  kontroller på sidan.

### 3.2 Strukturerad logg (Seq i dev och i prod)

Filtrera på sourcecontext `Jobbliggaren.Application.Common.Auditing.Jobs.AuditLogRetention.AuditLogRetentionJob`
(mätt mot `AuditLogRetentionJob.cs:4` — det gamla `JobbPilot.`-prefixet ger noll träffar sedan ADR 0069:s rename).

Förväntade meddelanden vid lyckad körning:

```
AuditLogRetentionJob: säkerställde partition audit_log_YYYYMMDD
AuditLogRetentionJob: inga partitions att droppa (cutoff YYYY-MM-DD)
```

Vid drop:

```
AuditLogRetentionJob: droppade partition audit_log_YYYYMMDD  (per partition)
AuditLogRetentionJob: droppade N partitions äldre än YYYY-MM-DD  (summary)
```

### 3.3 Verifiera partitions-state

Kör mot dev/prod-DB:

```sql
SELECT
    inhrelid::regclass::text AS partition_name,
    pg_get_expr(c.relpartbound, c.oid) AS bound
FROM pg_inherits i
JOIN pg_class c ON c.oid = i.inhrelid
WHERE inhparent = 'audit_log'::regclass
ORDER BY 1;
```

Förväntat: ~91 partitions (90 dagar bakåt + 1 framåt + default) plus
bootstrap-buffer-partitions tills retention etablerat sig.

### 3.4 Verifiera default-partition är tom

Default-partitionen ska inte ha rader i normal drift. Om den har det är
det en signal att jobbet failat ≥ 1 dag:

```sql
SELECT COUNT(*) FROM audit_log_default;
```

Förväntat: 0. Om > 0, se §4.2.

---

## 4. Failure-scenarier

### 4.1 Jobbet failer en enskild dag

**Symptom:** jobbet står som **Misslyckades** på `/admin/jobb`. Default-partitionen kan börja ta emot
rader om timing-fönstret missas.

**Åtgärd:**

1. Hangfire retry:ar automatiskt (default 10 retries med exponential backoff).
2. Om retries uttöms: kör om det misslyckade jobbet via
   `POST /api/v1/admin/jobs/failed/{jobId}/retry` (PR #248). Det finns ingen
   "Re-run"-knapp på `/admin/jobb` — ytan är read-side (ADR 0082), och den inbyggda
   Hangfire-dashboarden exponeras inte.
3. Verifiera att partitions skapats korrekt (§3.3).

**Risk:** Försumbar. PG hanterar default-partition-overflow rent.

### 4.2 Jobbet failat ≥ 1 dag och default-partitionen har rader

**Symptom:** `SELECT COUNT(*) FROM audit_log_default` > 0.

**Åtgärd — manuell move-procedur:**

```sql
BEGIN;

-- 1. Ta reda på vilka datum som finns i default
SELECT DATE(occurred_at) AS day, COUNT(*)
FROM audit_log_default
GROUP BY 1
ORDER BY 1;

-- 2. För varje saknad dag: skapa partition (anta 2026-05-15)
CREATE TABLE IF NOT EXISTS audit_log_20260515 PARTITION OF audit_log
    FOR VALUES FROM ('2026-05-15 00:00:00+00') TO ('2026-05-16 00:00:00+00');

-- 3. PG flyttar rader automatiskt från default till rätt partition vid
--    PARTITION OF-skapning OM rader passar inom range:t. Verifiera:
SELECT COUNT(*) FROM audit_log_default;  -- ska ha minskat

-- 4. Upprepa (2-3) tills default är tom

COMMIT;
```

**Skäl:** Att skapa range-partition som överlappar med rader i default
triggar PG:s automatiska re-route. Detta är **inte** dokumenterat som
publik API men är stabilt sedan PG11 och nyttjas i ops-runbooks i prod-
miljöer.

### 4.3 Jobbet failat ≥ 7 dagar (bootstrap-buffer förbrukad)

**Symptom:** Bootstrap-partitions (skapade vid initial migration) är slut.
Default-partitionen ackumulerar nya audit-skrivningar.

**Åtgärd:**

1. Kör manuell trigger via `POST /api/v1/admin/jobs/recurring/{id}/trigger`
   (PR #248). **Inte** via Worker-restart: `RecurringJobRegistrar.StartAsync` registrerar
   bara schemat för det här jobbet — metodens enda `Trigger` vid boot gäller
   `RefreshLandingStats` (rad 178) — så nästa körning blir 03:00, inte nu.
   Och inte via Hangfire-dashboarden, som inte exponeras (ADR 0082).
2. Om permanent fel: tillfälligt stäng av audit-skrivningar genom att flippa
   `IAuditableCommand`-implementationer (kräver hot-fix, inte standard ops).
3. Eskalera till Klas — det indikerar djupare DB-problem (disk full,
   permission-fel, etc.).

### 4.4 Ineffektiv DROP — partition droppas inte

**Symptom:** `DropPartitionsOlderThanAsync` returnerar tom lista trots att
det finns gamla partitions.

**Diagnos-query:**

```sql
SELECT c.relname,
       pg_get_expr(c.relpartbound, c.oid) AS bound
FROM pg_inherits i
JOIN pg_class c ON c.oid = i.inhrelid
WHERE inhparent = 'audit_log'::regclass
  AND c.relname ~ '^audit_log_[0-9]{8}$'
  AND c.relname < 'audit_log_' || to_char(now() - interval '90 days', 'YYYYMMDD')
ORDER BY 1;
```

Om query returnerar rader men jobbet inte droppar dem: bug. Logga issue
och eskalera. Manuell fallback:

```sql
-- För varje rad som ska droppas:
DROP TABLE IF EXISTS audit_log_YYYYMMDD;
```

---

## 5. Disaster recovery

### 5.1 Audit_log korrumperad / oavsiktligt droppad

**Inte realistiskt scenario** i Fas 1 — vi har ingen audit-data av värde
ännu. Vid prod-deploy: dagliga databas-snapshots. RDS är borta sedan ADR 0066 (dess
automated-backup-default var 7 dagars retention). **Backup-modellen på VPS är beslutad
sedan 2026-08-09 (ADR 0125, #197): nattlig age-krypterad `pg_dump` 02:15 UTC, splittad i
en huvudartefakt och en DEK-artefakt, retention 30 dagar.** Procedur och restore:
[`backup-restore.md`](backup-restore.md). Fönstret är Klas eget tal — **K4 = 30 dagar**,
beslutat 2026-08-04 — vilket besvarar **STOPP-4**, som ADR 0024 rad 636 och ADR 0032
rad 1335 uttryckligen förbjöd CC att uppfinna. Två saker kvarstår innan siffran är
*demonstrerbar* i Art. 5(2)-mening — men **skälet är inte längre att målet saknas.** Målet är
valt och mätt 2026-08-09 (OVHcloud Object Storage, `eu-west-par`) och 30-dagarsregeln
verkställs av leverantören. Kvar står **Art. 28-avtalet** (otecknat), **att credentialen kan
radera** (mätt; `vps-deploy-stack.md` §5 rad 27d) och att **DPIA-signaturen inte skrivits om** — kontrollera
`gdpr-processing-register.md` innan STOPP-4 räknas som stängd någon annanstans. Förväxla det inte med **audit-tabellens
egen retention, 90 dagar**, per §1 ovan (BUILD.md §7.1 + ADR 0022) — det är två skilda
storheter, och Art. 17-restore betjänas dessutom av soft-delete-rader i den LEVANDE
databasen (ADR 0024 D5), inte av snapshots.

### 5.2 Migration `AddAuditLogPartitioning` rollback

Down() återställer till icke-partitionerad tabell med PK = (id) och
behåller alla rader. Trigger:

```bash
export ConnectionStrings__Postgres="..."
dotnet ef database update <previous-migration> \
  --project src/Jobbliggaren.Infrastructure \
  --startup-project src/Jobbliggaren.Api \
  --context AppDbContext
```

**Kan inte köras vid Hangfire-jobb-aktivt-load** — kräver Worker-stopp först
för att undvika race med audit-skrivningar.

---

## 6. GDPR-noter

- **Retention** (Art. 5(1)(e)): 90-dagars-fönster är policy. Att förkorta
  kräver ADR-uppdatering + lagrings-konsekvens-bedömning.
- **Anonymisering** (Art. 17): partition-rotation tar inte hand om
  anonymisering vid kontoradering. Det görs av `IAuditTrailEraser` +
  `HardDeleteAccountsJob` (STEG 10b, ADR 0024 D3+D6).
- **Audit-paritet**: retention-jobbet skriver **inga** audit-rader för sin
  egen körning (medvetet — self-referential audit hade gett oändlig
  recursion vid kraschad audit_log). Spårbarheten ligger i Hangfire-
  loggen + strukturerad logg.

---

## 7. Referenser

- ADR 0022 — audit-log pipeline-behavior (write-only-disciplin)
- ADR 0023 — Worker-pipeline + Hangfire-infrastruktur (orchestrator-mönstret)
- [ADR 0024](../decisions/0024-audit-retention-and-art17-cascade.md) D1+D2 — partitioning-strategi
- BUILD.md §7.1 — `audit_log`-schema + 90-dagars retention
- TD-16 (audit-log retention + GDPR Art. 17-anonymisering) — STEG 10a stänger del 1.
  Historisk markör: TD-registret retirerades 2026-08-02 (ADR 0121) och TD-16 var redan stängd.
