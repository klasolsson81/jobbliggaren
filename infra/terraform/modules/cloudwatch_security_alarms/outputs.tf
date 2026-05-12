output "sns_topic_arn" {
  description = "ARN för secops-anomaly SNS-topic. Återanvänds av framtida security-alarms i samma env."
  value       = aws_sns_topic.secops_anomaly.arn
}

output "alarm_arn" {
  description = "ARN för failed-access-anomaly CloudWatch-alarm. Användbar för cross-stack-referens."
  value       = aws_cloudwatch_metric_alarm.failed_access_anomaly.arn
}

output "log_pipeline_health_alarm_arn" {
  description = "ARN för log-pipeline-health-alarmet (bevakar att api-log-gruppen tar emot events). Säkerhets-komplement till failed-access-alarmet."
  value       = aws_cloudwatch_metric_alarm.log_pipeline_health.arn
}

output "metric_filter_name" {
  description = "Namn på CloudWatch metric filter. Användbar för CloudWatch Insights-queries vid drill-down."
  value       = aws_cloudwatch_log_metric_filter.failed_access.name
}
