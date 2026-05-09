# Security-audit: STEG 13a (Terraform dev-stack — networking + RDS + ElastiCache)

**Status:** Approved with Major-findings (Sec-Major-1 + Sec-Major-2 kräver fix/ADR-beslut innan apply)
**Granskat:** 2026-05-09
**Auktoritet:** GDPR Art. 5/32, CLAUDE.md §5.4 + §11, ADR 0024 D7, TD-21, BUILD.md §15.1

**Granskat scope:**
- `infra/terraform/modules/network/{versions,variables,main,outputs}.tf`
- `infra/terraform/modules/rds/{versions,variables,main,outputs}.tf`
- `infra/terraform/modules/redis/{versions,variables,main,outputs}.tf`
- `infra/terraform/environments/dev/{versions,backend,providers,variables,terraform.tfvars,main,outputs,README}.{tf,md}`

**Kontext:** Korrekt komposition mot existerande `alias/jobbpilot-master-key` via `data "aws_kms_alias"`-lookup. Inga state-läckage cross-stack. Inga `0.0.0.0/0`-ingress utom ALB:s avsiktliga 80/443 (förväntat). Encryption end-to-end på RDS + Redis. Secrets-arkitektur följer GRANT-modellen. Inga PII-fält idag (placeholders, värden sätts STEG 14).

---

## Critical

Inga.

---

## Major

### Sec-Major-1 — RDS `log_statement = "ddl"` + odeklarerad CloudWatch-LogGroup läcker passwords till `Never expire`-logg

**Filer:** `infra/terraform/modules/rds/main.tf:23-27` + `:87` (`enabled_cloudwatch_logs_exports = ["postgresql", ...]`)

`log_statement = "ddl"` loggar alla DDL-statements *verbatim*, inklusive `CREATE ROLE jobbpilot_app PASSWORD '...'` och `ALTER ROLE ... PASSWORD '...'`. Dessa exporteras till CloudWatch Logs via `enabled_cloudwatch_logs_exports`. Konsekvens: när STEG 14:s DDL-init körs (skapar `jobbpilot_app` + `jobbpilot_worker` med passwords) hamnar passwords i klartext i `/aws/rds/instance/jobbpilot-dev-rds/postgresql`-loggen.

CloudWatch LogGroup för RDS-export skapas automatiskt av AWS — den ärver default-retention (`Never expire` om ingen `aws_cloudwatch_log_group`-resurs deklareras med matchande namn först). Det innebär två kombinerade problem:

1. **Secret-leak** — passwords i log = §5.4 "Secrets i log" = Blocker-kategori.
2. **Retention-glidning** — ADR 0024 D7 säger 30 dagar för app+audit-loggar. RDS-export-LogGroupen är inte deklarerad i Terraform (skapas implicit av RDS) → default `Never expire` → permanent secret-retention tills någon manuellt ändrar.

**Fix:**

A. Byt `log_statement = "ddl"` → `log_statement = "none"`. DDL-spåras redan via Terraform-state (Terraform äger schema) + Hangfire-installations-script (engångsoperation). Postgres-DDL-logg ger marginellt audit-värde mot kostnaden.

B. Deklarera explicit:

```hcl
resource "aws_cloudwatch_log_group" "rds_postgresql" {
  name              = "/aws/rds/instance/${var.name_prefix}-rds/postgresql"
  retention_in_days = 30
  kms_key_id        = var.kms_key_id

  tags = merge(var.tags, { Name = "${var.name_prefix}-rds-postgresql-log" })
}

resource "aws_cloudwatch_log_group" "rds_upgrade" {
  name              = "/aws/rds/instance/${var.name_prefix}-rds/upgrade"
  retention_in_days = 30
  kms_key_id        = var.kms_key_id
}
```

Detta uppfyller ADR 0024 D7 (30 dagar) och KMS-krypterar exporterad data.

C. Dokumentera i `hangfire-schema.md` (STEG 14 runbook) att DDL-init av `jobbpilot_app/jobbpilot_worker`-passwords ska göras via Secrets Manager-genererade randoms, *inte* via interaktiv `psql` med synliga passwords.

