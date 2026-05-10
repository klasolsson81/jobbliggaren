output "cluster_arn" {
  value = aws_ecs_cluster.this.arn
}

output "cluster_name" {
  value = aws_ecs_cluster.this.name
}

output "api_service_name" {
  value = aws_ecs_service.api.name
}

output "worker_service_name" {
  value = aws_ecs_service.worker.name
}

output "api_task_definition_arn" {
  value = aws_ecs_task_definition.api.arn
}

output "worker_task_definition_arn" {
  value = aws_ecs_task_definition.worker.arn
}

output "migrate_task_definition_arn" {
  description = "Task-definition-ARN för Migrate one-shot. Tom sträng om migrate-task inte skapats."
  value       = length(aws_ecs_task_definition.migrate) > 0 ? aws_ecs_task_definition.migrate[0].arn : ""
}

output "migrate_task_definition_family" {
  description = "Task-definition-family för Migrate (`<prefix>-migrate`). Används i `aws ecs run-task --task-definition <family>`."
  value       = length(aws_ecs_task_definition.migrate) > 0 ? aws_ecs_task_definition.migrate[0].family : ""
}
