---
session: STEG 13a
datum: 2026-05-09
slug: steg13a-terraform-stack
status: KLAR (kod-skriven, ej applied)
commits:
  - (TBD - pending push)
---

# STEG 13a — Infra-as-code-stack: networking + databas + cache (Alt A2 första block)

## Mål

Implementera första hälften av STEG 13:s scope — Terraform-modules för VPC + RDS + ElastiCache + dev-environment-stack — utan att gå in på container-infra (ECR/ECS/ALB/Route53 = STEG 13b). Pre-launch-gate-koden från STEG 12 har redan VPC-CIDR-konfiguration som env-var-injection — denna stack producerar VPC:n.

## Scope-uppdelning

Per discovery-rapport och Klas:s GO valdes uppdelning **13a + 13b** istället för bundlat STEG 13. Motivering: 13a kan smoke-testas isolerat (psql + redis-cli från throwaway-debug-task), 13b är en separat naturlig review-checkpoint för security-auditor.

## Block 1 — `modules/network/` (VPC + subnets + NAT + endpoints + base SGs)

**Implementation:**

- VPC `10.0.0.0/16` med DNS support + hostnames
- 3 AZs via `data "aws_availability_zones" "available"` (eu-north-1a/b/c förväntat)
- Public subnets `10.0.{0,1,2}.0/24` med `map_public_ip_on_launch = true`
- Private subnets `10.0.{10,11,12}.0/24` utan public-IP
- Internet Gateway + IGW-route i public route-table
- **Single NAT Gateway** i public-subnet-a med EIP. Privata route-tables (en per AZ) routar `0.0.0.0/0 → NAT[0]`. Cost-optimized: ~$32/mån vs ~$96 för Multi-AZ NAT (tre EIPs + tre NAT-Gateways). Tradeoff: AZ-a-failure → utgående trafik bryts från AZ-b/c privata subnets.
- 5 security groups med strikt principal-based ingress:
  - `alb-sg` — 80 + 443 från `0.0.0.0/0`
  - `ecs-sg` — 8080 från `alb-sg` (referenced)
  - `rds-sg` — 5432 från `ecs-sg`
  - `redis-sg` — 6379 från `ecs-sg`
  - `vpce-sg` — 443 från `ecs-sg`
- VPC Endpoints (gated på `var.enable_vpc_endpoints = true`):
  - S3 Gateway endpoint (gratis, attachad till alla privata route-tables)
  - Secrets Manager Interface endpoint (privata subnets, `private_dns_enabled = true`)
  - KMS Interface endpoint (privata subnets, `private_dns_enabled = true`)
  - Bedrock Interface endpoint **utelämnat** — Bedrock-tjänsten finns inte i `eu-north-1` (cross-region inference går till `eu-central-1`/`eu-west-1`). VPC Endpoint kan inte skapas för en frånvarande tjänst.
- ECS-egress: `0.0.0.0/0 -1` initialt — flaggat av security-auditor (Sec-Major-2), accepterat via ADR 0025

## Block 2 — `modules/rds/` (Postgres 18.3 Multi-AZ)

**Implementation:**

- Engine `postgres` 18.3, instance-class `db.t4g.medium`, Multi-AZ deployment
- Storage: gp3 20GB → 100GB auto-scale, `storage_encrypted = true` med `kms_key_id` (master-key)
- `manage_master_user_password = true` — AWS-managed Secrets Manager med automatisk rotation (default 7d). Master-secret KMS-encrypted med samma master-key.
- Performance Insights enabled med matchande KMS-key
- Enhanced Monitoring 60s med dedicated IAM-roll (`AmazonRDSEnhancedMonitoringRole`)
- `backup_retention_period = 7` dagar, `backup_window = "02:00-03:00"`, `maintenance_window = "Sun:04:00-Sun:05:00"`
- `deletion_protection = true` + `skip_final_snapshot = false` + `delete_automated_backups = false`
- `final_snapshot_identifier` med `timestamp()` + `lifecycle.ignore_changes` så ID:t förblir stabilt mellan plans men unikt per `terraform destroy`
- `enabled_cloudwatch_logs_exports = ["postgresql", "upgrade"]`
- `auto_minor_version_upgrade = true`, `apply_immediately = false` (maintenance-window-disciplin)
- Parameter group:
  - `log_statement = "none"` (säker default — se Sec-Major-1)
  - `log_min_duration_statement = "1000"` (slow-query-log för perf-debug)
  - `log_parameter_max_length = "0"` + `log_parameter_max_length_on_error = "0"` (PII-skydd)
  - `rds.force_ssl = "1"` (TLS-tvång)
- **Explicit `aws_cloudwatch_log_group`** för `postgresql` + `upgrade` med `retention_in_days = 30` + KMS — uppfyller ADR 0024 D7

