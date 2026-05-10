# Current work — JobbPilot

**Status:** STEG 13b APPLIED 2026-05-10. Hela infra-stacken uppe i eu-north-1 (VPC + RDS + Redis + ECR + ALB + ECS + IAM + CW LogGroups + secrets). Api smoke-test PASS via ALB (`http://jobbpilot-dev-alb-1232220213.eu-north-1.elb.amazonaws.com/api/ready` + `/health`). Worker i restart-loop pga PLACEHOLDER DB-creds (förväntat, fixas av STEG 14 DDL+roller). 4 in-flight kod-fixar krävdes: KMS-policy CloudWatch Logs grant (prod-stack), ASCII-only SG-descriptions, Dockerfile noble base-image (.NET 10 GA bytte default OS), placeholder secret-versions med ignore_changes. Spend ~$79/mån löper från ~07:00 ($2.60/dag). ADR 0026 deadline 2026-06-08 (29 dagar). Nästa: **STEG 13c** (Route53 + ACM + HTTPS-flip) ELLER **STEG 14** (GitHub Actions + DDL-init + IAM cleanup). Klas-prio.
**Senast uppdaterad:** 2026-05-10
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**STEG 13a + 13b APPLIED 2026-05-10.** Full apply-cykel. Se session-logg `docs/sessions/2026-05-10-0730-steg13b-apply.md` för fix-detaljer (KMS-policy CloudWatch Logs grant, ASCII-only SG-descriptions, Dockerfile noble base-image, placeholder secret-versions). Api 1/1 running, Worker restart-loop tills STEG 14 sätter riktiga DB-creds. Total apply-tid 45 min (vs 25-30 min estimat — overhead pga in-flight fixar).

### Apply-state

| Resurs | Identifier |
|---------|-----------|
| ALB-DNS | `http://jobbpilot-dev-alb-1232220213.eu-north-1.elb.amazonaws.com` |
| RDS | `jobbpilot-dev-rds.cj84yieu0qwc.eu-north-1.rds.amazonaws.com:5432` |
| Redis | `master.jobbpilot-dev-redis.tyzmvb.eun1.cache.amazonaws.com:6379` |
| ECR API | `710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-api:latest` |
| ECR Worker | `710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-worker:latest` |
| VPC | `vpc-0659b4386bba9dc31` |
| ECS Cluster | `jobbpilot-dev-cluster` |

### STEG 13a — Infra-as-code-stack: networking + databas + cache (Alt A2)

### STEG 13a — Infra-as-code-stack: networking + databas + cache (Alt A2) [APPLIED]

**Strategi:** Per A4-sekvens (STEG 12 = A1 kod-pre-launch-gates klar; STEG 13 = A2 Terraform-stack pågår; STEG 14 = A3 GitHub Actions + första deploy + IAM-cleanup). Klas valde sub-uppdelning 13a + 13b efter discovery-rapport som visade STEG 13:s totala scope ≈ 1-3 sessioner.

#### Block 1 — `modules/network/` (VPC + subnets + NAT + endpoints + base SGs)

- VPC `10.0.0.0/16`, 3 AZs (eu-north-1a/b/c) via `data "aws_availability_zones"`
- Public subnets `10.0.0.0/24, .1.0/24, .2.0/24` (för ALB + NAT)
- Private subnets `10.0.10.0/24, .11.0/24, .12.0/24` (för ECS + RDS + Redis)
- Single NAT Gateway i AZ-a (cost-optimized; ~$32/mån vs ~$96 för Multi-AZ NAT). AZ-a-failure → utgående trafik bryts. Mitigation-väg dokumenterad i README.
- VPC Endpoints: S3 Gateway (gratis, alltid på). **Interface-endpoints (SM + KMS) AV i lean-dev** (sparar ~$22/mån; SM/KMS-trafik går via NAT istället). Staging/prod sätter `enable_interface_endpoints = true` explicit.
- Bedrock VPC endpoint utelämnad — Bedrock-tjänsten finns ej i eu-north-1 (cross-region inference går via NAT till eu-central-1/eu-west-1)
- Security groups: `alb` (80/443 från 0.0.0.0/0), `ecs` (8080 från ALB-SG), `rds` (5432 från ECS-SG), `redis` (6379 från ECS-SG). `vpc_endpoints` (443 från ECS-SG) skapas bara om interface-endpoints aktiveras. Strikt principal-based via `referenced_security_group_id`.

