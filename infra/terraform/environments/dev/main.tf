# ---------------------------------------------------------------------------
# Dev-miljö för dev.jobbpilot.se. STEG 13a — networking + databas + cache.
# ECS, ALB, ECR, Route53, ACM kommer i STEG 13b.
# ---------------------------------------------------------------------------

# KMS-master-key lookup via alias — undviker state-koppling till baseline-stacken.
# Aliaset skapades av modules/kms i environments/prod/baseline.tfstate.
data "aws_kms_alias" "master" {
  name = "alias/jobbpilot-master-key"
}

# ---------------------------------------------------------------------------
# Networking
# ---------------------------------------------------------------------------

module "network" {
  source = "../../modules/network"

  name_prefix        = var.name_prefix
  vpc_cidr           = var.vpc_cidr
  az_count           = 3
  single_nat_gateway = true # cost-optimized för Fas 0; uppgraderas till multi-NAT i staging/prod

  # Lean dev: bara S3-Gateway-endpoint (gratis). Interface-endpoints (~$22/mån)
  # av i dev — Secrets Manager + KMS-trafik går via NAT istället. Trivial extra
  # NAT-data-cost vid app-startup vs $22/mån besparing.
  enable_s3_endpoint         = true
  enable_interface_endpoints = false

  tags = var.common_tags
}

# ---------------------------------------------------------------------------
# RDS Postgres 18.3 (Multi-AZ, encrypted, AWS-managed master-secret)
# ---------------------------------------------------------------------------

module "rds" {
  source = "../../modules/rds"

  name_prefix            = var.name_prefix
  subnet_ids             = module.network.private_subnet_ids
  vpc_security_group_ids = [module.network.rds_security_group_id]
  kms_key_id             = data.aws_kms_alias.master.target_key_arn

  engine_version = var.rds_engine_version
  instance_class = var.rds_instance_class
  # multi_az default false (lean dev); staging/prod sätter true explicit
  db_name         = "jobbpilot"
  master_username = "jobbpilot_admin"

  backup_retention_days = 7
  deletion_protection   = true

  tags = var.common_tags
}

# ---------------------------------------------------------------------------
# ElastiCache Valkey 8 (replication group, multi-AZ, transit + at-rest)
# ---------------------------------------------------------------------------

module "redis" {
  source = "../../modules/redis"

  name_prefix            = var.name_prefix
  subnet_ids             = module.network.private_subnet_ids
  vpc_security_group_ids = [module.network.redis_security_group_id]
  kms_key_id             = data.aws_kms_alias.master.target_key_arn

  engine                 = var.redis_engine
  engine_version         = var.redis_engine_version
  parameter_group_family = var.redis_parameter_group_family
  node_type              = var.redis_node_type
  # num_cache_clusters default 1 (lean dev = single node, ingen failover);
  # staging/prod sätter 2 + automatic_failover_enabled + multi_az_enabled explicit

  tags = var.common_tags
}

# ---------------------------------------------------------------------------
# Dev-specifika placeholders för app + Hangfire connection-strings.
# Värden sätts MANUELLT efter DDL-init (STEG 14, hangfire-schema.md §3-4):
#   1. Migrations-roll skapar jobbpilot_app + jobbpilot_worker via Install.sql
#   2. Operatör skriver connection-string-värden via:
#      aws secretsmanager put-secret-value --secret-id jobbpilot/dev/db/app-connection-string ...
# Master-creds (postgres-användaren) hanteras av RDS auto-roterad secret —
# används bara för DDL-init, aldrig av appen i runtime.
# ---------------------------------------------------------------------------

resource "aws_secretsmanager_secret" "db_app_connection" {
  name                    = "jobbpilot/dev/db/app-connection-string"
  description             = "Postgres-connection-string för Api (jobbpilot_app-rollen). Sätts efter STEG 14 DDL."
  kms_key_id              = data.aws_kms_alias.master.target_key_arn
  recovery_window_in_days = 7

  tags = merge(var.common_tags, {
    Purpose = "app-db-connection"
  })
}

