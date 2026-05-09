# ---------------------------------------------------------------------------
# DB Subnet Group — RDS placeras i privata subnets över minst 2 AZs.
# ---------------------------------------------------------------------------

resource "aws_db_subnet_group" "this" {
  name       = "${var.name_prefix}-rds"
  subnet_ids = var.subnet_ids

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-rds-subnet-group"
  })
}

# ---------------------------------------------------------------------------
# Parameter group — PG 18.x family. Hold defensive defaults.
# Sec-Major-1 + Sec-Minor-6 (STEG 13a security-audit):
#   - log_statement = "none" så DDL-passwords (CREATE/ALTER ROLE) inte hamnar
#     i CloudWatch Logs. Terraform-state + Hangfire Install.sql ger audit-trail
#     för schema-ändringar (DDL-spårning via Postgres-logg ger marginellt värde).
#   - log_parameter_max_length = 0 trunkerar bind-värden i slow-query-log så
#     PII (email, namn) inte exponeras via log_min_duration_statement.
# ---------------------------------------------------------------------------

resource "aws_db_parameter_group" "this" {
  name        = "${var.name_prefix}-rds-pg18"
  family      = "postgres18"
  description = "JobbPilot Postgres 18 parameter group."

  # Inga DDL-/all-statements i log — passwords hamnar annars i CloudWatch.
  parameter {
    name  = "log_statement"
    value = "none"
  }

  # Logga queries > 1s för perf-debug — query-text loggas men bind-värden
  # trunkeras till 0 chars (se nedan) så PII inte exponeras.
  parameter {
    name  = "log_min_duration_statement"
    value = "1000"
  }

  # Trunkera bind-värden i slow-query-log (Postgres 13+).
  # Standard-värdet -1 = oändligt = PII-läckage via WHERE-villkor.
  parameter {
    name  = "log_parameter_max_length"
    value = "0"
  }

  parameter {
    name  = "log_parameter_max_length_on_error"
    value = "0"
  }

  # SSL only — prod-tvång.
  parameter {
    name  = "rds.force_ssl"
    value = "1"
  }

  tags = var.tags
}

# ---------------------------------------------------------------------------
# CloudWatch LogGroups för RDS-export.
# Sec-Major-1: utan explicit deklaration skapas de implicit av AWS med
# default-retention "Never expire" — bryter ADR 0024 D7 (30d app-logg-retention)
# och skapar permanent retention av PII/secret-spår om något läcker via DDL/slow-query.
# ---------------------------------------------------------------------------

resource "aws_cloudwatch_log_group" "rds_postgresql" {
  name              = "/aws/rds/instance/${var.name_prefix}-rds/postgresql"
  retention_in_days = 30
  kms_key_id        = var.kms_key_id

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-rds-postgresql-log"
    Purpose = "rds-export"
  })
}

resource "aws_cloudwatch_log_group" "rds_upgrade" {
  name              = "/aws/rds/instance/${var.name_prefix}-rds/upgrade"
  retention_in_days = 30
  kms_key_id        = var.kms_key_id

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-rds-upgrade-log"
    Purpose = "rds-export"
  })
}

# ---------------------------------------------------------------------------
# RDS-instans — Multi-AZ, encrypted, Performance Insights.
# Master-password hanteras av AWS Secrets Manager (manage_master_user_password)
# med automatisk rotation (default 7d). Hämtas av appen via SecretsManager-port.
# ---------------------------------------------------------------------------

resource "aws_db_instance" "this" {
  identifier = "${var.name_prefix}-rds"

  engine                = "postgres"
  engine_version        = var.engine_version
  instance_class        = var.instance_class
  allocated_storage     = var.allocated_storage
  max_allocated_storage = var.max_allocated_storage
  storage_type          = "gp3"
  storage_encrypted     = true
  kms_key_id            = var.kms_key_id

  db_name  = var.db_name
  username = var.master_username
  port     = 5432

  # AWS-managed Secrets Manager för master-password + automatisk rotation.
  # Master-secret krypteras med master-key (app-secrets-domän per BUILD.md §8.4).
  # BYOK-key är reserverad för envelope-encryption av användar-supplied API-keys.
  manage_master_user_password   = true
  master_user_secret_kms_key_id = var.kms_key_id

  multi_az               = var.multi_az
  publicly_accessible    = false
  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = var.vpc_security_group_ids

  parameter_group_name = aws_db_parameter_group.this.name

  backup_retention_period = var.backup_retention_days
  backup_window           = var.backup_window
  maintenance_window      = var.maintenance_window
  copy_tags_to_snapshot   = true

  performance_insights_enabled    = var.performance_insights_enabled
  performance_insights_kms_key_id = var.performance_insights_enabled ? var.kms_key_id : null
  monitoring_interval             = 60
  monitoring_role_arn             = aws_iam_role.rds_monitoring.arn

  enabled_cloudwatch_logs_exports = ["postgresql", "upgrade"]

  auto_minor_version_upgrade = true
  apply_immediately          = false

  deletion_protection       = var.deletion_protection
  delete_automated_backups  = false
  skip_final_snapshot       = false
  final_snapshot_identifier = "${var.name_prefix}-rds-final-${formatdate("YYYYMMDDhhmmss", timestamp())}"

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-rds"
  })

  lifecycle {
    ignore_changes = [
      # Tidsstämpel i final_snapshot_identifier rör alltid plan annars.
      final_snapshot_identifier,
    ]
  }
}

# ---------------------------------------------------------------------------
# IAM-roll för Enhanced Monitoring.
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "rds_monitoring_assume" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["monitoring.rds.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "rds_monitoring" {
  name               = "${var.name_prefix}-rds-monitoring"
  assume_role_policy = data.aws_iam_policy_document.rds_monitoring_assume.json
  tags               = var.tags
}

resource "aws_iam_role_policy_attachment" "rds_monitoring" {
  role       = aws_iam_role.rds_monitoring.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonRDSEnhancedMonitoringRole"
}
