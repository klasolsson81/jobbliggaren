# Hangfire schema + Worker prod-härdning — JobbPilot

Operativ runbook för Hangfire-PostgreSQL-schemats lifecycle, GRANT-modell,
ConnectionStrings-split, dashboard-säkerhet och Fargate SIGTERM-handling.
Implementerar TD-17 (Fas 1 prod-deploy-blockare) per
[ADR 0023](../decisions/0023-worker-pipeline-and-hangfire.md) +
[ADR 0024](../decisions/0024-audit-retention-and-art17-cascade.md).

---

## 1. Översikt

Hangfire-storage är PostgreSQL via `Hangfire.PostgreSql`. Schema-namnet är
`hangfire` (separerat från `public` för att undvika kolumn-konflikter).

**STEG 6 Plan B-uppdatering 2026-05-24:** `ConnectionStrings__HangfireStorage`
konsumeras av BÅDA Worker-processen (jobb-execution via `HangfireServer`) OCH
Api-processen (`IBackgroundJobClient.Enqueue` för admin-endpoint
`/api/v1/admin/job-ads/backfill-ssyk`). Rollen `jobbpilot_worker` förblir
hangfire-only (PUBLIC revoke:ad, ingen `jobbpilot_app`-inheritance — se §4).
Roll-namnet är legacy; renamning planerad i STEG 14 prod-DDL-cutover (TD-99).

**Två schema-lifecycle-strategier:**

| Miljö | `PrepareSchemaIfNecessary` | Schema-skapnings-väg |
|---|---|---|
| Dev / test | `true` (default) | Worker skapar schema vid uppstart |
| Prod | `false` (krav per TD-17) | Manuell DDL via §3 + Worker-DB-user med minimal GRANT-set |

Production-defense i `Worker/Program.cs`: kastar `InvalidOperationException`
vid uppstart om `IsProduction() && PrepareSchemaIfNecessary` — fail-loud om
prod-overlay glömt sätta `false`.

---

## 2. Normal drift

| Vad | Var | Frekvens |
|---|---|---|
| Schema skapas | Worker uppstart (dev) eller manuell DDL (prod) | Engångs per miljö |
| Recurring jobs registreras | `RecurringJobRegistrar` (Worker host start) | Vid varje deploy |
| `audit-log-retention` | 03:00 UTC daily | — |
| `detect-ghosted` | 03:00 UTC daily | — |
| `hard-delete-accounts` | 04:00 UTC daily | — |

---

## 3. Initial schema-DDL (prod, körs en gång innan första Worker-deploy)

Hangfire.PostgreSql packar schema-skapnings-DDL i sin `Install.sql`. Vid prod
ska migrations-användaren (DDL-rättigheter) köra denna manuellt. Worker
runtime-användaren ska INTE ha CREATE-rättigheter.

**Steg 1 — exportera Install.sql från NuGet-paketet:**

JobbPilot använder Central Package Management — `Directory.Packages.props` är
auktoritativ versions-källa, inte enskilda csproj.

```bash
# Linux / macOS (GNU grep krävs; macOS native grep saknar -P, installera via brew install grep
# eller ersätt nedan med sed/awk):
HANGFIRE_PG_VERSION=$(grep -oP '"Hangfire\.PostgreSql"[^>]*Version="\K[^"]+' \
    Directory.Packages.props)
INSTALL_SQL="$HOME/.nuget/packages/hangfire.postgresql/${HANGFIRE_PG_VERSION}/tools/Install.v22.sql"

# Windows PowerShell
$version = (Select-Xml -Path Directory.Packages.props `
    -XPath "//PackageVersion[@Include='Hangfire.PostgreSql']/@Version").Node.Value
$installSql = "$env:USERPROFILE\.nuget\packages\hangfire.postgresql\$version\tools\Install.v22.sql"

cat $INSTALL_SQL | head -20  # eller: Get-Content $installSql -TotalCount 20
```

**Steg 2 — kör som migrations-user (DDL-rättigheter):**

```bash
psql "host=<rds-endpoint> dbname=jobbpilot user=jobbpilot_migrations" \
    -v SchemaName=hangfire \
    -f $INSTALL_SQL