resource "aws_secretsmanager_secret" "db_hangfire_connection" {
  name                    = "jobbpilot/dev/db/hangfire-storage-connection-string"
  description             = "Postgres-connection-string för Worker (jobbpilot_worker-rollen, DML-only på hangfire.*). Sätts efter STEG 14 DDL."
  kms_key_id              = data.aws_kms_alias.master.target_key_arn
  recovery_window_in_days = 7

  tags = merge(var.common_tags, {
    Purpose = "worker-hangfire-connection"
  })
}

# Placeholder-versioner så ECS-execution-role kan hämta AWSCURRENT-label vid task-startup.
# STEG 14 skriver över via `aws secretsmanager put-secret-value` efter DDL — lifecycle
# ignorerar secret_string så Terraform inte vill rolla tillbaka post-STEG 14.
# Application kommer krascha vid första query (PLACEHOLDER-creds existerar inte), task
# restartar i loop tills STEG 14 sätter riktiga värden. Detta är förväntad transient
# state mellan STEG 13b-apply och STEG 14-DDL.
resource "aws_secretsmanager_secret_version" "db_app_connection_placeholder" {
  secret_id     = aws_secretsmanager_secret.db_app_connection.id
  secret_string = "Host=${module.rds.address};Port=${module.rds.port};Database=jobbpilot;Username=PLACEHOLDER_SET_BY_STEG_14;Password=PLACEHOLDER_SET_BY_STEG_14;SSL Mode=Require;Trust Server Certificate=true"

  lifecycle {
    ignore_changes = [secret_string]
  }
}

resource "aws_secretsmanager_secret_version" "db_hangfire_connection_placeholder" {
  secret_id     = aws_secretsmanager_secret.db_hangfire_connection.id
  secret_string = "Host=${module.rds.address};Port=${module.rds.port};Database=jobbpilot;Username=PLACEHOLDER_SET_BY_STEG_14;Password=PLACEHOLDER_SET_BY_STEG_14;SSL Mode=Require;Trust Server Certificate=true"

  lifecycle {
    ignore_changes = [secret_string]
  }
}

# ---------------------------------------------------------------------------
# STEG 13b — container-infra
#
# Lookup baseline JobbPilotBedrockInvoke-policy som task-role-api attachas till.
# Skapad i environments/prod/baseline (modules/bedrock_model_access).
# ---------------------------------------------------------------------------

data "aws_iam_policy" "bedrock_invoke" {
  name = "JobbPilotBedrockInvoke"
}

# ECR repos — api + worker + migrate (STEG 14b one-shot DDL-image)
module "ecr" {
  source = "../../modules/ecr"

  name_prefix      = var.name_prefix
  repository_names = ["api", "worker", "migrate"]
  kms_key_id       = data.aws_kms_alias.master.target_key_arn

  tags = var.common_tags
}

# CloudWatch LogGroups — 30d retention per ADR 0024 D7
module "cloudwatch_logs" {
  source = "../../modules/cloudwatch_logs"

  name_prefix       = var.name_prefix
  log_group_names   = ["api", "worker", "ecs-exec", "migrate"]
  retention_in_days = 30
  kms_key_id        = data.aws_kms_alias.master.target_key_arn

  tags = var.common_tags
}

# CloudWatch security-alarms — TD-68 / ADR 0031
# Metric filter + SNS-alarm för failed_access_attempt-events.
# Konsumerar api-log-gruppen där IFailedAccessLogger skriver via ILogger-pipeline.
module "cloudwatch_security_alarms" {
  source = "../../modules/cloudwatch_security_alarms"

  name_prefix          = var.name_prefix
  log_group_name       = module.cloudwatch_logs.log_group_names["api"]
  kms_key_id           = data.aws_kms_alias.master.target_key_arn
  alert_email          = var.secops_alert_email
  alarm_threshold      = var.failed_access_alarm_threshold
  alarm_period_seconds = 60

  tags = var.common_tags
}