**Konsekvens om ej fixat:** GDPR Art. 32 (säkerhet vid behandling) + JobbPilot §5.4 secrets-i-log. Blockerar STEG 14 DDL-apply.

**Status:** Måste fixas innan `terraform apply` på dev.

### Sec-Major-2 — ECS security-group `egress_all` öppnar all utgående trafik till `0.0.0.0/0`

**Fil:** `infra/terraform/modules/network/main.tf:208-213`

```hcl
resource "aws_vpc_security_group_egress_rule" "ecs_egress_all" {
  security_group_id = aws_security_group.ecs.id
  ip_protocol       = "-1"
  cidr_ipv4         = "0.0.0.0/0"
}
```

Kommentaren säger "Egress till RDS, Redis, VPCE, internet via NAT" — men regeln är `-1` (alla protokoll) till `0.0.0.0/0`. AWS-default men strider mot **least-privilege**.

Konkret attack-yta: en komprometterad ECS-task kan exfiltrera data till valfri internet-host via NAT (DNS-tunnel, paste.ee, Discord-webhooks, etc.). Bedrock-trafik går till AWS-managed endpoints i eu-central-1/eu-west-1 — kända ranges.

**Fix-alternativ (Klas väljer):**

**A — Pragmatic (rekommenderat för Fas 0):** Behåll, men dokumentera som accepterad risk i ny ADR. Motivering: Bedrock cross-region IPs är dynamiska, GitHub/NuGet/npm kräver internet, no-egress-restriction är industri-standard för ECS Fargate Fas 0.

**B — Hardened (Fas 1+):** Begränsa egress till:
- `443/tcp` till `0.0.0.0/0` (HTTPS only — blockera plaintext-exfil)
- `5432/tcp` till `aws_security_group.rds.id` (referenced)
- `6379/tcp` till `aws_security_group.redis.id` (referenced)
- `53/udp` till `0.0.0.0/0` (DNS via NAT)

Detta blockerar t.ex. exfil via raw TCP-sockets på godtyckliga portar.

**Status:** Major. Acceptabel om Klas tar A med ADR. Annars Fix B innan apply. Inte direkt GDPR-blockerande eftersom data still-i-transit krypteras (TLS), men attack-yta-värde är reell.

---

## Minor

### Sec-Minor-1 — Single NAT Gateway = AZ-a är SPOF för utgående trafik

**Fil:** `infra/terraform/environments/dev/main.tf:22` + `modules/network/main.tf:9`

`single_nat_gateway = true` betyder en NAT i AZ-a; privata subnets i AZ-b/c routar via AZ-a. Vid AZ-a-failure: ECS-tasks i AZ-b/c kan inte nå Bedrock, Secrets Manager (utan VPCE — VPCE finns dock i alla 3 AZs, så det skyddar partiellt), eller internet generellt.

GDPR-bedömning: ingen direkt GDPR-implikation — degraded availability, inte data-läckage. Men Service-availability < 99.9% kan trigga DPA-SLA-villkor mot framtida B2B-kunder.

**Mitigation-väg dokumenterad?** README §"Vad som skapas" anger "AZ-a-failure → utgående trafik bryts" — bra, men docs/runbooks saknar disaster-recovery-procedur (toggle `single_nat_gateway = false` och apply tar ~5 min).

**Status:** Defererad (acceptabelt Fas 0, ~$32×2 = $64 extra/mån för full HA). Lyft till staging.

### Sec-Minor-2 — RDS `master_user_secret_kms_key_id` använder master-key (rätt val, men dokumentera)

**Fil:** `infra/terraform/modules/rds/main.tf:68`

`master_user_secret_kms_key_id = var.kms_key_id` där `var.kms_key_id = alias/jobbpilot-master-key`. Rätt val per BUILD.md §8.4 (master-key för app-secrets; BYOK-key reserved för envelope-encryption av användar-API-keys). Men koden saknar kommentar som dokumenterar val — risk att framtida dev byter till BYOK-key av "konsistensskäl".