```

**Steg 3 — verifiera:**

```sql
SELECT schemaname, tablename
FROM pg_tables
WHERE schemaname = 'hangfire'
ORDER BY tablename;

-- Förväntat: ~13 tabeller (job, jobparameter, jobqueue, schema, server,
-- set, list, hash, counter, aggregatedcounter, lock, signal, state)
```

---

## 4. GRANT-modell — least-privilege i prod

**Roller:**

```sql
-- STEG 0 — REVOKE PUBLIC innan något GRANT (TD-17, security-auditor STEG 11 Sec-Major-2).
-- Postgres-default ger PUBLIC-rollen läsbara-yta på new schemas. Måste explicit revokeas
-- så framtida roller inte ärver Hangfire-access via PUBLIC-medlemskap.
REVOKE ALL ON SCHEMA hangfire FROM PUBLIC;
REVOKE ALL ON DATABASE jobbpilot FROM PUBLIC;

-- STEG 1 — Migrations-roll: kör Install.sql + framtida schema-uppgraderingar.
-- CREATE bara på hangfire-schemat (inte hela databasen) — least-privilege.
CREATE ROLE jobbpilot_migrations LOGIN PASSWORD '<from-secrets-manager>';
GRANT CONNECT ON DATABASE jobbpilot TO jobbpilot_migrations;
GRANT CREATE, USAGE ON SCHEMA hangfire TO jobbpilot_migrations;

-- (En gång vid bootstrap, om hangfire-schemat inte ännu existerar:)
--   som postgres/superuser:  CREATE SCHEMA hangfire AUTHORIZATION jobbpilot_migrations;
-- Därefter behöver migrations-rollen aldrig CREATE ON DATABASE.

-- STEG 2 — Worker runtime-roll: bara DML på hangfire.* (TD-17 punkt 4).
CREATE ROLE jobbpilot_worker LOGIN PASSWORD '<from-secrets-manager>';
GRANT CONNECT ON DATABASE jobbpilot TO jobbpilot_worker;
GRANT USAGE ON SCHEMA hangfire TO jobbpilot_worker;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hangfire TO jobbpilot_worker;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA hangfire TO jobbpilot_worker;

-- STEG 3 — Default privileges för framtida tabeller (skapade av migrations-roll).
-- Krav: alla framtida hangfire-DDL i prod MÅSTE köras som jobbpilot_migrations
-- (annars ärver jobbpilot_worker inte rättigheter på nya tabeller/sequencer).
ALTER DEFAULT PRIVILEGES FOR ROLE jobbpilot_migrations
    IN SCHEMA hangfire
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO jobbpilot_worker;
ALTER DEFAULT PRIVILEGES FOR ROLE jobbpilot_migrations
    IN SCHEMA hangfire
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO jobbpilot_worker;
```

**ConnectionStrings split (TD-17 punkt 4):**

I prod splittas `ConnectionStrings` i två — en per JobbPilot-tabell-yta och
en per Hangfire-yta. Lateral access-yta minskar.

```jsonc
// appsettings.Production.json (overlay) eller AWS Secrets Manager
{
  "ConnectionStrings": {
    "Postgres": "Host=...;Database=jobbpilot;Username=jobbpilot_app;Password=...",
    "HangfireStorage": "Host=...;Database=jobbpilot;Username=jobbpilot_worker;Password=..."
  }
}
```

**Worker/Program.cs-ändring vid första prod-deploy** (TODO för Fas 0-stängning):

```csharp
var hangfireConnectionString = builder.Configuration.GetConnectionString("HangfireStorage")
    ?? builder.Configuration.GetConnectionString("Postgres")  // fallback dev
    ?? throw new InvalidOperationException("...");