# CloudWatch ops-alarms — ADR 0036 (v0.2-prod-launch-checklist §9.1+§9.2).
# Metric filters + SNS-alarms för:
#   - event_name=job_event_failure (SyncPlatsbankenStreamJob.LogEventFailed, EventId 5303)
#   - event_name=audit_write_failure (SystemEventAuditor.LogAuditFailure, EventId 5602)
#   - worker-log-pipeline-health (cohesion-paritet med secops-modul)
# BUILD.md §14.4 ("JobTech sync misslyckas 3 gånger i rad") + ADR 0035 §6 alarm-wire.
module "cloudwatch_ops_alarms" {
  source = "../../modules/cloudwatch_ops_alarms"

  name_prefix           = var.name_prefix
  worker_log_group_name = module.cloudwatch_logs.log_group_names["worker"]
  api_log_group_name    = module.cloudwatch_logs.log_group_names["api"]
  kms_key_id            = data.aws_kms_alias.master.target_key_arn
  alert_email           = var.ops_alert_email

  tags = var.common_tags
}

# Budget Actions — F2-P3 / ADR 0005 second amendment 2026-05-12
# Vid $50/mån-threshold-breach (100% ACTUAL) bifogas JobbPilotBedrockDeny
# på api-task-role → blockerar all Bedrock-invocation via explicit Deny.
# Reversibel via auto-detach vid cycle-reset.
# ECS-stop som automatisk Budget Action har skippats (AWS-API stödjer
# bara STOP_EC2/STOP_RDS); sekundärskydd via manuell runbook F2-P4.
module "budget_actions" {
  source = "../../modules/budget_actions"

  name_prefix      = var.name_prefix
  budget_name      = var.baseline_budget_name
  target_role_name = module.iam_ecs.task_api_role_name
  kms_key_id       = data.aws_kms_alias.master.target_key_arn
  alert_email      = var.cost_anomaly_alert_email

  tags = var.common_tags
}

# IAM-roller för ECS — execution + task-role-api + task-role-worker + task-role-migrate (STEG 14b)
module "iam_ecs" {
  source = "../../modules/iam_ecs"

  name_prefix         = var.name_prefix
  account_id          = var.account_id
  ecr_repository_arns = values(module.ecr.repository_arns)
  log_group_arns      = values(module.cloudwatch_logs.log_group_arns)
  secret_arns = [
    aws_secretsmanager_secret.db_app_connection.arn,
    aws_secretsmanager_secret.db_hangfire_connection.arn,
    module.rds.master_user_secret_arn,
    module.redis.auth_token_secret_arn,
    module.redis.connection_string_secret_arn,
  ]
  kms_key_arn               = data.aws_kms_alias.master.target_key_arn
  bedrock_invoke_policy_arn = data.aws_iam_policy.bedrock_invoke.arn

  # Migrate-roll (STEG 14b) — separat blast-radius från api/worker. PutSecretValue
  # bara på app + hangfire-connection-secrets; GetSecretValue på master-secret.
  migrate_master_secret_arn = module.rds.master_user_secret_arn
  migrate_writable_secret_arns = [
    aws_secretsmanager_secret.db_app_connection.arn,
    aws_secretsmanager_secret.db_hangfire_connection.arn,
  ]

  tags = var.common_tags
}

# ALB — internet-facing, HTTP-only initialt (HTTPS aktiveras när domän finns)
module "alb" {
  source = "../../modules/alb"

  name_prefix           = var.name_prefix
  vpc_id                = module.network.vpc_id
  public_subnet_ids     = module.network.public_subnet_ids
  alb_security_group_id = module.network.alb_security_group_id

  https_listener_enabled = var.alb_https_enabled
  acm_certificate_arn    = var.alb_acm_certificate_arn

  # Lean dev = ingen deletion protection (lättare destroy mellan utvecklingspass)
  enable_deletion_protection = false

  tags = var.common_tags
}

# ECS — cluster + task-defs + services + (valbar) autoscaling
module "ecs" {
  source = "../../modules/ecs"

  name_prefix           = var.name_prefix
  aws_region            = var.aws_region
  private_subnet_ids    = module.network.private_subnet_ids
  ecs_security_group_id = module.network.ecs_security_group_id