#### Block 2 — `modules/rds/` (Postgres 18.3, lean-dev defaults)

- Engine `postgres` 18.3, **db.t4g.micro Single-AZ** (lean dev-default; staging/prod sätter db.t4g.medium Multi-AZ explicit), gp3 20GB → 100GB auto-scale
- `manage_master_user_password=true` (AWS-managed Secrets Manager + auto-rotation 7d default)
- KMS-encryption på storage + master-secret + Performance Insights (alla på `alias/jobbpilot-master-key`)
- `deletion_protection=true` + `skip_final_snapshot=false` + `final_snapshot_identifier` med `timestamp()` + `lifecycle.ignore_changes`
- Enhanced Monitoring 60s + dedicated IAM-roll
- Parameter group:
  - `log_statement=none` (Sec-Major-1: hindrar password-leak vid STEG 14 DDL `CREATE/ALTER ROLE`)
  - `log_min_duration_statement=1000`
  - `log_parameter_max_length=0` + `log_parameter_max_length_on_error=0` (Sec-Minor-6: trunkerar bind-värden i slow-query-log → ingen PII-läckage via WHERE-klausuler)
  - `rds.force_ssl=1` (TLS-tvång)
- Explicit `aws_cloudwatch_log_group` för `postgresql` + `upgrade` med `retention_in_days=30` + KMS (Sec-Major-1: hindrar default `Never expire` som bryter ADR 0024 D7)

#### Block 3 — `modules/redis/` (Valkey 8 replication group)

- Engine `valkey` 8.0 (BUILD.md §15.1 sa "Redis 8.6" — Redis 8.x är post-license-byte och inte AWS-supportad; Valkey 8 är AWS:s Redis-kompatibla efterföljare)
- `cache.t4g.small` × 2 noder, automatic_failover_enabled, multi_az_enabled
- transit + at-rest encryption (KMS-master-key)
- AUTH-token: `random_password` 64 chars `[a-zA-Z0-9]` (~380 bits entropy) i Secrets Manager med 7d recovery
- `lifecycle.ignore_changes=[auth_token]` — rotation kräver out-of-band procedur (Sec-Minor-4 defererad → STEG 13b runbook)

#### Block 4 — `environments/dev/` (env-stack)

- `versions.tf` (TF >= 1.14, AWS ~> 5.80, random ~> 3.6)
- `backend.tf` — S3-state, key=`dev/main.tfstate`, encrypted, DynamoDB-locks
- `providers.tf` — provider med default_tags Environment=dev
- `variables.tf` — `name_prefix=jobbpilot-dev`, `vpc_cidr=10.0.0.0/16`, RDS/Redis-defaults
- `terraform.tfvars` — tomt (defaults räcker för dev)
- `main.tf` — `data "aws_kms_alias" "master"` lookup + 3 modul-anrop (network, rds, redis) + 2 dev-secrets-placeholders (`jobbpilot/dev/db/app-connection-string`, `jobbpilot/dev/db/hangfire-storage-connection-string` — sätts post-DDL i STEG 14)
- `outputs.tf` — VPC + subnets + SG-IDs + RDS-endpoint + Redis-endpoint + secret-ARNs
- `README.md` — körinstruktioner + verifierings-kommandon + cost-flagga

### Säkerhets-fynd från STEG 13a

**Fixade in-block (security-auditor):**
- **Sec-Major-1** — `log_statement="ddl"` + odeklarerad CloudWatch-LogGroup. Fix: `none` + explicit LogGroup med 30d retention + KMS. Hindrar password-leak vid STEG 14 DDL.
- **Sec-Minor-6** (kombinerad) — slow-query-log inkluderar bind-värden = PII via WHERE-klausuler. Fix: `log_parameter_max_length=0` + `_on_error=0`.
- **Sec-Minor-2** (docs) — kommentar tillagd vid `master_user_secret_kms_key_id` om master-vs-BYOK-key-val.

