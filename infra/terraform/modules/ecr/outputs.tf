output "repository_urls" {
  description = "Map av service-namn till ECR-repo-URL (för docker push)."
  value       = { for name, repo in aws_ecr_repository.this : name => repo.repository_url }
}

output "repository_arns" {
  description = "Map av service-namn till repo-ARN (för IAM-policy-references)."
  value       = { for name, repo in aws_ecr_repository.this : name => repo.arn }
}

output "repository_names" {
  description = "Map av service-namn till fullt repo-namn."
  value       = { for name, repo in aws_ecr_repository.this : name => repo.name }
}
