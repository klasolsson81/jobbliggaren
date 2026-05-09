# ---------------------------------------------------------------------------
# Subnet group — privata subnets över minst 2 AZs.
# ---------------------------------------------------------------------------

resource "aws_elasticache_subnet_group" "this" {
  name       = "${var.name_prefix}-redis"
  subnet_ids = var.subnet_ids
  tags       = var.tags
}

# ---------------------------------------------------------------------------
# Parameter group — Valkey 8 family. Defaults är OK för Fas 0.
# ---------------------------------------------------------------------------

resource "aws_elasticache_parameter_group" "this" {
  name        = "${var.name_prefix}-redis-pg"
  family      = var.parameter_group_family
  description = "JobbPilot ${var.engine} ${var.engine_version} parameter group."

  tags = var.tags
}

# ---------------------------------------------------------------------------
# AUTH-token genereras lokalt + sparas i Secrets Manager för app-konsumtion.
# Krav: minst 16 tecken, max 128 tecken, [a-zA-Z0-9!&#$^<>-]. Random string
# håller sig i [a-zA-Z0-9] för att undvika special-char-korruption i config.
# ---------------------------------------------------------------------------

resource "random_password" "auth_token" {
  length      = 64
  special     = false
  upper       = true
  lower       = true
  numeric     = true
  min_upper   = 8
  min_lower   = 8
  min_numeric = 8
}

resource "aws_secretsmanager_secret" "auth_token" {
  name                    = "${var.name_prefix}/redis/auth-token"
  description             = "ElastiCache AUTH-token för ${var.name_prefix} replication group."
  kms_key_id              = var.kms_key_id
  recovery_window_in_days = 7

  tags = merge(var.tags, {
    Purpose = "elasticache-auth"
  })
}

resource "aws_secretsmanager_secret_version" "auth_token" {
  secret_id     = aws_secretsmanager_secret.auth_token.id
  secret_string = random_password.auth_token.result
}

# ---------------------------------------------------------------------------
# Replication group — multi-AZ, transit + at-rest encryption, AUTH on.
# ---------------------------------------------------------------------------

resource "aws_elasticache_replication_group" "this" {
  replication_group_id = "${var.name_prefix}-redis"
  description          = "JobbPilot ${var.engine} replication group (${var.name_prefix})."

  engine         = var.engine
  engine_version = var.engine_version
  node_type      = var.node_type
  port           = 6379

  num_cache_clusters         = var.num_cache_clusters
  automatic_failover_enabled = var.automatic_failover_enabled
  multi_az_enabled           = var.multi_az_enabled

  subnet_group_name    = aws_elasticache_subnet_group.this.name
  security_group_ids   = var.vpc_security_group_ids
  parameter_group_name = aws_elasticache_parameter_group.this.name

  at_rest_encryption_enabled = true
  transit_encryption_enabled = true
  kms_key_id                 = var.kms_key_id
  auth_token                 = random_password.auth_token.result

  snapshot_retention_limit = var.snapshot_retention_days
  snapshot_window          = "01:00-02:00"
  maintenance_window       = "sun:03:00-sun:04:00"

  apply_immediately          = false
  auto_minor_version_upgrade = true

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-redis"
  })

  lifecycle {
    ignore_changes = [
      # AUTH-token ändras endast via medveten secret-rotation; pin för att
      # undvika oavsiktlig replace via Terraform.
      auth_token,
    ]
  }
}
