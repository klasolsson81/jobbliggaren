output "replication_group_id" {
  value = aws_elasticache_replication_group.this.id
}

output "primary_endpoint_address" {
  description = "Primary endpoint för writes."
  value       = aws_elasticache_replication_group.this.primary_endpoint_address
}

output "reader_endpoint_address" {
  description = "Reader endpoint för read-replicas."
  value       = aws_elasticache_replication_group.this.reader_endpoint_address
}

output "port" {
  value = aws_elasticache_replication_group.this.port
}

output "auth_token_secret_arn" {
  description = "ARN för Secrets Manager-secret med AUTH-token."
  value       = aws_secretsmanager_secret.auth_token.arn
}
