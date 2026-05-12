# Runbook — AWS RDS Migration Apply (Phase E)

**Syfte:** Applicera EF Core-migrations mot AWS dev-RDS (privat subnet) via `JobbPilot.Migrate schema`-task.

**Bakgrund:** RDS är `publicly_accessible = false` (security-disciplin) — direkt anslutning från lokal maskin är blockerad. Migrations appliceras därför via ECS Fargate-task med samma image som `JobbPilot.Migrate` Phase A-D, men med `schema`-mode-arg per [ADR 0033](../decisions/0033-migrate-cli-mode-dispatch.md).

**Mode-disambiguation (ADR 0033):**

- `JobbPilot.Migrate init` → Phase A-D (engångs-init eller creds-rotation — **inte** för EF-migrations)
- `JobbPilot.Migrate schema` → Phase E (EF Core `Database.MigrateAsync`)
- Saknad arg → exit 1 (default-less)

## Standard-flöde: automatiserat via `deploy-dev.yml`

**Per [ADR 0033 amendment 2026-05-12](../decisions/0033-migrate-cli-mode-dispatch.md#amendment-2026-05-12--auto-trigga-schema-mode-i-deploy-devyml):**
`schema`-mode auto-triggas i deploy-dev.yml mellan ECR-push och Api-deploy vid varje `v*-dev`-tag eller `workflow_dispatch`-trigger. Manuell `aws ecs run-task` (§2 nedan) är **fallback** för debug eller out-of-band-apply utan ny deploy.

**Standardflöde:**

```powershell
git tag -a v0.2.0-dev -m "<beskrivning>"
git push origin v0.2.0-dev
# Observera deploy-dev workflow-run i GitHub Actions UI:
#   Step "Run Migrate schema-task (Phase E)" loggar pending + applied.
```

Vid fail på schema-step: hela deploy avbryts (Api uppdateras inte). Felsökning per §4 nedan.

---

## 1. Förutsättningar (manuell fallback-procedur §2)

- [ ] Dev-RDS är initierad (Phase A-D körd vid F2-P0a → roller + secrets finns)
- [ ] EF-migration är committad och pushad till `main` (commit-SHA noteras)
- [ ] CI-deploy-pipeline har byggt + pushat ny Migrate-image till ECR
- [ ] `MIGRATE_APP_CONN_SECRET_ARN`-secret innehåller giltig `jobbpilot_app`-creds (skapad i Phase D)
- [ ] AWS CLI v2 + AWS_PROFILE=jobbpilot konfigurerat lokalt
- [ ] Migration är **backward-compatible** (Api fortsätter köra under apply) — nya kolumner nullable, inga drop:s på used kolumner, inga rename-utan-shadow-copy

---

## 2. Procedur — applicera schema-mode-task

### 2.1 Verifiera nuvarande state

```powershell
# Lista pending EF-migrations lokalt (ska matcha vad som ska appliceras mot dev-RDS)
dotnet ef migrations list --project src/JobbPilot.Infrastructure `
    --startup-project src/JobbPilot.Api `
    --context AppDbContext
```

Output ska visa pending migrations (markerade `(pending)` om de inte är applicerade lokalt).

### 2.2 Identifiera senaste Migrate-task-def-revision

```powershell
aws ecs list-task-definitions `
    --family-prefix jobbpilot-dev-migrate `
    --status ACTIVE `
    --sort DESC `
    --max-items 1
```

Notera task-def-ARN (t.ex. `arn:aws:ecs:eu-north-1:<account>:task-definition/jobbpilot-dev-migrate:42`).

### 2.3 Kör schema-mode-task

```powershell
$taskDefArn = "<arn-från-2.2>"
$clusterName = "jobbpilot-dev"
$subnetId = "<private-subnet-id-från-terraform>"
$securityGroupId = "<migrate-sg-id-från-terraform>"

aws ecs run-task `
    --cluster $clusterName `
    --task-definition $taskDefArn `
    --launch-type FARGATE `
    --network-configuration "awsvpcConfiguration={subnets=[$subnetId],securityGroups=[$securityGroupId],assignPublicIp=DISABLED}" `
    --overrides '{
        \"containerOverrides\": [{
            \"name\": \"migrate\",
            \"command\": [\"dotnet\", \"JobbPilot.Migrate.dll\", \"schema\"]
        }]
    }' `
    --started-by "migration-apply-$(Get-Date -Format yyyyMMdd-HHmmss)"
```

Notera task-ARN från output (t.ex. `arn:aws:ecs:eu-north-1:<account>:task/jobbpilot-dev/<task-id>`).

### 2.4 Vänta in completion

```powershell
$taskArn = "<arn-från-2.3>"

aws ecs wait tasks-stopped `
    --cluster jobbpilot-dev `
    --tasks $taskArn

aws ecs describe-tasks `
    --cluster jobbpilot-dev `
    --tasks $taskArn `
    --query "tasks[0].{LastStatus:lastStatus,ExitCode:containers[0].exitCode,StoppedReason:stoppedReason}"
```

**Förväntad output:**
```json
{
    "LastStatus": "STOPPED",
    "ExitCode": 0,
    "StoppedReason": "Essential container in task exited"
}
```

Exit-code 0 = success. Annat exit-code → se §4 Felhantering.

### 2.5 Verifiera CloudWatch-loggar

```powershell
aws logs tail /ecs/jobbpilot-dev-migrate --since 10m --follow
```

Sökmönster i loggarna:

| Event | Förväntat |
|---|---|
| `Mode: schema (Phase E — EF Core MigrateAsync)` | EventId 201 — dispatch korrekt |
| `Phase E: EF Core Database.MigrateAsync ...` | EventId 60 |
| `Pending migrations: N` | EventId 61 — listar antal pending |
| `  -> <migration-namn>` | EventId 62 — pending-listan |
| `Phase E COMPLETE — applied N migration(s)` | EventId 63 — apply lyckades |
| ELLER `Phase E: no pending migrations` | EventId 64 — idempotent no-op |

### 2.6 Smoke-test mot applicerat schema

Anslut via Api (eller smoke-test-task) och verifiera att nya kolumner är queryable:

```sql
-- För F2-P8a (Fas2P8aJobAdExternalReference)
SELECT external_source, external_id, raw_payload FROM job_ads LIMIT 0;

-- Verifiera UNIQUE-index
SELECT indexname, indexdef FROM pg_indexes
WHERE tablename = 'job_ads' AND indexname = 'ix_job_ads_external_source_external_id';
```

`SELECT ... LIMIT 0` ska returnera 0 rader utan error (verifierar att kolumnerna finns).

### 2.7 Verifiera `__EFMigrationsHistory`

```sql
SELECT migration_id, product_version FROM __ef_migrations_history ORDER BY migration_id;
```

Output ska inkludera den nyss applicerade migrationen (t.ex. `20260512130357_Fas2P8aJobAdExternalReference`).

---

## 3. Rollback-procedur

EF Core stöder **inte** automatisk rollback-via-task. Manuell procedur:

### 3.1 Generera revert-migration lokalt

```powershell
# Skapa en ny migration som är inversen av problem-migrationen
dotnet ef migrations add Revert<MigrationName> `
    --project src/JobbPilot.Infrastructure `
    --startup-project src/JobbPilot.Api `
    --context AppDbContext
```

EF genererar en migration som är diff:en mellan nuvarande Model och föregående snapshot. Granska den manuellt — det ska vara en `Down`-version av problem-migrationen.

### 3.2 Commit + push revert-migration

```powershell
git add src/JobbPilot.Infrastructure/Persistence/Migrations/<timestamp>_Revert<Name>.cs
git commit -m "fix(jobads): revert <migration-name> efter problem mot dev-RDS"
git push origin main
```

### 3.3 Apply revert via Phase E

Samma procedur som §2 men med revert-migrationen som senaste pending.

---

## 4. Felhantering

### 4.1 Exit-code 1 + `Saknad env-var: MIGRATE_APP_CONN_SECRET_ARN`

Task-def saknar required env-var. Verifiera Terraform-task-def-spec i `infra/terraform/modules/ecs/migrate.tf` (motsvarande filnamn).

### 4.2 `App connection-string secret är tom`

Secrets Manager-värdet för `jobbpilot/dev/db/app-connection-string` har inget värde. Phase D (init-mode) ska ha skrivit värdet — om secrets är tomt har Phase D aldrig körts eller misslyckats.

**Åtgärd:** kör `JobbPilot.Migrate init`-task först (genererar nya creds + skriver till Secrets Manager). **OBS:** detta roterar alla 3 db-roller-passwords — Api/Worker-kontainers måste restartas efter.

### 4.3 Authentication failed för jobbpilot_app

CS i Secrets Manager är inte synkad med faktisk PostgreSQL-creds. Phase D-mid-rotation-race eller manuell ALTER ROLE.

**Åtgärd:** kör `JobbPilot.Migrate init`-task → genererar nya creds + skriver synkade värden. Restart Api/Worker efter.

### 4.4 EF migration `relation already exists` eller liknande schema-conflict

Manuell DDL har applicerats utan att uppdatera `__ef_migrations_history`. EF Core ser migrationen som pending men DB har redan ändringen.

**Åtgärd:** lägg till migration-id i `__ef_migrations_history` manuellt via psql (anslut via SSM-tunnel eller bastion):

```sql
INSERT INTO __ef_migrations_history (migration_id, product_version)
VALUES ('20260512130357_Fas2P8aJobAdExternalReference', '10.0.0');
```

Re-run schema-task → no-op.

### 4.5 Task fails med `Cannot reach RDS endpoint`

Security-group-config eller subnet-routing-problem. Verifiera:

```powershell
# Migrate-task-SG ska ha egress till RDS-SG på port 5432
aws ec2 describe-security-groups --group-ids <migrate-sg-id> --query "SecurityGroups[0].IpPermissionsEgress"

# RDS-SG ska ha ingress från Migrate-SG på 5432
aws ec2 describe-security-groups --group-ids <rds-sg-id> --query "SecurityGroups[0].IpPermissions"
```

---

## 5. När `init`-mode ska användas istället

Bara vid:

1. **Första initialiseringen** av en ny RDS-instans (innan Api/Worker körs)
2. **Creds-rotation-policy** (~1×/år eller vid säkerhets-incident)

`init` genererar 3 nya passwords + skriver till Secrets Manager + invaliderar gamla creds-versioner. Api/Worker som har gamla creds i memory faller på auth efter init-körning — måste restartas via `aws ecs update-service --force-new-deployment`.

**Förbjudet:** kör inte `init` som "snabb-fix" för EF-migrations. Använd `schema`-mode.

---

## 6. Referenser

- [ADR 0033](../decisions/0033-migrate-cli-mode-dispatch.md) — CLI-mode-dispatch
- [ADR 0023](../decisions/0023-worker-pipeline-hangfire.md) — Hangfire-infrastruktur (Phase B-mönstret)
- [ADR 0032](../decisions/0032-jobtech-integration.md) — JobTech-integration (utlöste F2-P8a-migrationen)
- `docs/runbooks/hangfire-schema.md` — `init`-mode-procedur (Phase A-D)
- [Microsoft Learn — Applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