## Block 3 — `modules/redis/` (Valkey 8 replication group)

**Implementation:**

- Engine `valkey` (BUILD.md §15.1 sa "Redis 8.6" — verifierades vid web-search att Redis 8.x är post-Redis-Inc-license-byte 2024 och inte AWS-supportad. AWS-rekommenderad efterföljare är **Valkey** — Redis-protokoll-kompatibel BSD-licensierad fork.)
- `engine_version = "8.0"`, `parameter_group_family = "valkey8"` (verifieras vid `terraform plan`)
- `node_type = "cache.t4g.small"`, `num_cache_clusters = 2`, `automatic_failover_enabled = true`, `multi_az_enabled = true`
- `at_rest_encryption_enabled = true` + `transit_encryption_enabled = true` + `kms_key_id` (master-key)
- AUTH-token via `random_password` 64 chars `[a-zA-Z0-9]` (~380 bits entropy, vida över NIST 256-bits-minimum). Lagras i Secrets Manager med 7d recovery + KMS-encryption.
- `lifecycle.ignore_changes = [auth_token]` — rotation kräver out-of-band procedur (Sec-Minor-4 defererad → STEG 13b runbook `redis-auth-rotation.md`)
- `snapshot_retention_limit = 7`, snapshot-window 01:00-02:00, maintenance-window sun:03:00-sun:04:00

## Block 4 — `environments/dev/` (env-stack)

**Filer:**

- `versions.tf` — TF >= 1.14, AWS ~> 5.80, random ~> 3.6
- `backend.tf` — S3-state-key `dev/main.tfstate`, encrypted, DynamoDB-locks (samma bucket + tabell som baseline)
- `providers.tf` — AWS-provider med `default_tags = var.common_tags` (Project + Environment=dev + ManagedBy + Owner)
- `variables.tf` — `name_prefix = "jobbpilot-dev"`, `vpc_cidr`, RDS/Redis-version-overrides
- `terraform.tfvars` — tomt (defaults räcker)
- `main.tf`:
  - `data "aws_kms_alias" "master"` — lookup mot baseline-stackens `alias/jobbpilot-master-key`. Cleaner än `data "terraform_remote_state"` (ingen state-permission-koppling).
  - `module "network"` (single NAT, VPC-endpoints på)
  - `module "rds"` med `kms_key_id = data.aws_kms_alias.master.target_key_arn`
  - `module "redis"` likadant
  - 2 dev-specifika `aws_secretsmanager_secret`-resurser direkt (inte via befintlig `modules/secrets_manager/` som hör till baseline):
    - `jobbpilot/dev/db/app-connection-string` (sätts post-DDL i STEG 14)
    - `jobbpilot/dev/db/hangfire-storage-connection-string` (sätts post-DDL i STEG 14)
- `outputs.tf` — VPC-IDs, subnet-IDs, SG-IDs, RDS-endpoint, Redis-endpoint, secret-ARNs
- `README.md` — körinstruktioner, verifierings-kommandon, cost-flagga (~$140/mån baseline)

## Säkerhets-fynd från security-auditor

Full rapport: `docs/reviews/2026-05-09-steg13a-security.md`.

### Fixade in-block

**Sec-Major-1 — RDS `log_statement="ddl"` + odeklarerad CloudWatch-LogGroup läcker passwords**

`log_statement = "ddl"` loggar all DDL verbatim, inklusive `CREATE ROLE jobbpilot_app PASSWORD '...'` som STEG 14 kommer att köra. Plus `enabled_cloudwatch_logs_exports = ["postgresql"]` exporterar till CloudWatch där default-retention är `Never expire` (eftersom AWS skapar LogGroupen implicit utan retention-konfig).

Fix: tre delar.
1. `log_statement = "none"` — DDL-spåras via Terraform-state + Hangfire Install.sql; Postgres-DDL-logg ger marginellt audit-värde mot kostnaden (passwords i klartext i loggen)
2. Explicit `aws_cloudwatch_log_group "rds_postgresql"` + `"rds_upgrade"` med `retention_in_days = 30` + `kms_key_id`. Måste skapas *före* RDS-instansen så AWS adopterar våra LogGroups istället för att skapa nya.
3. `hangfire-schema.md` (STEG 14 runbook) ska dokumentera att DDL-init av `jobbpilot_app/jobbpilot_worker`-passwords körs via Secrets Manager-genererade randoms, inte via interaktiv `psql` med synliga passwords

**Sec-Minor-6 — Slow-query-log inkluderar bind-värden = PII**

`log_min_duration_statement = 1000` loggar query-text + bind-värden för slow queries. Bind-värden för `WHERE email = 'klas@example.com'` blir e-postadresser i CloudWatch.

