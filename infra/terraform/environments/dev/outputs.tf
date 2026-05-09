# ---------------------------------------------------------------------------
# Networking
# ---------------------------------------------------------------------------

output "vpc_id" {
  value = module.network.vpc_id
}

output "vpc_cidr" {
  value = module.network.vpc_cidr
}

output "public_subnet_ids" {
  value = module.network.public_subnet_ids
}

output "private_subnet_ids" {
  value = module.network.private_subnet_ids
}

output "alb_security_group_id" {
  value = module.network.alb_security_group_id
}

output "ecs_security_group_id" {
  value = module.network.ecs_security_group_id
}

# ---------------------------------------------------------------------------
# Databas
# ---------------------------------------------------------------------------

output "rds_endpoint" {
  description = "RDS endpoint host:port."
  value       = module.rds.endpoint
}

output "rds_address" {
  value = module.rds.address
}

output "rds_port" {
  value = module.rds.port
}

output "rds_db_name" {
  value = module.rds.db_name
}

output "rds_master_user_secret_arn" {
  description = "AWS-managed Secrets Manager-secret med master-password (auto-roterad). Används bara för DDL-init."
  value       = module.rds.master_user_secret_arn
  sensitive   = true
}

# ---------------------------------------------------------------------------
# Cache
# ---------------------------------------------------------------------------

output "redis_primary_endpoint_address" {
  value = module.redis.primary_endpoint_address
}

output "redis_reader_endpoint_address" {
  value = module.redis.reader_endpoint_address
}

output "redis_port" {
  value = module.redis.port
}

output "redis_auth_token_secret_arn" {
  description = "Secrets Manager-secret med Redis AUTH-token."
  value       = module.redis.auth_token_secret_arn
  sensitive   = true
}

# ---------------------------------------------------------------------------
# Dev-secrets (placeholder, sätts post-DDL)
# ---------------------------------------------------------------------------

output "db_app_connection_secret_arn" {
  value = aws_secretsmanager_secret.db_app_connection.arn
}

output "db_hangfire_connection_secret_arn" {
  value = aws_secretsmanager_secret.db_hangfire_connection.arn
}
