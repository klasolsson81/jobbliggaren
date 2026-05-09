output "log_group_names" {
  description = "Map av service-suffix till LogGroup-namn (för ECS-task-def `awslogs-group`)."
  value       = { for name, lg in aws_cloudwatch_log_group.this : name => lg.name }
}

output "log_group_arns" {
  description = "Map av service-suffix till LogGroup-ARN (för IAM-policy-references)."
  value       = { for name, lg in aws_cloudwatch_log_group.this : name => lg.arn }
}
