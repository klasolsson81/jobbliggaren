# Terraform dev environment — JobbPilot

Dev-miljö för `dev.jobbpilot.se` (initialt mot ALB-default-DNS innan domän finns).

**STEG 13a:** networking + RDS + ElastiCache + dev-secrets-placeholders.
**STEG 13b:** ECR + IAM + CloudWatch LogGroups + ALB + ECS Fargate + Dockerfiles + `/api/ready`-endpoint.

Route53 + ACM (HTTPS) tillkommer som STEG 13c när `jobbpilot.se` registreras. GitHub Actions tag-pipeline = STEG 14.

## Förkrav

1. Bootstrap-stacken är applied (`infra/terraform/bootstrap/`).
2. Prod-baseline-stacken är applied (`infra/terraform/environments/prod/`) — denna stack använder dess `alias/jobbpilot-master-key` via lookup.
3. `AWS_PROFILE=jobbpilot` (SSO) eller `--profile jobbpilot` per kommando.
4. SSO-session aktiv: `aws sso login --profile jobbpilot` vid behov.

**Cost-policy:** dev-stacken är scoped som **deploy-pipeline-verifierare**, inte produktions-mirror. Multi-AZ-failover, replica-load och cross-AZ-resilience testas först i staging/prod. Lean-defaults (~$30/mån) håller dev-kostnaden under $50-budget-alerten.

## Vad som skapas

### STEG 13a — Networking + databas + cache

| Resurs | Detalj |
|--------|--------|
| VPC | `10.0.0.0/16`, 3 AZs (a/b/c) |
| Public subnets | `10.0.0.0/24`, `.1.0/24`, `.2.0/24` (för ALB + NAT) |
| Private subnets | `10.0.10.0/24`, `.11.0/24`, `.12.0/24` (för ECS + RDS + Redis) |
| Internet Gateway | För publika subnets |
| NAT Gateway | **En** i AZ-a (cost-optimized; AZ-a-failure → utgående trafik bryts) |
| VPC Endpoints | S3 Gateway (gratis). Interface-endpoints (SM + KMS) **AV i dev** — sparar ~$22/mån; SM/KMS-trafik går via NAT istället. |
| Security groups | `alb`, `ecs`, `rds`, `redis` (`vpce` skapas bara om interface-endpoints aktiveras) |
| RDS | Postgres 18.3, **db.t4g.micro Single-AZ**, gp3 20GB → 100GB auto-scale, KMS-encrypted, Performance Insights, Enhanced Monitoring |
| RDS master-pwd | AWS-managed via Secrets Manager (auto-rotation 7d default) |
| ElastiCache | Valkey 8.0, **cache.t4g.micro × 1 nod (single primary)**, transit + at-rest encryption, AUTH-token i Secrets Manager |
| Secrets-placeholders | `jobbpilot/dev/db/app-connection-string` + `jobbpilot/dev/db/hangfire-storage-connection-string` (sätts post-DDL i STEG 14) |

### STEG 13b — Container-infra