```

I dev/test fortsätter vi använda en enda `Postgres`-string (ingen split-kostnad
lokalt). Defererat tills första prod-deploy.

---

## 5. Hangfire dashboard — SECURITY (TD-17 punkt 3)

**Dashboard exponeras INTE i Fas 1.** Worker hostar inte HTTP. Om dashboard
någonsin införs (i Api eller dev-tooling) gäller följande:

**Hot:**

- Hangfire-default är **publik** — `app.UseHangfireDashboard("/hangfire")`
  utan auth ger fritt fram för alla med URL.
- Dashboard exponerar:
  - **Job arguments** — kan innehålla user-IDs, aggregat-IDs, business-data
  - **Stack-traces** vid fail — potentiellt PII i exception-Message
  - **Server-state** — connection-strings (maskade men förekomst-info)
  - **Recurring job-konfiguration** — cron-tider, hosting-internals

**Krav vid aktivering (komplett checklista per security-auditor STEG 11 Sec-Major-3):**

1. **Auth + audit**

```csharp
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AdminOnlyDashboardFilter() },
    IgnoreAntiforgeryToken = false,  // Hangfire 1.8+: default false; verifiera vid version-bump
});
```

`AdminOnlyDashboardFilter` ska kräva `[Authorize(Policy = "Admin")]` och
audit-logga varje dashboard-access. Granulära events:

- `Admin.HangfireDashboard.Accessed`
- `Admin.HangfireJob.RetryClicked`
- `Admin.HangfireJob.Deleted`
- `Admin.HangfireRecurringJob.Triggered`

2. **Rate-limiting** — admin-token-kompromiss kan annars ge DoS-yta:

```csharp
app.MapGroup("/hangfire").RequireRateLimiting("admin-dashboard");
// admin-dashboard: 60 req/min per user
```

3. **IP-allowlist** — extra defense-in-depth:

```csharp
app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/hangfire"),
    branch => branch.UseMiddleware<AdminIpAllowlistMiddleware>());
```

4. **Kort session-expire för admin-roll** — JobbPilot-cookien (ADR 0018) är
   30d refresh, för långt för admin. Sätt SlidingExpiration ~15 min på
   admin-policy (separat cookie-scheme eller policy-baserad expire).

5. **No-cache headers** — dashboard-svar ska inte cacheas av reverse-proxy/
   CloudFront:

```csharp
app.MapGroup("/hangfire").Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store, private";
    ctx.Response.Headers.Pragma = "no-cache";
    await next();
});
```

6. **CSP-relax** — Hangfire-dashboard renderar inline-scripts. Om Api har
   strikt CSP måste `/hangfire`-rutten ha relax-header eller hash-baserad
   nonce, annars renderar dashboarden trasigt.

7. **Read-only-roll (framtida)** — Hangfire stödjer
   `IDashboardAsyncAuthorizationFilter` per åtgärd. Om "operations-läs"-
   roll införs (utan retry/delete-rättigheter): splittra `AdminOnly`
   till `AdminWrite` + `AdminRead`-filter.

8. **CSRF-token-version-check vid version-bump** — Hangfire 1.8+ defaultar
   `IgnoreAntiforgeryToken = false`. Vid uppgradering: verifiera explicit
   att default inte flippats.

**Fram till aktivering:** övervaka via strukturerad logg (CloudWatch/Seq) +
Hangfire-tabeller direkt:

```sql
-- Misslyckade jobb senaste 24h
SELECT * FROM hangfire.job
WHERE statename = 'Failed'
  AND createdat > NOW() - INTERVAL '24 hours'
ORDER BY createdat DESC;