  execution_role_arn   = module.iam_ecs.execution_role_arn
  task_api_role_arn    = module.iam_ecs.task_api_role_arn
  task_worker_role_arn = module.iam_ecs.task_worker_role_arn

  # ECR images — taggar styrs av var.api_image_tag / var.worker_image_tag.
  # Initial smoke: bygg + push manuellt med `latest`-tag innan första apply
  # (annars startar ECS-tasks utan image → image_pull_failure).
  api_image_uri    = "${module.ecr.repository_urls["api"]}:${var.api_image_tag}"
  worker_image_uri = "${module.ecr.repository_urls["worker"]}:${var.worker_image_tag}"

  api_target_group_arn = module.alb.api_target_group_arn

  api_log_group_name    = module.cloudwatch_logs.log_group_names["api"]
  worker_log_group_name = module.cloudwatch_logs.log_group_names["worker"]

  # Sizing
  api_cpu       = var.api_cpu
  api_memory    = var.api_memory
  worker_cpu    = var.worker_cpu
  worker_memory = var.worker_memory

  # Counts
  api_desired_count    = var.api_desired_count
  worker_desired_count = var.worker_desired_count

  # Spot + autoscaling
  use_fargate_spot   = var.use_fargate_spot
  enable_autoscaling = var.enable_autoscaling

  # Secrets injection — task-def-secrets-block, läses av execution-rollen vid task-startup.
  # Redis-CS komponeras i modules/redis (host:port,password=...,ssl=True,abortConnect=False)
  # och injiceras som single secret ConnectionStrings__Redis. Matchar
  # Infrastructure/DependencyInjection.cs:90+120 GetConnectionString("Redis")-pattern.
  api_secrets = {
    "ConnectionStrings__Postgres" = aws_secretsmanager_secret.db_app_connection.arn
    "ConnectionStrings__Redis"    = module.redis.connection_string_secret_arn
  }

  # Worker använder INTE Redis (verifierat: Infrastructure/DependencyInjection.cs:90 läser
  # Redis bara i AddIdentityAndSessions som är HTTP-only; Worker laddar inte denna).
  # Bara Postgres + HangfireStorage krävs.
  worker_secrets = {
    "ConnectionStrings__HangfireStorage" = aws_secretsmanager_secret.db_hangfire_connection.arn
    "ConnectionStrings__Postgres"        = aws_secretsmanager_secret.db_app_connection.arn
  }

  # Klartext env-vars (icke-känsligt). Alb__HttpsEnabled gate:ar
  # app.UseHttpsRedirection() — se Api/Program.cs (förhindrar redirect-loop
  # bakom HTTP-only-ALB per ADR 0026 + sec-auditor Sec-Major-2 STEG 13b).
  #
  # AdminBootstrap__InitialAdminEmail — IdempotentAdminRoleSeeder tilldelar
  # Admin-rollen till user med matchande email vid host-startup (senior-cto-
  # advisor 2026-05-11 B1 — IaC over manual psql-script). Email är inte
  # känsligt; lösenord sätts vid registrering av Klas själv. Värdet
  # synkroniserades manuellt till task-def rev 11 2026-05-13 inför F2-P8b
  # admin-trigger-smoke-test och persisteras nu via Terraform.
  api_environment = {
    "ASPNETCORE_ENVIRONMENT"             = "Production"
    "ASPNETCORE_URLS"                    = "http://+:8080"
    "ForwardedHeaders__KnownNetworks__0" = var.vpc_cidr
    "Alb__HttpsEnabled"                  = tostring(var.alb_https_enabled)
    "AdminBootstrap__InitialAdminEmail"  = var.initial_admin_email
  }

  # Worker har inga Redis-deps; Hangfire + Postgres räcker. DOTNET_ENVIRONMENT
  # (inte ASPNETCORE_ENVIRONMENT) eftersom Worker använder Generic Host
  # (Host.CreateApplicationBuilder).
  worker_environment = {
    "DOTNET_ENVIRONMENT" = "Production"
  }

