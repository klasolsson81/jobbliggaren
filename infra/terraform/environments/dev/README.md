# Terraform dev environment — JobbPilot

Dev-miljö för `dev.jobbpilot.se`. STEG 13a-omfång: networking + RDS + ElastiCache + dev-secrets-placeholders.

ECS, ECR, ALB, Route53, ACM, CloudWatch LogGroups, Dockerfiles → **STEG 13b**.

## Förkrav

1. Bootstrap-stacken är applied (`infra/terraform/bootstrap/`).
2. Prod-baseline-stacken är applied (`infra/terraform/environments/prod/`) — denna stack använder dess `alias/jobbpilot-master-key` via lookup.
3. `AWS_PROFILE=jobbpilot` (SSO) eller `--profile jobbpilot` per kommando.
4. SSO-session aktiv: `aws sso login --profile jobbpilot` vid behov.

**Cost-policy:** dev-stacken är scoped som **deploy-pipeline-verifierare**, inte produktions-mirror. Multi-AZ-failover, replica-load och cross-AZ-resilience testas först i staging/prod. Lean-defaults (~$30/mån) håller dev-kostnaden under $50-budget-alerten.

## Vad som skapas

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

## Saknas (kommer i STEG 13b)

- ECR repos (`jobbpilot-api`, `jobbpilot-worker`)
- Dockerfiles (multi-stage .NET 10, non-root, healthcheck)
- ECS Fargate cluster + task-definitioner + services
- ALB + listeners + target groups
- ACM-cert för `dev.jobbpilot.se`
- Route53 zone + A-record
- CloudWatch LogGroups (`retention_in_days = 30` per ADR 0024 D7)
- IAM execution-roles + task-roles (Bedrock-policy attach + Secrets Manager get + KMS Decrypt + ECS Exec)
- DDL-init av Hangfire-schema + jobbpilot_app/jobbpilot_worker-roller (operativt, runbook `hangfire-schema.md §3-4`)
- KnownNetworks-overlay-värde i task-def env-vars (= VPC-CIDR `10.0.0.0/16`)

## Kostnad — baseline ~$30/mån utan trafik

| Resurs | ~$/mån |
|---|---|
| RDS db.t4g.micro Single-AZ + 20GB gp3 | ~$13 |
| ElastiCache cache.t4g.micro × 1 | ~$8 |
| NAT Gateway (single) | ~$32 + data |
| S3 Gateway endpoint | $0 (gratis) |
| **Totalt** | **~$53/mån** |

Med `monthly_budget_usd=50` triggar 100%-alert vid första hela debiteringscykel. Acceptabelt — det är den varning vi vill ha. Vid behov: höj till $80 vid Fas 2 (JobTech-trafik) eller `terraform destroy` mellan utvecklingspass.

**Att göra dev billigare:**
- Kör `terraform destroy` när du inte aktivt utvecklar (~15 min apply-tid att återskapa). Sparar ~$1,75/dag.
- Sätt `single_nat_gateway = false` *inte* — multi-NAT är dyrare, inte billigare. Lean-dev har redan single NAT.
- NAT Gateway är dominant cost (~$32/mån). Att ta bort den helt kräver re-design (ECS i public subnets eller VPC Endpoints för ECR + Bedrock — komplext).

**Kostnadsskillnad mot prod-spec (BUILD.md §15.1, för senare staging/prod):**
| Resurs | Dev (lean) | Prod (BUILD.md §15.1) |
|---|---|---|
| RDS | t4g.micro Single-AZ | t4g.medium Multi-AZ (~$60/mån) |
| Redis | t4g.micro × 1 | t4g.small × 2 Multi-AZ (~$25/mån) |
| Interface VPC Endpoints | Av | På (~$22/mån) |
| Multi-AZ NAT (ev. prod) | Av | Av/på (~$96/mån om på) |

## Cleanup

```powershell
# OBS: deletion_protection=true på RDS — sätt false + apply först om du verkligen vill ta bort.
terraform destroy
```

State-bucket-versioning + DynamoDB-locks är intakta efter destroy — bara dev-resurserna försvinner.