**Fix:** Lägg kommentar:
```hcl
# Master-secret krypteras med master-key (app-secrets-domän per BUILD.md §8.4).
# BYOK-key är reserverad för envelope-encryption av användar-supplied API-keys.
master_user_secret_kms_key_id = var.kms_key_id
```

**Status:** Defererad. Klas eller dotnet-architect adresserar i samma round som Sec-Major-1.

### Sec-Minor-3 — `random_password` för Redis AUTH genererar `[a-zA-Z0-9]` istället för full alphabet

**Fil:** `infra/terraform/modules/redis/main.tf:29-38`

`special = false` förenklar config-konsumtion (ingen URL-escape) men minskar entropy från ~6.6 bits/tecken (full a-zA-Z0-9!&#$^<>-) till 5.95 bits/tecken (a-zA-Z0-9). 64 tecken × 5.95 = ~380 bits — vida över NIST 256-bits-minimum. Acceptabelt.

**Status:** Defererad. Säkerhetsmässigt OK; aspect noterad om någon i framtiden minskar `length`.

### Sec-Minor-4 — `random_password.auth_token` lifecycle-ignore på `auth_token` skapar rotation-friktion

**Fil:** `infra/terraform/modules/redis/main.tf:93-99`

`lifecycle.ignore_changes = [auth_token]` betyder Terraform aldrig replar AUTH-token. Rotation kräver out-of-band procedur eller `terraform taint` (destruktiv för replication-gruppen). ElastiCache stödjer `auth_token_update_strategy` (`SET`/`ROTATE`/`DELETE`) — ej exponerad för replication-group i Terraform-resursen.

ADR-värde: dokumentera rotations-procedur i `docs/runbooks/redis-auth-rotation.md` innan första rotationsbehov. ADR 0024 har inget på detta idag.

**Status:** Defererad. Skapa runbook som del av STEG 13b eller separat task.

### Sec-Minor-5 — Saknad `enabled_cloudwatch_logs_exports = ["slow-log", "engine-log"]` på Redis

**Fil:** `infra/terraform/modules/redis/main.tf`

ElastiCache stödjer `log_delivery_configuration` (slow-log + engine-log → CloudWatch Logs). Idag saknas det helt → ingen visibility över Redis-långsamma-queries eller errors. Inte säkerhetsblockerande, men hygien-åtgärd. Om läggs till: deklarera LogGroup explicit med `retention_in_days = 30` per ADR 0024 D7.

**Status:** Defererad. Adressera i STEG 13b när hela CloudWatch-LogGroup-uppsättningen sätts.

### Sec-Minor-6 — Slow-query-log inkluderar query-text och bind-värden (PII via CloudWatch)

**Fil:** `infra/terraform/modules/rds/main.tf:30-33`

`log_min_duration_statement = 1000` loggar varje query som tar > 1s, inklusive query-text + bind-värden. Bind-värden för `SELECT * FROM users WHERE email = 'klas@example.com'` blir e-postadresser i CloudWatch.

**Fix:** Sätt också parametern `log_parameter_max_length = 0` (Postgres 13+) som trunkerar bind-värden i logg till 0 chars → bara query-template loggas, inte värden.

```hcl
parameter {
  name  = "log_parameter_max_length"
  value = "0"
}
parameter {
  name  = "log_parameter_max_length_on_error"
  value = "0"
}
```

**Status:** Måste adresseras tillsammans med Sec-Major-1 — utan denna parameter kombinerad med `log_statement = "none"` läcker slow-queries fortfarande PII via bind-värden.

**Eskalering:** GDPR-relevant (PII via slow-query-log → CloudWatch utan retention-cap). Hör ihop med Sec-Major-1.

---

## Nit

### Sec-Nit-1 — `monitoring_role_arn` skapas på modul-nivå men kan delas

**Fil:** `infra/terraform/modules/rds/main.tf:113-134`

Enhanced Monitoring-rollen skapas per RDS-modul-instans. Vid staging+prod blir det 3× rolen (jobbpilot-dev/staging/prod-rds-monitoring). Trivial overhead. Acceptabel — rollens IAM-policy är AWS-managed.

**Status:** Defererad. Lyft om rolle-mängd blir IAM-quota-issue.

### Sec-Nit-2 — `apply_immediately = false` är default men dokumenterad

Bra — implicit doc att maintenance-window används för changes, inte downtime mid-day. Praise.

### Sec-Nit-3 — `final_snapshot_identifier` med `timestamp()` + `lifecycle.ignore_changes` är säker mot recycling

**Fil:** `infra/terraform/modules/rds/main.tf:95, 101-106`

Verifierat: `timestamp()` evalueras vid `terraform plan/apply`. Med `ignore_changes = [final_snapshot_identifier]` regenereras inte ID:t i framtida plans → samma ID stannar i state. Vid `terraform destroy` används state-värdet → unik snapshot per destroy. Korrekt mönster.

**Praise:** detta är icke-trivialt rätt — flera infra-stacks ute i naturen får detta fel.

---

## Praise

- **Komposition via `data "aws_kms_alias"`-lookup** istället för cross-stack `terraform_remote_state` — undviker state-koppling, gör baseline-stacken disposabel.
- **Inga 0.0.0.0/0-ingress utom ALB:s avsiktliga 80/443**. Alla SG-ingress använder `referenced_security_group_id` — strikt principal-based.
- **VPC Endpoints för Secrets Manager + KMS med `private_dns_enabled = true`** — Secrets-anrop går aldrig via NAT → mindre attack-yta + mindre cost.
- **`publicly_accessible = false` på RDS**, RDS+Redis i private subnets only.
- **`storage_encrypted = true` + `performance_insights_kms_key_id` + `master_user_secret_kms_key_id` + `at_rest_encryption_enabled` + `transit_encryption_enabled`** — encryption end-to-end, alla på master-key.
- **`rds.force_ssl = 1`** — TLS-tvång för alla DB-anrop. GDPR Art. 32 (in-transit).
- **`deletion_protection = true` + `skip_final_snapshot = false` + `delete_automated_backups = false`** — defensiv data-protection.
- **`copy_tags_to_snapshot = true`** — tagging följer snapshots.
- **AWS-managed master-password (`manage_master_user_password = true`)** med auto-rotation 7d default.
- **AUTH-token för Redis i Secrets Manager med separate KMS-encryption + 7d recovery window** — symmetri med master-secret.
- **`single_nat_gateway = true` med dokumenterad upgrade-väg** — cost-pragmatik medvetet vald, inte glömd.
- **README dokumenterar pre-apply budget-justering** (~$140/mån = höj `monthly_budget_usd` till $200) — proaktivt mot 100%-alert.
- **Tagging-konsistens** (`Project`, `Environment`, `ManagedBy`, `Owner`) via `default_tags` på provider + per-resurs `Name` + `Purpose`.
- **Inga PII-fält i secret-namnen** — `app-connection-string` / `hangfire-storage-connection-string` är funktionella, inte personliga.

---

## Sammanfattning

**1 Major (Sec-Major-1) + 1 Minor (Sec-Minor-6) hör ihop och måste fixas in-block — kombinerat blockerar STEG 14 DDL-apply pga secret-leak-risk via `log_statement = "ddl"` + bind-värden i slow-query-log + odeklarerad RDS-CloudWatch-LogGroup.**

**1 Major (Sec-Major-2) över ECS-egress-all** — accepterbar Fas 0 om Klas dokumenterar i ADR; annars hardena egress.

5 Minor (1 fixad in-block: Sec-Minor-6) + 3 Nit defererade (single-NAT, AUTH-rotation-runbook, Redis-CloudWatch-export, dokumentations-kommentarer). Inga tvingar fix innan apply utöver Sec-Major-1+Sec-Minor-6.

**Block-status:** Approved för apply efter Sec-Major-1 + Sec-Minor-6 fixad. Sec-Major-2 kräver Klas:s val: ADR-acceptera eller hardena.

**Inga GDPR-blockers i ren infra-kod.** GDPR-relevansen ligger i log-konfiguration som idag öppnar PII/secret-läckage-yta — adresseras via Sec-Major-1 + Sec-Minor-6.

Rätt EU-region (eu-north-1), rätt encryption-stack, rätt SG-isolation, rätt secrets-arkitektur. Stommen är solid.
