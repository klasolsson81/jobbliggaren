output "execution_role_arn" {
  description = "ECS task execution-role-ARN. Används i task-def `executionRoleArn`."
  value       = aws_iam_role.execution.arn
}

output "task_api_role_arn" {
  description = "Task-role-ARN för Api-runtime. Används i Api-task-def `taskRoleArn`."
  value       = aws_iam_role.task_api.arn
}

output "task_worker_role_arn" {
  description = "Task-role-ARN för Worker-runtime. Används i Worker-task-def `taskRoleArn`."
  value       = aws_iam_role.task_worker.arn
}

output "task_migrate_role_arn" {
  description = "Task-role-ARN för Migrate one-shot (STEG 14b). Tom sträng om migrate-rollen inte skapats."
  value       = length(aws_iam_role.task_migrate) > 0 ? aws_iam_role.task_migrate[0].arn : ""
}