  # ---------------------------------------------------------------------------
  # Migrate one-shot task-def (STEG 14b). Skapas bara om migrate_image_uri
  # sätts i tfvars (default tom = task-def skapas inte). Build + push av
  # image-flow:
  #   docker build -f src/JobbPilot.Migrate/Dockerfile -t ${ECR}/jobbpilot-dev-migrate:<sha> .
  #   docker push ${ECR}/jobbpilot-dev-migrate:<sha>
  #   tfvars-edit: migrate_image_tag = "<sha>"
  # Run-task: aws ecs run-task --task-definition jobbpilot-dev-migrate
  # ---------------------------------------------------------------------------
  migrate_image_uri      = var.migrate_image_tag != "" ? "${module.ecr.repository_urls["migrate"]}:${var.migrate_image_tag}" : ""
  task_migrate_role_arn  = module.iam_ecs.task_migrate_role_arn
  migrate_log_group_name = module.cloudwatch_logs.log_group_names["migrate"]

  migrate_environment = {
    "AWS_REGION"                       = var.aws_region
    "MIGRATE_DB_HOST"                  = module.rds.address
    "MIGRATE_DB_PORT"                  = tostring(module.rds.port)
    "MIGRATE_DB_NAME"                  = "jobbpilot"
    "MIGRATE_MASTER_SECRET_ARN"        = module.rds.master_user_secret_arn
    "MIGRATE_APP_CONN_SECRET_ARN"      = aws_secretsmanager_secret.db_app_connection.arn
    "MIGRATE_HANGFIRE_CONN_SECRET_ARN" = aws_secretsmanager_secret.db_hangfire_connection.arn
  }

  tags = var.common_tags
}

# ---------------------------------------------------------------------------
# STEG 13c — DNS + TLS (ADR 0026 trigger 1 / TD-30)
#
# Hosted zone bor i prod/baseline (delad resurs). Dev-stack lookar upp via
# data-source. ACM-cert utfärdas i eu-north-1 (samma region som ALB) med
# DNS-validering via Route53-CNAME. ALIAS-record dev.jobbpilot.se → ALB.
#
# Pre-apply-checklista (dev-stack):
#   1. prod/baseline-stack appliad (zone existerar)
#   2. Registrar pekar NS-records på AWS-NS (output `route53_name_servers`)
#   3. Verifiera global propagering INNAN dev-apply:
#        dig NS jobbpilot.se +short
#        → ska returnera 4 ns-*.awsdns-*.* (inte registrar-default-NS)
#      Om annorlunda → vänta 30-60 min till och re-kontrollera. Apply utan
#      propagerad NS → ACM-validation hänger till timeout (60 min, höjt
#      från default 45 i modules/acm/main.tf).
#
# Apply-flöde:
#   1. terraform apply (denna stack) — skapar ACM-cert + validation +
#      A-ALIAS-record. Validering 5-30 min normalt.
#   2. När `acm_dev.certificate_arn` finns: edit terraform.tfvars med
#      alb_https_enabled=true + alb_acm_certificate_arn=<output>
#   3. terraform apply igen — flippar HTTP-listenern till HTTPS-redirect
#   4. Smoke-test https://dev.jobbpilot.se/api/ready
#   5. Skapa ADR 0027 supersession av ADR 0026
# ---------------------------------------------------------------------------

data "aws_route53_zone" "apex" {
  name         = var.apex_domain_name
  private_zone = false
}

module "acm_dev" {
  source = "../../modules/acm"

  domain_name     = "${var.dev_subdomain}.${var.apex_domain_name}"
  route53_zone_id = data.aws_route53_zone.apex.zone_id

  tags = var.common_tags
}

# A-ALIAS-record dev.jobbpilot.se → ALB. ALIAS = AWS-specifik (gratis,
# automatic-update om ALB byter IP, healthcheck-aware).
resource "aws_route53_record" "dev_alb_alias" {
  zone_id = data.aws_route53_zone.apex.zone_id
  name    = "${var.dev_subdomain}.${var.apex_domain_name}"
  type    = "A"

  alias {
    name                   = module.alb.alb_dns_name
    zone_id                = module.alb.alb_zone_id
    evaluate_target_health = true
  }
}
