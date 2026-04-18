output "secret_arns" {
  description = "Map från secret-namn → ARN. App läser från denna vid DI-setup."
  value       = { for k, s in aws_secretsmanager_secret.placeholder : k => s.arn }
}

output "secret_names" {
  value = [for k in keys(aws_secretsmanager_secret.placeholder) : k]
}
