---
session: STEG 14b apply — DDL-init + ConnectionStrings split + Worker-recovery
datum: 2026-05-10
slug: steg14b-ddl-worker-recovery
status: KLAR (Migrate exit 0, Worker stable, API smoke-test PASS, TD-37 deferrad till 14c)
commits:
  - (TBD) feat(migrate): STEG 14b — Migrate console-app + IAM + ECS task-def + Worker-recovery
---

# STEG 14b apply — DDL-init + Worker-recovery

## Mål

Stänga Worker-restart-loopen från STEG 13b genom att skapa Postgres-roller +
Hangfire-schema och skriva final connection-strings till Secrets Manager.
Klas-val: **Väg B** (one-shot ECS-task med PostgreSqlObjectsInstaller) över
Väg A (psql + Session Manager från laptop). Ordning: DDL+Worker först,
TD-37-investigation efter.

## Sammanfattning

- ✅ Ny `src/JobbPilot.Migrate/` console-app — 4 phases (master-DDL → Hangfire-Install
  → master-GRANTs → Secrets PutSecretValue), idempotent, CancellationToken-flow
- ✅ Ny `task_migrate`-roll i `modules/iam_ecs` (count-pattern, separat blast-radius)
- ✅ Migrate-task-def i `modules/ecs` + `migrate_run_task_command` Terraform output
- ✅ ECR-repo + LogGroup för migrate (KMS-encrypted, 30d retention)
- ✅ 3 agent-reviews parallellt — alla APPROVE-WITH-FIXES, fixar applicerade
- ✅ Apply-iterationer: Phase 1 (IAM) + Phase 2 (task-def rev 1-3)
- ✅ Image-iterationer: orig + fix1 + fix2 + fix3 (push till ECR)
- ✅ Run-task-iterationer: 4 (DO-block-bug → SET ROLE-bug → charmap-bug → KMS-bug)
- ✅ Final task PASS (a2a988a4): alla 4 phases, exit 0
- ✅ Worker + API force-new-deployment → COMPLETED stable
- ✅ Smoke-test https://dev.jobbpilot.se/api/ready → 200 + HSTS aktiv
- ✅ Hangfire BackgroundJobServer running med dispatchers
- ⏭️ TD-37 deferrad till 14c (kräver djupare investigation utanför 14b-scope)
- ✅ TD-38 lyft (Trust Server Certificate hardening för Fas 1)

## Tids-blocks