**ADR-accepterad:**
- **Sec-Major-2** — ECS-SG egress `0.0.0.0/0` `-1`. ADR 0025 dokumenterar mitigation-stack (TLS+anonymisering+ALB-only-ingress) + omvärderingstrigger (Fas 1→Fas 2-övergång) + förberedd hardening-väg.

**Defererade:**
- Sec-Minor-1 (single NAT SPOF — acceptabel Fas 0)
- Sec-Minor-3 (Redis AUTH alphabet — entropy 380 bits OK)
- Sec-Minor-4 (Redis AUTH-rotation runbook — STEG 13b)
- Sec-Minor-5 (Redis CloudWatch-export — STEG 13b)
- Sec-Nit-1/2/3 (defererade)

## Senaste commits

(uppdateras efter denna sessions commits)

| SHA | Beskrivning |
|-----|-------------|
| 60d9f98 | docs: STEG 12 docs-sync (current-work + steg-tracker + tech-debt + session-logg + reviews) |
| d879f96 | docs(runbooks): STEG 12 Sec-Major-2 — ForwardLimit + CloudFront-prefix-list |
| bb26fec | feat(api): TD-21 — ForwardedHeadersConfig + production-defense (STEG 12) |
| f8488b4 | feat(worker): TD-17 punkt 4 — HangfireConnectionStringResolver-fallback (STEG 12) |
| 8211ddb | docs: STEG 11 docs-sync |

## Tester totalt (oförändrat — ingen .NET-kod rörd i 13a)

- **Backend:** 537 (157 Domain + 183 Application + 23 Architecture + 26 Worker + 148 Api Integration)
- **Frontend:** 65 Vitest + 19 Playwright E2E

## När nästa session startar

### Operativa pre-apply-steg (innan STEG 13b kan börja)

1. **SSO-login:** `aws sso login --profile jobbpilot` (token har gått ut)
2. **Budget-höjning** (annars trigga 100%-alert direkt vid första debiteringscykel — baseline ~$140/mån):
   - Edit `infra/terraform/environments/prod/terraform.tfvars`: lägg till `monthly_budget_usd = 200`
   - `cd infra/terraform/environments/prod && terraform apply`
3. **Version-verifiering** (innan dev-apply):
   - Valkey: `aws elasticache describe-cache-engine-versions --engine valkey --region eu-north-1 --profile jobbpilot` — om 8.0 inte tillgängligt: ändra `redis_engine_version` + `redis_parameter_group_family` i `environments/dev/variables.tf` (ev. 7.2 + valkey7)
   - Postgres family: `aws rds describe-db-engine-versions --engine postgres --region eu-north-1 --query "DBEngineVersions[?starts_with(EngineVersion, '18')].[EngineVersion,DBParameterGroupFamily]" --profile jobbpilot` — justera `family` i `modules/rds/main.tf` om family-strängen inte är `postgres18`
4. **Bedrock model-access** (för STEG 14, inte blockerande för 13a-apply): bekräfta i eu-central-1 + eu-west-1 per `aws-setup.md §3.1`

### Apply-ordning

```powershell
$env:AWS_PROFILE = "jobbpilot"

cd infra/terraform/environments/prod
# edit terraform.tfvars (höj budget)
terraform apply

cd ../dev
terraform init   # downloadar AWS + random providers
terraform plan -out=plan.out
terraform apply plan.out  # ~15 min för RDS Multi-AZ
```

### STEG 13b kan börja efter 13a-apply (eller parallellt om Klas föredrar mock-test)