| Resurs | Detalj |
|--------|--------|
| ECR repos | 2 separata: `jobbpilot-dev-api`, `jobbpilot-dev-worker`. KMS-encrypted, scan_on_push, lifecycle keep-last-10. MUTABLE-taggar i dev (`latest` återanvänds). |
| CloudWatch LogGroups | 3 grupper: `/aws/ecs/jobbpilot-dev/{api,worker,ecs-exec}`, **30d retention** + KMS (per ADR 0024 D7) |
| IAM execution-role | ECR pull + CloudWatch put + Secrets Manager get + KMS Decrypt (least-privilege per repo + log-group + secret) |
| IAM task-role-api | Bedrock Invoke (via baseline `JobbPilotBedrockInvoke`-policy attach) + Secrets Manager runtime-read + KMS Decrypt + ECS Exec |
| IAM task-role-worker | Secrets Manager runtime-read + KMS Decrypt + ECS Exec. **Ingen Bedrock i Fas 1** — lyfts vid Fas 4 när AI-jobb introduceras. |
| ALB | Internet-facing, 2 AZ, drop_invalid_header_fields, 60s idle-timeout. **HTTP-only initialt** (port 80 → target-group-api). HTTPS-listener gated på `var.alb_https_enabled` (kräver ACM-cert + domän). |
| ALB target-group-api | port 8080, target_type=ip (Fargate awsvpc), health-check `/api/ready` (30s interval, 2 healthy / 3 unhealthy thresholds), 30s deregistration |
| ECS cluster | `jobbpilot-dev-cluster` med Container Insights. Capacity providers: FARGATE + FARGATE_SPOT (default SPOT i dev = ~70% rabatt). |
| ECS task-def-api | 0.5 vCPU + 1 GB, port 8080, secrets-injection (Postgres + Redis-AUTH), env-vars (Redis-host, KnownNetworks=10.0.0.0/16), HEALTHCHECK curl /api/ready, non-root |
| ECS task-def-worker | 0.25 vCPU + 0.5 GB, **HTTP-fri (ADR 0023)**, secrets (HangfireStorage + Postgres + Redis-AUTH), stopTimeout=30s (TD-17 SIGTERM) |
| ECS service-api | desired_count=1 (lean), ALB-target-group-attached, deployment_circuit_breaker, ECS Exec aktivt |
| ECS service-worker | desired_count=1, ingen ALB-koppling |
| Auto-scaling | **AV i dev** (`enable_autoscaling=false`). Staging/prod sätter `true` → CPU-target-tracking 70% (1-10 Api, 1-4 Worker). |
| Dockerfiles | Multi-stage .NET 10 (sdk → aspnet-runtime). Non-root (`USER app`). Api: `EXPOSE 8080` + `HEALTHCHECK curl /api/ready`. Worker: ingen port, ingen healthcheck. |

## Körning

```powershell
cd infra/terraform/environments/dev
$env:AWS_PROFILE = "jobbpilot"
terraform init
terraform plan -out=plan.out
terraform apply plan.out
```

## Verifiering efter apply

```powershell
# RDS
aws rds describe-db-instances --db-instance-identifier jobbpilot-dev-rds --profile jobbpilot
aws rds describe-db-snapshots --db-instance-identifier jobbpilot-dev-rds --profile jobbpilot

# ElastiCache
aws elasticache describe-replication-groups --replication-group-id jobbpilot-dev-redis --profile jobbpilot

# VPC
aws ec2 describe-vpcs --filters "Name=tag:Name,Values=jobbpilot-dev-vpc" --profile jobbpilot
aws ec2 describe-vpc-endpoints --filters "Name=tag:Project,Values=JobbPilot" --profile jobbpilot

# Secrets
aws secretsmanager list-secrets --profile jobbpilot | grep "jobbpilot/dev"
```

## Connectivity-smoke-test (STEG 13a slut)

Eftersom inget ECS finns ännu (det är 13b), connectivity-smoke testas via temporär bastion eller **AWS Systems Manager Session Manager** mot en throwaway-EC2 i private subnet.

**Alternativ A — `psql`-test mot RDS via SSM-session:**

1. Hämta master-creds från Secrets Manager:
   ```powershell
   aws secretsmanager get-secret-value `
     --secret-id (terraform output -raw rds_master_user_secret_arn) `
     --profile jobbpilot
   ```
2. Spinn upp en debug-task i private subnet (efter STEG 13b finns Api-tasken som kan användas direkt). Tills dess: skip — connectivity verifieras vid första Api-task-start i 13b.

**Alternativ B (rekommenderat):** smoke-test vänta till STEG 13b när Api-task finns. Verifiera då via:
```powershell
aws ecs execute-command --cluster <cluster> --task <task-id> --container api --command "/bin/sh" --interactive --profile jobbpilot
# i shellet: testa psql + redis-cli mot endpoints
```

## Operativt: docker build + push innan första apply

ECS-tasks kraschar med `image_pull_failure` om ECR-repos är tomma vid första apply. Bygg + push manuellt:

```powershell
$env:AWS_PROFILE = "jobbpilot"

# 1. Apply STEG 13a + 13b foundation först (skapar ECR-repos)
cd infra\terraform\environments\dev
terraform apply -target=module.ecr -target=module.cloudwatch_logs -target=module.iam_ecs

# 2. Login + build + push från repo-root
$ECR = (terraform output -raw ecr_api_repository_url) -replace '/.*$', ''
aws ecr get-login-password --region eu-north-1 --profile jobbpilot | docker login --username AWS --password-stdin $ECR

cd ..\..\..   # tillbaka till repo-root
docker build -f src/JobbPilot.Api/Dockerfile -t (terraform -chdir=infra/terraform/environments/dev output -raw ecr_api_repository_url):latest .
docker push (terraform -chdir=infra/terraform/environments/dev output -raw ecr_api_repository_url):latest

docker build -f src/JobbPilot.Worker/Dockerfile -t (terraform -chdir=infra/terraform/environments/dev output -raw ecr_worker_repository_url):latest .
docker push (terraform -chdir=infra/terraform/environments/dev output -raw ecr_worker_repository_url):latest

# 3. Sedan full apply (skapar resterande resurser inkl. ECS som drar images)
cd infra\terraform\environments\dev
terraform apply
```