Fix: `log_parameter_max_length = "0"` + `log_parameter_max_length_on_error = "0"` (Postgres 13+) trunkerar bind-värden i logg till 0 chars → bara query-template loggas, inte värden.

**Sec-Minor-2 — Master-vs-BYOK-key-val odokumenterat**

Kommentar tillagd vid `master_user_secret_kms_key_id`: "Master-secret krypteras med master-key (app-secrets-domän per BUILD.md §8.4). BYOK-key är reserverad för envelope-encryption av användar-supplied API-keys."

### ADR-accepterad

**Sec-Major-2 — ECS-SG egress `0.0.0.0/0 -1` (alla protokoll)**

Avvägning: Bedrock cross-region-trafik kräver dynamiska egress-IPs (inga AWS-managed prefix-lists för Bedrock), Fargate-default är open egress, mitigation-stack via TLS + audit-anonymisering + IP-anonymisering + ALB-only-ingress bär huvudbördan. Hardening-väg dokumenterad i ADR.

**ADR 0025** skapad — accepterar status quo för Fas 0, omvärderingstrigger Fas 1→Fas 2-övergång (när JobTech-integration ger bredare attack-yta).

### Defererade

- Sec-Minor-1: Single NAT SPOF — acceptabel Fas 0, ~$64/mån extra för Multi-AZ NAT, lyft till staging
- Sec-Minor-3: Redis AUTH alphabet `[a-zA-Z0-9]` (5.95 bits/tecken × 64 = 380 bits, OK)
- Sec-Minor-4: Redis AUTH-rotation runbook → STEG 13b
- Sec-Minor-5: Redis CloudWatch-export → STEG 13b
- Sec-Nit-1: Enhanced Monitoring-roll per modul-instans — lyft vid IAM-quota
- Sec-Nit-2: `apply_immediately = false` — implicit doc, OK
- Sec-Nit-3: `final_snapshot_identifier`-pattern — verifierat säkert mot recycling

## Beslut

- **Valkey 8 över Redis 8.6** — BUILD.md §15.1 spec-drift. Redis 8.x post-license-byte stöds inte av AWS ElastiCache. Valkey 8 är AWS-rekommenderad Redis-protokoll-kompatibel efterföljare. Justera BUILD.md vid lämplig docs-pass.
- **Single NAT Gateway** — cost-optimized för Fas 0. Mitigation-väg dokumenterad. Multi-AZ NAT lyfts till staging.
- **Bedrock VPC Endpoint utelämnat** — region-mismatch (eu-north-1 ↔ eu-central-1/eu-west-1). Cross-region-trafik via NAT är acceptabel; PrivateLink över region-gränser kräver DTS-cost > NAT data-processing.
- **`data "aws_kms_alias"`-lookup** över `data "terraform_remote_state"` — undviker state-permission-koppling, gör baseline-stacken disposabel.
- **Dev-secrets direkt i env-stack** istället för refactor av `modules/secrets_manager/` — säkrare väg som inte rör baseline-state.
- **AWS-managed master-password** (`manage_master_user_password = true`) — bättre än egen `random_password` + Secrets Manager-rotation-implementation.
- **`log_statement = "none"`** — säkraste default. DDL-audit kommer från Terraform-state + Install.sql, inte Postgres-logg.
- **ECS-egress acceptance via ADR 0025** — pragmatisk Fas 0-position med omvärderingstrigger.

## Quirks/spec-drift upptäckt

- **BUILD.md §15.1 "Redis 8.6"** är inte AWS-supportad version (Redis 8.x är post-license-byte). Justera till Valkey 8.x vid lämplig docs-pass.
- **`postgres18` family-sträng** antagen men inte verifierad mot AWS API (SSO utgånget). Verifieras vid `terraform plan`. Möjligt att stränget är `postgres18.0` eller liknande.
- **Valkey 8.0 / `valkey8` parameter-group-family** antagna. Verifieras via `aws elasticache describe-cache-engine-versions --engine valkey --region eu-north-1`.

## Inte applied

`terraform apply` kräver:
1. SSO-login (`aws sso login --profile jobbpilot`)
2. Budget-höjning ($50 → $200 i `environments/prod/terraform.tfvars`) + apply på prod-stacken
3. Version-verifiering (Valkey 8.0, postgres18 family-sträng) — justera variables vid behov
4. Apply-ordning: prod (budget) → dev (init + plan + apply, ~15 min)

Allt operativt — Klas kör manuellt.

## Commits

| SHA | Beskrivning |
|-----|-------------|
| (TBD) | feat(infra): STEG 13a — Terraform dev-stack (network + rds + redis) |
| (TBD) | docs: STEG 13a docs-sync (current-work + steg-tracker + ADR 0025 + reviews + session-logg) |

