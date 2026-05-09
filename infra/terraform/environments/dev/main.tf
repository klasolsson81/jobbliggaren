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

  name_prefix          = var.name_prefix
  vpc_cidr             = var.vpc_cidr
  az_count             = 3
  single_nat_gateway   = true # cost-optimized för Fas 0; uppgraderas till multi-NAT i staging/prod
  enable_vpc_endpoints = true

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

  engine_version  = var.rds_engine_version
  instance_class  = var.rds_instance_class
  multi_az        = true
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
  num_cache_clusters     = 2

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