STEG 14 ersätter denna manuella process med GitHub Actions (`v*-dev`-tag → build → push → ECS service-update).

## Saknas (kommer i STEG 13c eller STEG 14)

- ACM-cert för `dev.jobbpilot.se` (kräver Route53 + domän-registrering — STEG 13c eller separat)
- Route53 zone + A-record (samma)
- HTTPS-listener på ALB (gated på `var.alb_https_enabled` — flippa när cert finns)
- DDL-init av Hangfire-schema + `jobbpilot_app`/`jobbpilot_worker`-roller (operativt, runbook `hangfire-schema.md §3-4`) — **STEG 14**
- GitHub Actions tag-pipeline (`v*-dev`/`v*-rc`/`v*`) — **STEG 14**
- VPC Flow Logs (säkerhetshygien — separat task)

## Kostnad — baseline efter full apply (lean dev, FARGATE_SPOT)

| Resurs | ~$/mån |
|---|---|
| **STEG 13a:** | |
| RDS db.t4g.micro Single-AZ + 20GB gp3 | ~$13 |
| ElastiCache cache.t4g.micro × 1 | ~$8 |
| NAT Gateway (single) | ~$32 + data |
| S3 Gateway endpoint | $0 (gratis) |
| **STEG 13b:** | |
| ALB (fixed, även med 0 tasks) | ~$16 |
| ECS Fargate Api 0.5 vCPU + 1 GB (SPOT) | ~$5 |
| ECS Fargate Worker 0.25 vCPU + 0.5 GB (SPOT) | ~$2.50 |
| ECR storage (~5 GB images) | ~$0.50 |
| CloudWatch Logs (~2 GB ingest) | ~$2 |
| **Totalt apply'd** | **~$79/mån** |

Med `monthly_budget_usd=50` triggar 100%-ACTUAL ~halva månaden in (~dag 19). Det är dokumenterad disciplin per ADR 0005 revision 2026-05-09.

**Att göra dev billigare:**
- **`terraform destroy` mellan utvecklingspass** (rekommenderat): återskapa ~15 min, spar ~$2.60/dag inaktiv tid
- **`var.api_desired_count = 0` + `worker_desired_count = 0`** vid längre paus: stoppar ECS-tasks (~$7/mån sparat) men ALB ($16) + RDS ($13) + Redis ($8) + NAT ($32) kvarstår
- **FARGATE_SPOT** redan aktivt (`var.use_fargate_spot = true`) → ~70% rabatt mot FARGATE on-demand
- **Skip ALB tillfälligt:** komplicerat (kräver kommentera bort `module.alb` + `module.ecs.api_target_group_arn`); inte rekommenderat

**Kostnadsskillnad mot prod-spec (BUILD.md §15.1, för senare staging/prod):**
| Resurs | Dev (lean) | Prod (BUILD.md §15.1) |
|---|---|---|
| RDS | t4g.micro Single-AZ | t4g.medium Multi-AZ (~$60/mån) |
| Redis | t4g.micro × 1 | t4g.small × 2 Multi-AZ (~$25/mån) |
| Interface VPC Endpoints | Av | På (~$22/mån) |
| ECS Api | 0.5 vCPU + 1 GB × 1 SPOT | 1 vCPU + 2 GB × 2 (autoscale till 10) FARGATE |
| ECS Worker | 0.25 vCPU + 0.5 GB × 1 SPOT | 0.5 vCPU + 1 GB × 1 (autoscale till 4) FARGATE |
| Auto-scaling | Av | På |
| ALB deletion_protection | Av | På |

## Cleanup

```powershell
# OBS: deletion_protection=true på RDS — sätt false + apply först om du verkligen vill ta bort.
terraform destroy
```

State-bucket-versioning + DynamoDB-locks är intakta efter destroy — bara dev-resurserna försvinner.