## Tester totalt (oförändrat)

- **Backend:** 537 (157 Domain + 183 Application + 23 Architecture + 26 Worker + 148 Api Integration)
- **Frontend:** 65 Vitest + 19 Playwright E2E

Ingen .NET- eller frontend-kod rörd i 13a → ingen test-impact.

## Reviews

| Rapport | Status |
|---|---|
| `docs/reviews/2026-05-09-steg13a-security.md` | Approved with 2 Major (Sec-Major-1 + Sec-Minor-6 fixade in-block; Sec-Major-2 ADR-accepterad via 0025), 5 Minor + 3 Nit defererade |

dotnet-architect ej invocerad — STEG 13a är ren infra (Terraform/HCL), ingen .NET-domän-design. Lyfts vid STEG 13b när Worker/Api task-definitioner skrivs.

## Lärdomar STEG 13a

- **`log_statement = "ddl"` är klassisk Postgres-fotgropfälla** — DDL-statements innehåller `CREATE/ALTER ROLE PASSWORD '...'` i klartext. Sätt `none` i prod, byt till `mod` bara om DDL-audit verkligen behövs (och då via en pattern som strippar passwords pre-execution).
- **`log_min_duration_statement` ensam är inte PII-säker** — slow-query-textens bind-värden loggas. Krävs `log_parameter_max_length = 0` (PG 13+) för att trunkera. Båda parametrarna måste alltid följas åt.
- **AWS-managed CloudWatch LogGroups** skapas implicit av RDS/ECS/Lambda med default `Never expire` — alltid deklarera explicit `aws_cloudwatch_log_group` *före* tjänst-skapning för att kontrollera retention + KMS.
- **Cross-region Bedrock från eu-north-1** fungerar via NAT Gateway, men ingen lokal VPC Endpoint finns för Bedrock i regioner där tjänsten inte är hostad. PrivateLink över region-gränser kräver DTS-cost > NAT data-processing.
- **`data "aws_kms_alias"`-lookup** mellan stackar > `data "terraform_remote_state"`. Inga tfstate-permissions att hantera, ingen koppling till baseline-stackens version.
- **AWS-managed master-password** (`manage_master_user_password = true`) > egen `random_password` + Secrets Manager-rotation. Cleaner, automatisk rotation, mindre kod.
- **Redis 8.x är inte AWS-supportad** efter Redis Inc:s license-byte 2024 — Valkey 8 är AWS:s Redis-kompatibla efterföljare. BUILD.md §15.1 spec-drift identifierad.
- **`final_snapshot_identifier` med `timestamp()` + `lifecycle.ignore_changes`** är icke-trivialt rätt — `timestamp()` evalueras vid plan/apply men `ignore_changes` håller ID:t stabilt mellan plans. Vid destroy tas state-värdet → unik snapshot. Många infra-stacks ute i naturen får detta fel.
- **Single NAT cost-optimization** är medvetet val, inte glömt. README + steg-tracker dokumenterar upgrade-väg.

## Nästa session

**Två alternativa nästa-steg:**

**Alt 1 — Operativt (kort)**: Klas kör SSO-login + budget-höjning + version-verifiering + `terraform apply` mot dev-stacken. Smoke-testa via `aws rds describe-db-instances` + `aws elasticache describe-replication-groups`. Inget kod-skrivande. Ger feedback om version-strängar är fel + isolerar 13a-state.

**Alt 2 — STEG 13b parallellt** (medan Klas applyar 13a separat): bygg ECR + Dockerfiles + ECS-cluster + ALB + Route53 + ACM + CloudWatch LogGroups + IAM-roles. Förutsätter att 13a-apply ger oss faktiska VPC/subnet-IDs (men de är deterministiska från Terraform-output, så 13b-koden kan skrivas mot `data "terraform_remote_state"` eller via explicit subnet-CIDR-input).

Min rek: **Alt 1 först** så vi får apply-feedback (Valkey/PG version-strängar, AZ-namn, ev. AWS-API-quirks) innan 13b. Spar omarbete om något är fel.

## Open follow-ups (utöver STEG 13b-scope)

- VPC Flow Logs aktivering (ev. STEG 13b eller separat hygien-task)
- BUILD.md §15.1 docs-uppdatering: "Redis 8.6" → "Valkey 8" (separat docs-pass)
- Redis AUTH-rotation runbook (`docs/runbooks/redis-auth-rotation.md`) — STEG 13b
- Redis CloudWatch-export aktivering — STEG 13b
- Bootstrap-IAM-user cleanup — STEG 14 sista steg per `aws-setup.md §3.4`
