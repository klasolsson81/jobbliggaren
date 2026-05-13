output "sns_topic_arn" {
  description = "ARN för ops-anomaly SNS-topic. Återanvänds av framtida ops-alarms i samma env (Hangfire-job-failures, JobTech-API-circuit-breaker, etc.)."
  value       = aws_sns_topic.ops_anomaly.arn
}

output "jobtech_alarm_arn" {
  description = "ARN för jobtech-sync-failures CloudWatch-alarm. Användbar för cross-stack-referens vid composite-alarms eller dashboard-konfiguration."
  value       = aws_cloudwatch_metric_alarm.jobtech_failure.arn
}

output "auditor_alarm_arn" {
  description = "ARN för auditor-write-failures CloudWatch-alarm. Användbar för cross-stack-referens."
  value       = aws_cloudwatch_metric_alarm.auditor_failure.arn
}

output "worker_log_pipeline_health_alarm_arn" {
  description = "ARN för worker-log-pipeline-health-alarmet. Ops-komplement (kontrakt-paritet med cloudwatch_security_alarms.log_pipeline_health-alarmet för api-loggar)."
  value       = aws_cloudwatch_metric_alarm.worker_log_pipeline_health.arn
}

output "metric_filter_names" {
  description = "Namn på CloudWatch metric filters. Användbara för CloudWatch Insights-queries vid drill-down."
  value = {
    jobtech_failure        = aws_cloudwatch_log_metric_filter.jobtech_failure.name
    auditor_failure_worker = aws_cloudwatch_log_metric_filter.auditor_failure_worker.name
    auditor_failure_api    = aws_cloudwatch_log_metric_filter.auditor_failure_api.name
  }
}