| Tid (lokal) | Aktivitet |
|-------------|-----------|
| 18:50 | Klas-GO på 14b. Discovery: ECS Exec on, RDS-secret finns, Hangfire 1.21.1 har INGEN exporterad Install.sql |
| 19:00 | Plan-design Väg B. Skapar src/JobbPilot.Migrate/ (csproj + Program.cs + MigrateLog + Dockerfile) |
| 19:15 | (Datorn omstartad) Återupptag — fix Npgsql 10.0.1→10.0.2 (transitive) + CA1848 LoggerMessage + CA1305 InvariantCulture + CA1873 pre-compute |
| 19:30 | Utvidgar modules/iam_ecs med task_migrate + modules/ecs med migrate-task-def + environments/dev anrop |
| 19:40 | terraform validate + plan: 6 add + 2 change |
| 19:45 | 3 agent-reviews parallellt (security/code/architect) — alla APPROVE-WITH-FIXES |
| 19:55 | Applicera fixar: re-fetch Phase C, RdsMasterSecret PascalCase + null-validate, format()-SQL-defense (sen rollback efter pl/pgsql-fail), ta bort kms:GenerateDataKey (sen rollback efter KMS-fail), SHA256-fingerprint, CTS+SIGTERM-handler, migrate_run_task_command-output, Roles-const-block, Dockerfile sln-skip |
| 20:00 | terraform apply Phase 1 (6 add + 2 change). Bygg + push migrate-image → ECR |
| 20:05 | tfvars-edit migrate_image_tag=14b-9113bed. Apply Phase 2 (task-def rev 1) |
| 20:08 | **Run 1:** task bdc8242 → exitCode 139 (SIGSEGV). DO-block med Npgsql @role-parameters fungerar inte i pl/pgsql-scope |
| 20:15 | Refaktor: två-stegs SELECT + DDL-pattern. CTS-disposal-bug fix. Bygg fix1, apply rev 2 |
| 20:18 | **Run 2:** task 4ea8766 → exit 1, `42501: must be able to SET ROLE "jobbpilot_migrations"` (RDS-master är limited superuser, kan inte SET ROLE utan membership) |
| 20:25 | Fix: `GRANT migrations/app/worker TO CURRENT_USER` post-CREATE. Bygg fix2, apply rev 3 |
| 20:30 | **Run 3:** task 597f19f → exit 1. Phase A nästan helt klar (REVOKE + 3 ROLES + 3 GRANTs + GRANT-membership + CREATE SCHEMA + GRANTs på public). Failure efter "GRANT ALL public-sequences till app". CloudWatch logs trunkerade i Windows-shellen pga charmap-error på `→`-tecken |
| 20:35 | Byt `→` → `->` i log-descriptions för ASCII-only output. Bygg fix3, apply rev 4 |
| 20:40 | **Run 4:** task 05cb63a → exit 1. Phase A/B/C ALLA PASS (inkl. Hangfire-Install av 13 tabeller!). Phase D fail med `Amazon.SecretsManager.AmazonSecretsManagerException: Access to KMS is not allowed`. Security-auditor:s Sec-Minor-5 (ta bort kms:GenerateDataKey) var EMPIRISKT FEL — KMS-encrypted Secrets-PutSecretValue kräver GenerateDataKey för envelope-encryption av nya version |
| 20:45 | Återställ kms:GenerateDataKey i task_migrate-policy. Apply IAM-update (0 add, 2 change) |
| 20:48 | **Run 5:** task a2a988a → **exit 0!** Alla 4 phases PASS. PutSecretValue × 2 OK |
| 20:55 | Worker + API force-new-deployment. ~3 min till båda COMPLETED stable |
| 21:08 | Smoke-test PASS (200 + HSTS). Worker logs visar Hangfire BackgroundJobServer started + dispatchers running |
| 21:15 | TD-37-investigation: deferrad till 14c (kräver Linux-Docker repro). TD-38 lyft |
| 21:20 | Docs-sync + session-logg + commit |

**Total session-tid:** ~2.5 timmar (mycket av det 4-iteration debug-cykel + ~3 min per Worker rolling).

## In-flight fixar

### Fix 1 — Npgsql 10.0.1 → 10.0.2 (transitive constraint)
**Problem:** `Directory.Packages.props` hade `Npgsql 10.0.1` men `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1` (befintlig) kräver transitivt `Npgsql >= 10.0.2`. NU1109 downgrade-error på alla projekt som ärver via CentralPackageTransitivePinningEnabled.
**Fix:** Bump till 10.0.2.
**Lärdom:** Vid central package versions-bump — kolla transitive constraint av befintliga packages först.

