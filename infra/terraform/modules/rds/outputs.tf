output "instance_id" {
  value = aws_db_instance.this.id
}

output "endpoint" {
  description = "RDS endpoint (host:port)."
  value       = aws_db_instance.this.endpoint
}

output "address" {
  description = "RDS hostname utan port."
  value       = aws_db_instance.this.address
}

output "port" {
  value = aws_db_instance.this.port
}

output "db_name" {
  value = aws_db_instance.this.db_name
}

output "master_username" {
  value = aws_db_instance.this.username
}

output "master_user_secret_arn" {
  description = "ARN för AWS-managed Secrets Manager-secret med master-password (auto-roterad)."
  value       = aws_db_instance.this.master_user_secret[0].secret_arn
}

output "master_user_secret_kms_key_id" {
  value = aws_db_instance.this.master_user_secret[0].kms_key_id
}