- ECR repos (jobbpilot-api, jobbpilot-worker) + lifecycle policies
- Dockerfiles för Api + Worker (multi-stage .NET 10, non-root, healthcheck-endpoint)
- ECS Fargate cluster + task-definitioner + services
- ALB + listeners (HTTPS via ACM) + target groups + health checks
- Route53 zone (jobbpilot.se eller dev.jobbpilot.se) + ACM-cert
- CloudWatch LogGroups med `retention_in_days=30` per ADR 0024 D7
- IAM execution-roles + task-roles (Bedrock-policy attach + Secrets Manager get + KMS Decrypt + ECS Exec)
- ConnectionStrings-injektion i task-defs via Secrets Manager-ARN
- KnownNetworks-overlay-värde i task-def env-vars (= VPC-CIDR `10.0.0.0/16`)

## Kända begränsningar / quirks (STEG 13a-relaterat)

- **`terraform fmt` reformaterade 2 filer** vid första körning — kommitterade i fmt-form
- **Bedrock VPC endpoint saknas** — region-mismatch (Bedrock i eu-central-1/eu-west-1, VPC i eu-north-1). Cross-region-trafik går via NAT → kostnad på data-processing. Mitigation: ADR 0025 dokumenterar
- **Single NAT i AZ-a** — AZ-a-failure → utgående trafik bryts. README dokumenterar upgrade-väg (`single_nat_gateway=false`)
- **Valkey 8.0 antagen** men ej verifierad mot AWS API (SSO utgånget). Verifieras vid `terraform plan`
- **`postgres18`-family-sträng antagen** men ej verifierad. Verifieras vid `terraform plan`
- **`final_snapshot_identifier` med `timestamp()` + `lifecycle.ignore_changes`** — unik snapshot per `terraform destroy`, men ID:t förblir stabilt mellan plans/applies
- **`auth_token` lifecycle-ignored** på Redis — rotation kräver out-of-band procedur (Sec-Minor-4 → runbook STEG 13b)

## Open follow-ups

**Operativa AWS-uppgifter (alla dokumenterade i runbooks, appliceras i STEG 13b/14):**
- Apply STEG 13a (operativt: budget + SSO + version-verifiering + apply)
- ECR + Dockerfiles + ECS + ALB + Route53 + ACM + IAM-roles (STEG 13b)
- ConnectionStrings split (jobbpilot_app + jobbpilot_worker) via DDL i RDS (STEG 14, `hangfire-schema.md §4`)
- Hangfire schema-DDL via Install.sql + REVOKE PUBLIC (STEG 14, `hangfire-schema.md §3-4`)
- GitHub Actions tag-pipeline (`v*-dev`/`v*-rc`/`v*`) — STEG 14
- Bootstrap-IAM-user cleanup (`aws-setup.md §3.4` — STEG 14 sista steg)
- VPC Flow Logs aktivering (säkerhetshygien — separat task, ev. STEG 13b)

**Sec-Minor-defererade från STEG 13a:**
- Sec-Minor-1: Single NAT SPOF (lyfts till staging)
- Sec-Minor-3: Redis AUTH alphabet (acceptabel)
- Sec-Minor-4: Redis AUTH-rotation runbook (STEG 13b)
- Sec-Minor-5: Redis CloudWatch-export (STEG 13b)
- Sec-Nit-1: Enhanced Monitoring-roll per modul-instans (lyft vid IAM-quota)
- Sec-Nit-2/3: dokumentations-noter

**Övriga TD (oförändrat sedan STEG 12):**
- TD-13 (PII-encryption Fas 2 — kombineras med TD-27)
- TD-14 (DeleteResumeVersion Fas 4)
- TD-15 (Resume-formulär a11y Fas 1)
- TD-18 (intervju-states-utökning)
- TD-19 (Worker defense-in-depth Fas 2 — inkl Hangfire.AspNetCore-trim)
- TD-20 (SqlQuery<FormattableString>-refactor opportunistiskt)
- TD-23 (Redis MULTI/EXEC opportunistiskt)
- TD-24 (cascade-paginering Fas 4)
- TD-25 (per-konto try/catch opportunistiskt)
- TD-26 (AI-kostnadstak Fas 4)
- TD-27 (EmailHash-HMAC Fas 2)
- TD-28 (Frontend typed-confirmation-UX för DELETE /me)