### Fix 2 — DO-block + @role-parameters fungerar inte i pl/pgsql-scope
**Problem:** Initial `CREATE ROLE` använde DO-block med Npgsql-parameters:
```sql
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role) THEN
        EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', @role, @pwd);
    ...
END $$;
```
Postgres failade med `42703: column "role" does not exist`. Anonymous DO-blocks
är pl/pgsql och tar inte Npgsql-parameters direkt — `@role`-referenser
propagerar inte in i pl/pgsql-scope.
**Fix:** Två-stegs-pattern: parameteriserad SELECT för existens-check,
sedan string-interpolerad DDL. Säkerhet via const Roles-namn + charset
[A-Za-z0-9] för pwds (inga `'`/`\` möjliga) + ValidateIdentifier-defense.
**Lärdom:** Anonymous DO-blocks kan inte ta Npgsql-parameters. Detta är
icke-trivialt att lära sig — kostade en run-task-iteration.

### Fix 3 — RDS-master är limited superuser, kan inte SET ROLE utan membership
**Problem:** `CREATE SCHEMA hangfire AUTHORIZATION jobbpilot_migrations` failade
med `42501: must be able to SET ROLE "jobbpilot_migrations"`. RDS-master-user
har `rds_superuser`-role, INTE full Postgres SUPERUSER. Kan inte SET ROLE
utan explicit membership i target-rollen.
**Fix:** Efter CREATE ROLE, kör `GRANT migrations/app/worker TO CURRENT_USER`.
Master får membership → kan AUTHORIZATION-skapa + GRANT på objekt ägda av
target-roller (Phase C).
**Lärdom:** RDS-Postgres är inte vanlig Postgres. SUPERUSER-yta är lite annorlunda.
Test-flödet borde köra mot RDS direkt i Fas 1 (inte Testcontainers vanilla
postgres) för att fånga sådana här edge-cases.

### Fix 4 — `→`-tecken triggar AWS CLI charmap-error på Windows
**Problem:** Migrate loggar description-strängar med `→` (t.ex. "DEFAULT
PRIVILEGES public-tabeller → app"). När `aws logs tail`/`filter-log-events`
försöker outputta dessa till Windows-shell:en (cp1252) failer pythons
charmap-codec på `→`. Loggen trunkeras vid första förekomsten →
omöjligt att läsa migrate-failure i Windows.
**Fix:** Byt `→` → `->` i log-descriptions. ASCII-only.
**Lärdom:** Container-applikationer som loggar till CloudWatch och läses
från Windows-shell måste hålla logg-output ASCII-only. Eller lös via
PowerShell/cmd `chcp 65001` + `PYTHONIOENCODING=utf-8` (vi försökte, fungerade
inte tillräckligt robust). Trade-off: docs/code-readability vs CI-debug-UX.

### Fix 5 — `kms:GenerateDataKey` krävs för KMS-encrypted Secrets vid PutSecretValue
**Problem:** security-auditor Sec-Minor-5 rekommenderade att ta bort
`kms:GenerateDataKey` från task_migrate-policy med argumentet att Secrets
Manager-tjänsten gör encrypt server-side. Empiriskt FEL: vid PutSecretValue
mot KMS-encrypted secret behöver klienten ge data-key för envelope-
encryption av nya secret-versionen. Utan GenerateDataKey:
`AmazonSecretsManagerException: Access to KMS is not allowed`.
**Fix:** Återställ `["kms:Decrypt", "kms:GenerateDataKey"]` i policy.
ViaService-condition begränsar redan yta till Secrets Manager-flödet.
**Lärdom:** Agent-rekommendationer är inte alltid korrekta — verifiera
empiriskt vid säkerhetsfynd som påverkar funktionalitet. security-auditor
rapporterade detta som Minor (defense-in-depth) men det blockerade
hela Phase D.

## Apply-flöde (faktiskt)

| # | Steg | Resultat | Tid |
|---|------|----------|-----|
| 1 | Skriv src/JobbPilot.Migrate/ + sln-add + Directory.Packages.props | ✅ Builds | ~30 min |
| 2 | Utvidgar iam_ecs + ecs + dev-stack + Dockerfile | ✅ validate PASS | ~20 min |
| 3 | 3 agent-reviews + spara docs/reviews/ + applicera fixar | ✅ APPROVE-WITH-FIXES | ~30 min |
| 4 | terraform apply Phase 1 (6 add + 2 change) | ✅ | ~30s |
| 5 | docker build + push image (orig) | ✅ | ~3 min |
| 6 | tfvars-edit + apply Phase 2 (task-def rev 1) | ✅ 1 add + 1 change | ~20s |
| 7 | run-task #1 (orig) → exit 139 (SIGSEGV pl/pgsql @-param) | ❌ | ~2 min |
| 8 | Refaktor två-stegs CREATE ROLE + bygg fix1 + apply rev 2 | ✅ rebuild | ~5 min |
| 9 | run-task #2 (fix1) → exit 1 (42501 SET ROLE) | ❌ | ~1 min |
| 10 | Fix GRANT TO CURRENT_USER + bygg fix2 + apply rev 3 | ✅ rebuild | ~5 min |
| 11 | run-task #3 (fix2) → exit 1 (charmap-error blockerade trace) | ❌ | ~1 min |
| 12 | Byt `→` → `->` + bygg fix3 + apply rev 4 | ✅ rebuild | ~5 min |
| 13 | run-task #4 (fix3) → exit 1 (KMS-access for PutSecretValue) | ❌ | ~1 min |
| 14 | Återställ kms:GenerateDataKey i policy + apply (0 add 2 change) | ✅ | ~30s |
| 15 | run-task #5 (fix3 + IAM-fix) → **exit 0** | ✅ | ~1 min |
| 16 | aws ecs update-service --force-new-deployment × 2 (api + worker) | ✅ | ~3 min stabilization |
| 17 | Smoke-test + Hangfire-verify via CloudWatch | ✅ 200 + HSTS + dispatchers | ~30s |
| 18 | Lyft TD-38 + uppdatera TD-37 + docs-sync | (denna commit) | ~15 min |

## Resultat

### Migrate-task final state (a2a988a4dcf54273859ea5d27347f578)

```
17:56:13 Phase A start
17:56:16 - REVOKE PUBLIC från db
         - CREATE/ALTER ROLE jobbpilot_migrations OK
         - CREATE/ALTER ROLE jobbpilot_app OK
         - CREATE/ALTER ROLE jobbpilot_worker OK
         - GRANT CONNECT till alla 3
         - GRANT membership-roller TO master
         - CREATE SCHEMA hangfire
         - REVOKE PUBLIC från hangfire
         - GRANT USAGE/CREATE på hangfire/public
         - GRANT ALL på public.* + sequences till app
         - DEFAULT PRIVILEGES public → app

17:56:16 Phase B: Hangfire schema-install (PostgreSqlObjectsInstaller) COMPLETE
         (13 tabeller skapade i hangfire-schema)

18:00:35 Phase C: master re-fetched, GRANT på hangfire.* till worker (DML-only)
         + DEFAULT PRIVILEGES hangfire → worker

18:02:32 Phase D: PutSecretValue × 2
         - app-connection-string-BCvQsM
         - hangfire-storage-connection-string-2FI8PN

Migrate COMPLETE — exit 0
```

### Worker recovery (fe7ca962da6b4d88940de6e5b133ddc7)

```
18:04:06 Hangfire.BackgroundJobServer started
         Worker count: 4
         Listening queues: 'default'
         Shutdown timeout: 00:00:25
         Schedule polling interval: 00:00:15
         Server ip-10-0-10-58:1:5a9d44eb successfully announced
         Server starting dispatchers: ServerWatchdog, ServerJobCancellationWatcher,
                                      ExpirationManager, CountersAggregator, Worker,
                                      DelayedJobScheduler, RecurringJobScheduler
         All dispatchers started
```

### API smoke-test

```
$ curl -s -o /dev/null -w "HTTP %{http_code}\n" https://dev.jobbpilot.se/api/ready
HTTP 200

$ curl -sI https://dev.jobbpilot.se/api/ready | grep -i strict
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

## Cost (oförändrat sedan STEG 13c)

Migrate-task = ECS Fargate spot ~$0.0006 per körning (256 CPU, 512 MB, ~30s).
Negligible. ECR-repo +1 (jobbpilot-dev-migrate, ~10MB image) = $0.10/mån.
LogGroup +1 (med samma 30d retention pattern) = ~$0.05/mån.

**Spend uppdaterad: ~$79.65/mån** (+$0.15 från STEG 14b).

## Nya TDs

**TD-38: Trust Server Certificate=true persisteras till app/worker connection-strings**
— security-auditor Sec-Minor-4. Dev-yta är låg (VPC-isolerad). Fas 1
hardening-uppgift: lägg in RDS-CA-bundle i container-truststores och flippa
till `Trust=false`.

## Lärdomar STEG 14b

- **Pl/pgsql DO-blocks tar inte Npgsql-parameters.** Anonymous blocks är
  kompilerade som pl/pgsql och `@role`-referenser propagerar inte in i scope.
  Använd två-stegs SELECT + DDL-pattern istället.
- **RDS-master är limited superuser**, INTE full Postgres SUPERUSER. Kan
  inte SET ROLE utan explicit GRANT membership. För `CREATE SCHEMA AUTHORIZATION`
  + Phase C-GRANTs krävs `GRANT migrations TO CURRENT_USER` post-CREATE.
- **Hangfire.PostgreSql 1.21.1 har INGEN exporterad Install.sql** i NuGet-
  paketet. Schema-DDL embedded i DLL:n. `PostgreSqlObjectsInstaller.Install
  (connection, schemaName)` är officiella API:t — public static, stabilt
  sedan 1.0.
- **AWS CLI charmap-codec failer på `→` i Windows-shell.** Container-
  applikationer som loggar till CloudWatch måste hålla logg-output ASCII-
  only ELSE måste man ha PowerShell `chcp 65001` + `PYTHONIOENCODING=utf-8`
  (fungerade inte tillräckligt robust i denna session).
- **`kms:GenerateDataKey` krävs för KMS-encrypted Secrets vid PutSecretValue.**
  Klient genererar data-key för envelope-encryption av nya version. security-
  auditor:s Sec-Minor-5 var empiriskt felaktig — testa innan tro vid sec-fynd
  som påverkar funktionalitet.
- **Re-fetch master-creds vid Phase C** är säker mot mid-flow rotation-race.
  Migrate.Phase B (Hangfire-Install) tog ~80ms i denna run, men kan ta
  60-120s vid större schema → re-fetch är försäkring.
- **Idempotens via DO-block + IF NOT EXISTS-pattern** fungerar bra för CREATE
  ROLE: re-run roterar pwd via ALTER ROLE-grenen. Worker plockar nya creds
  vid nästa force-new-deployment. Inga föräldralösa pwds eftersom Phase D
  alltid skriver final connection-strings.
- **Image-tag-strategy `14b-{git-sha}-{fixN}`** spårar iterations-historik
  utan att skapa ECR-image-spillover (lifecycle keep-last-10 håller det rent).
- **Terraform output med interpolerad CLI-string** (`migrate_run_task_command`)
  räddade ~10 min copy-paste per iteration. Fas 1: lägg till liknande för
  alla one-shot tasks.
- **count-pattern i Terraform-modul** (`count = var.X != "" ? 1 : 0`) ger
  backwards-compat — IaC kan applias utan att Migrate-resurser skapas i
  miljöer där de inte behövs (t.ex. först-time dev-bootstrap).

## Nästa session

**STEG 14c** (`first formal tag-deploy + Bootstrap-cleanup-verify + Fas 0-stängning`):

- **TD-37 investigation först** (Integration-tests fail i CI):
  - Reproducera lokalt med Linux-Docker (devcontainer eller `act`)
  - Verbose Serilog för 500-error-roten
  - Eventuell isolering till separat workflow-job (continue-on-error)
- `git tag v0.1.0-dev && git push origin v0.1.0-dev` → trigger deploy-dev.yml
- Verifiera first formal CI/CD-deploy via GitHub Actions
- Verifiera Bootstrap-IAM-user borta (redan tom enligt 14a-discovery)
- ADR (om Fas 0-completion-decision nödvändig)
- README.md-update "Status: Fas 0 → Fas 1"
- **Stänger Fas 0** per BUILD.md §18

**Defererat från STEG 14b:**
- TD-38 (Trust Server Certificate hardening) — Fas 1
- security-auditor Sec-Minor-1 (modulo-bias rejection-sampling) — accepterad risk
- security-auditor Sec-Minor-3 (defensiv identifier-validation) — partial fix (ValidateIdentifier finns men inte överallt)
- code-reviewer Major-2 (full CancellationToken-flow) — light implementation, kan utvidgas

ADR 14b inte skriven — pattern-konsistent med befintliga moduler (count-pattern + IAM-isolation), ingen ny arkitekturell decision.