-- Recurring jobs och senaste körning
SELECT id, key, value
FROM hangfire.hash
WHERE key LIKE 'recurring-job:%'
ORDER BY key;
```

---

## 6. Fargate SIGTERM + Hangfire ShutdownTimeout (TD-17 punkt 6)

**Default-flöde i AWS ECS Fargate:**

1. ECS skickar SIGTERM till containern
2. 30 s grace-period (default `stopTimeout`)
3. SIGKILL om processen inte avslutat

**Hangfire-handling:**

- `BackgroundJobServerOptions.ShutdownTimeout` = **25 sekunder** (Worker-default
  via `HangfireWorkerOptions.ShutdownTimeoutSeconds`). Strax under Fargate
  default → Hangfire hinner committa job-state innan SIGKILL.
- Vid hög belastning (SaveChanges-batches > 25 s eller cleanup-väntan på
  open transactions): höj Fargate `stopTimeout` till 60 s + matchande
  `ShutdownTimeoutSeconds` i `appsettings.Production.json`.

**Idempotency-säkring (alla jobb):**

| Jobb | Idempotent? | Restart-väg vid SIGTERM mid-flight |
|---|---|---|
| `audit-log-retention` | Ja | Nästa daily run skapar samma partition (CREATE IF NOT EXISTS) + droppar gamla (DROP IF EXISTS) |
| `detect-ghosted` | Ja | StaleApplicationSpecification re-evaluerar — redan-markerade apps filtreras bort |
| `hard-delete-accounts` | Ja | Steg 0 orphan-cleanup plockar upp Identity-rader vars JobSeeker redan deletats; Steg 1+2 idempotent via `WHERE deleted_at < ...` filter |

**Fargate task-definition (för IaC vid prod-deploy):**

```hcl
resource "aws_ecs_task_definition" "worker" {
  # ...
  container_definitions = jsonencode([{
    name = "jobbpilot-worker"
    # ...
    stop_timeout = 30  # default; höj till 60 om smoke-tests visar behov
  }])
}
```

---

## 7. Felsökning

### 7.1 Worker startar inte i prod — `InvalidOperationException` om PrepareSchemaIfNecessary

```
Hangfire:PrepareSchemaIfNecessary måste vara false i Production (TD-17).
Kör schema-DDL via docs/runbooks/hangfire-schema.md innan första deploy.
```

**Fix:** verifiera att `appsettings.Production.json` (eller AWS Parameter Store
overlay) sätter:

```json
{
  "Hangfire": {
    "PrepareSchemaIfNecessary": false,
    "ShutdownTimeoutSeconds": 25
  }
}
```

### 7.2 Schema-state inkonsistent (Worker-startup-error om missing tabell)

Steg 1 — kontrollera state:

```sql
SELECT tablename FROM pg_tables WHERE schemaname = 'hangfire';
```

Steg 2 — om <13 tabeller saknas: kör Install.sql igen som migrations-user
(idempotent — Hangfire's Install.sql tål re-run).

Steg 3 — om version-mismatch (Hangfire-paket-uppgradering): kontrollera
`hangfire.schema`-tabellen för senaste versions-stamp:

```sql
SELECT version FROM hangfire.schema ORDER BY version DESC LIMIT 1;
```

### 7.3 Recurring job kör inte — verify registration

```sql
-- Borde finnas 3 rader: audit-log-retention, detect-ghosted, hard-delete-accounts
SELECT id, key, value
FROM hangfire.hash
WHERE key LIKE 'recurring-job:%';
```

Om saknas: `RecurringJobRegistrar` har inte körts. Verifiera Worker-loggen
för uppstarts-errors.

### 7.4 SIGTERM mid-flight — verifiering manuell

Tills automated chaos-test finns: vid prod-deploy med rolling update,
övervaka Hangfire-dashboard (när införd) eller `hangfire.job`-tabellen för
"Failed"-rader med exception av typ `OperationCanceledException`. Förvänta
att nästa cron-fönster plockar upp via idempotency-vägen.

---

## 8. Kalibrerings-fas (TD-17 punkt 5)

**Första 21 dagarna efter prod-deploy** är kalibrerings-fas för
`detect-ghosted`-jobbet:

- Migration `AddApplicationStaleDetectionFields` backfillade
  `last_status_change_at = NOW()` vid migrations-tid (per ADR 0023 / Klas
  tillägg #1).
- Befintliga apps får sitt 21-dagars-fönster räknat från migrations-tid,
  inte från app-skapande.
- Konsekvens: 21 dagar efter prod-deploy kan `detect-ghosted` plötsligt
  flagga onormalt många apps som ghosted samtidigt (alla apps som varit
  inaktiva sedan migration).

**Övervakning:**

- Klas följer Hangfire-dashboard (eller `hangfire.job`-tabellen) för
  anomaliska volymer av `MarkGhostedCommand`-dispatch.
- Om volym > 100/dag under kalibrerings-fönstret: pausa jobbet via
  `IRecurringJobManager.RemoveIfExists("detect-ghosted")` + manuell triage
  innan re-aktivering.

**Efter dag 21:** stale-detektion blir steady-state. Volymer borde vara
< 50/dag i Fas 1-användarbas.

---

## 9. Revisionshistorik

| Datum | Ändring |
|-------|---------|
| 2026-05-09 | Första versionen — TD-17 stängning (STEG 11) |
