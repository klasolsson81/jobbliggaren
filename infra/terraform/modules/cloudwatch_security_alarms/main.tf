# ---------------------------------------------------------------------------
# CloudWatch security-alarms — TD-68 / ADR 0031.
#
# Etablerar metric filter + SNS-alarm för failed_access_attempt-events från
# IFailedAccessLogger i Application-skiktet. Detekterar BOLA-enumeration-
# attack (OWASP API1:2023) i prod-trafik via aggregerad threshold-alarm.
#
# Per-user-detection (granular drill-down) görs via CloudWatch Insights-query
# vid alarm-trigger — total-count räcker som signal-tröskel i Fas 1.
# Per-user-metric-dimensions defereras tills användarbasen växer (kostnaden
# av en metric per user blir mätbar först då).
# ---------------------------------------------------------------------------

# Metric filter på api log group — extraherar failed_access_attempt-events
# producerade av FailedAccessLogger (EventId 4001, Warning-level).
# Filter-pattern matchar strukturerade fält i log-line.
resource "aws_cloudwatch_log_metric_filter" "failed_access" {
  name           = "${var.name_prefix}-failed-access-attempts"
  log_group_name = var.log_group_name

  # Matchar log-rader som innehåller "event_name=failed_access_attempt".
  # LoggerMessage-source-gen producerar message i textform (inte JSON), därav
  # substring-pattern istället för $.event_name JSON-path. Pattern är UNQUOTED
  # för substring-match — quoted ("...") gör exact-token-match som kan miss:a
  # om termen sitter inom annan text i message. AWS CWL filter-syntax:
  # unquoted term → substring anywhere in log-event-message.
  #
  # Verifiering före apply: `aws logs test-metric-filter --filter-pattern
  # 'event_name=failed_access_attempt' --log-event-messages '<sample>'`.
  pattern = "event_name=failed_access_attempt"

  metric_transformation {
    name      = "FailedAccessAttempts"
    namespace = "JobbPilot/Security"
    value     = "1"
    unit      = "Count"
  }
}

# SNS topic för security/anomaly-alerts. Dedikerad topic så security-alarms
# inte blandas med ops-alarms (cost/perf). Krypterad med master KMS-nyckeln
# per BUILD.md §14 (in-transit + at-rest encryption).
resource "aws_sns_topic" "secops_anomaly" {
  name              = "${var.name_prefix}-secops-anomaly"
  kms_master_key_id = var.kms_key_id

  tags = merge(var.tags, {
    Purpose = "secops-anomaly-alerts"
  })
}

# Least-privilege topic-policy: endast CloudWatch alarms från samma konto får
# publicera. Default SNS-policy tillåter alla principals i samma konto vilket
# öppnar för alarm-suppression-attack (intern app med SNS-permissions injicerar
# falska OK-events). Defense-in-depth per security-auditor 2026-05-12.
data "aws_caller_identity" "current" {}

resource "aws_sns_topic_policy" "secops_anomaly" {
  arn = aws_sns_topic.secops_anomaly.arn

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "AllowCloudWatchAlarmsToPublish"
        Effect = "Allow"
        Principal = {
          Service = "cloudwatch.amazonaws.com"
        }
        Action   = "SNS:Publish"
        Resource = aws_sns_topic.secops_anomaly.arn
        Condition = {
          StringEquals = {
            "AWS:SourceAccount" = data.aws_caller_identity.current.account_id
          }
          # Defense-in-depth: lås till alarms med name_prefix-pattern så
          # framtida felkonfigurerad alarm i samma konto inte oavsiktligt
          # kan publicera till secops-topic (alarm-suppression-skydd).
          ArnLike = {
            "AWS:SourceArn" = "arn:aws:cloudwatch:*:${data.aws_caller_identity.current.account_id}:alarm:${var.name_prefix}-*"
          }
        }
      }
    ]
  })
}

# Email-subscription (optional). Om alert_email är null/tom: ingen subscription
# skapas — Klas konfigurerar manuellt via console eller separat
# tf-changeset. Subscription-confirmation kräver att email-adressen svarar
# på AWS opt-in-mail, så detta är ett semi-manuellt steg oavsett.
resource "aws_sns_topic_subscription" "secops_email" {
  count = var.alert_email != "" ? 1 : 0

  topic_arn = aws_sns_topic.secops_anomaly.arn
  protocol  = "email"
  endpoint  = var.alert_email
}

# Health-alarm: api-log-pipeline producerar events. Om `IncomingLogEvents` blir
# 0 över ett längre period betyder det att log-pipelinen är bruten (FluentBit
# kraschade, ECS-task döende, IAM-policy-fel etc.) — då säger
# treat_missing_data=notBreaching på failed-access-alarmet "OK" felaktigt.
# Detta är en separat invariant ("loggar når CloudWatch alls") som security-
# auditor 2026-05-12 Minor 3 flaggade.
resource "aws_cloudwatch_metric_alarm" "log_pipeline_health" {
  alarm_name        = "${var.name_prefix}-api-log-pipeline-health"
  alarm_description = "Bevakar att api-log-gruppen tar emot events. 0 events över 15 min = log-pipeline bruten (FluentBit/ECS/IAM-fel). Kompletterar failed-access-anomaly-alarmet — utan denna gör 'tyst pipeline' att anomaly-detection blir bevisbart icke-funktionell."

  metric_name = "IncomingLogEvents"
  namespace   = "AWS/Logs"
  statistic   = "Sum"

  dimensions = {
    LogGroupName = var.log_group_name
  }

  comparison_operator = "LessThanOrEqualToThreshold"
  threshold           = 0
  period              = var.log_pipeline_health_period_seconds
  evaluation_periods  = 1

  # Missing data räknas som breaching — om CloudWatch INTE rapporterar
  # IncomingLogEvents är pipelinen sannolikt nere (ej noll-data, utan
  # ingen-data-alls). Korrekt val för health-invariant.
  treat_missing_data = "breaching"

  alarm_actions = [aws_sns_topic.secops_anomaly.arn]
  ok_actions    = [aws_sns_topic.secops_anomaly.arn]

  tags = merge(var.tags, {
    Purpose = "log-pipeline-health"
  })
}

# Alarm: total count av failed-access-events över threshold per period.
# Per-user-detection defereras (se modul-docstring). Threshold + period
# konfigurerbart per env — dev: höga (utveckling triggar via tester),
# prod: lägre (faktisk attack-detection).
resource "aws_cloudwatch_metric_alarm" "failed_access_anomaly" {
  alarm_name        = "${var.name_prefix}-failed-access-anomaly"
  alarm_description = "Aggregerad threshold för failed_access_attempt-events (TD-68 / ADR 0031). Vid trigger: kör CloudWatch Insights-query mot api-log-gruppen filtrerad på requesting_user_id för per-user-drill-down."

  metric_name = aws_cloudwatch_log_metric_filter.failed_access.metric_transformation[0].name
  namespace   = aws_cloudwatch_log_metric_filter.failed_access.metric_transformation[0].namespace
  statistic   = "Sum"

  comparison_operator = "GreaterThanThreshold"
  threshold           = var.alarm_threshold
  period              = var.alarm_period_seconds
  evaluation_periods  = 1

  # Treat missing data som "ok" — när inga events kommer in (normal-flöde)
  # ska alarm vara grön. Missing != bredd-attack.
  treat_missing_data = "notBreaching"

  alarm_actions = [aws_sns_topic.secops_anomaly.arn]
  ok_actions    = [aws_sns_topic.secops_anomaly.arn]

  tags = merge(var.tags, {
    Purpose = "secops-anomaly-alarm"
  })
}
