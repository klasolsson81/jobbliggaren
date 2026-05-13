# ---------------------------------------------------------------------------
# CloudWatch ops-alarms — ADR 0036 (cohesion-pendant till ADR 0031/TD-68).
#
# Etablerar metric filter + SNS-alarm för:
#   - event_name=job_event_failure (SyncPlatsbankenStreamJob.LogEventFailed,
#     EventId 5303, LogLevel.Warning) — JobTech-sync per-event-failures.
#     Alarm-tröskel ≥3 per 30-min-fönster matchar BUILD.md §14.4
#     "JobTech sync misslyckas 3 gånger i rad"-spec aggregerat.
#   - event_name=audit_write_failure (SystemEventAuditor.LogAuditFailure,
#     EventId 5602, LogLevel.Critical) — SystemEventAuditor retry-exhaustion.
#     Zero-tolerance: ≥1 per 5-min-fönster (GDPR Art. 30 record-of-processing).
#     Stänger ADR 0035 §6 alarm-wire-gap.
#
# Speglar ADR 0031:s event_name=-konvention från FailedAccessLogger
# (CCP — Martin 2017 kap. 13: konventionen ska ändras på ett ställe, inte
# parallella patterns som divergerar). Worker LoggerMessage-templates
# producerar matchande strängar — modulen + Worker-kod-ändring levereras
# i samma cohesion-bundle.
#
# Worker-log-pipeline-health-alarm ingår för cohesion-paritet med
# cloudwatch_security_alarms.log_pipeline_health (security-auditor Minor-3
# 2026-05-12): treat_missing_data=notBreaching på failure-alarms blir
# bevisbart icke-funktionell utan parallell health-check.
#
# Future Fas-3+-trigger för upgrade till canary/synthetic-tests:
# - Volume > 10k events/dag på event_name-metricerna
# - ECS-task-restart-frekvens > 1/vecka
# - 3:e separat ops-alarm införs (då motiverar canary parametrisering)
# ---------------------------------------------------------------------------

data "aws_caller_identity" "current" {}

# ---------------------------------------------------------------------------
# SNS topic för ops-anomaly-alerts.
#
# Separat från secops-anomaly per ISP (Martin 2017 kap. 10): ops-on-call
# och security-on-call är distinkta triage-flöden även om mottagar-adress
# råkar vara samma idag. Aliasing-antagande låser inte konfigurationen.
#
# Suffix `-ops-anomaly` speglar `-secops-anomaly` för ubiquitous language
# (Evans 2003 kap. 2): operatör som lärt sig <prefix>-<category>-anomaly-
# mönster i secops applicerar det automatiskt på ops.
#
# KMS-encryption + restrictive topic-policy speglar TD-68/ADR 0031-mönster
# (Saltzer/Schroeder 1975 fail-safe defaults — alarm-suppression-skydd är
# agnostiskt mot alarm-syfte).
# ---------------------------------------------------------------------------

resource "aws_sns_topic" "ops_anomaly" {
  name              = "${var.name_prefix}-ops-anomaly"
  kms_master_key_id = var.kms_key_id

  tags = merge(var.tags, {
    Purpose = "ops-anomaly-alerts"
  })
}

resource "aws_sns_topic_policy" "ops_anomaly" {
  arn = aws_sns_topic.ops_anomaly.arn

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
        Resource = aws_sns_topic.ops_anomaly.arn
        Condition = {
          StringEquals = {
            "AWS:SourceAccount" = data.aws_caller_identity.current.account_id
          }
          # Defense-in-depth: lås till alarms med name_prefix-pattern så
          # framtida felkonfigurerad alarm i samma konto inte oavsiktligt
          # kan publicera till ops-topic (alarm-suppression-skydd).
          ArnLike = {
            "AWS:SourceArn" = "arn:aws:cloudwatch:*:${data.aws_caller_identity.current.account_id}:alarm:${var.name_prefix}-*"
          }
        }
      }
    ]
  })
}

resource "aws_sns_topic_subscription" "ops_email" {
  count = var.alert_email != "" ? 1 : 0

  topic_arn = aws_sns_topic.ops_anomaly.arn
  protocol  = "email"
  endpoint  = var.alert_email
}

# ---------------------------------------------------------------------------
# Metric filter — JobTech sync per-event-failures (worker log group).
#
# Pattern matchar event_name=job_event_failure (LoggerMessage-template i
# SyncPlatsbankenStreamJob.LogEventFailed, EventId 5303). Substring-match
# följer CWL filter-syntax: unquoted term = substring anywhere in message.
# ---------------------------------------------------------------------------

resource "aws_cloudwatch_log_metric_filter" "jobtech_failure" {
  name           = "${var.name_prefix}-jobtech-event-failures"
  log_group_name = var.worker_log_group_name

  pattern = "event_name=job_event_failure"

  metric_transformation {
    name      = "JobTechSyncFailures"
    namespace = "JobbPilot/Operations"
    value     = "1"
    unit      = "Count"
  }
}

# ---------------------------------------------------------------------------
# Metric filter — SystemEventAuditor audit_write_failure (worker log group).
#
# SystemEventAuditor triggas i Worker-jobben (3 Hangfire-recurring + admin-
# endpoint via Api). Två metric filters speglar samma aggregat-metric över
# två log-groups.
# ---------------------------------------------------------------------------

resource "aws_cloudwatch_log_metric_filter" "auditor_failure_worker" {
  name           = "${var.name_prefix}-auditor-failures-worker"
  log_group_name = var.worker_log_group_name

  pattern = "event_name=audit_write_failure"

  metric_transformation {
    name      = "SystemEventAuditorFailures"
    namespace = "JobbPilot/Operations"
    value     = "1"
    unit      = "Count"
  }
}

resource "aws_cloudwatch_log_metric_filter" "auditor_failure_api" {
  name           = "${var.name_prefix}-auditor-failures-api"
  log_group_name = var.api_log_group_name

  pattern = "event_name=audit_write_failure"

  metric_transformation {
    name      = "SystemEventAuditorFailures"
    namespace = "JobbPilot/Operations"
    value     = "1"
    unit      = "Count"
  }
}

# ---------------------------------------------------------------------------
# Alarm — JobTech sync per-event-failures aggregat.
#
# Threshold ≥3 över 30-min-fönster (single-period, evaluation_periods=1)
# per AWS Well-Architected REL06-BP02 (aggregate > consecutive-evaluation
# för signal-tydlighet). Matchar BUILD.md §14.4 "JobTech sync misslyckas
# 3 gånger i rad" semantiskt — tre eller fler failures inom valfritt 30-min-
# fönster triggar alarm.
#
# treat_missing_data=notBreaching: missing data = normal drift (inga
# failures = inga events emit:as). Health-alarm nedan täcker invarianten
# "log-pipeline når CloudWatch alls" så missing != tystdöd.
# ---------------------------------------------------------------------------

resource "aws_cloudwatch_metric_alarm" "jobtech_failure" {
  alarm_name        = "${var.name_prefix}-jobtech-sync-failures"
  alarm_description = "Aggregerad threshold för event_name=job_event_failure (BUILD.md §14.4 'JobTech sync misslyckas 3 gånger i rad'). Vid trigger: kör CloudWatch Insights-query mot worker-log-gruppen filtrerad på external_id för per-event-drill-down + verifiera JobTech-API-health via curl jobstream.api.jobtechdev.se."

  metric_name = aws_cloudwatch_log_metric_filter.jobtech_failure.metric_transformation[0].name
  namespace   = aws_cloudwatch_log_metric_filter.jobtech_failure.metric_transformation[0].namespace
  statistic   = "Sum"

  comparison_operator = "GreaterThanOrEqualToThreshold"
  threshold           = var.jobtech_failure_threshold
  period              = var.jobtech_failure_period_seconds
  evaluation_periods  = 1

  treat_missing_data = "notBreaching"

  alarm_actions = [aws_sns_topic.ops_anomaly.arn]
  ok_actions    = [aws_sns_topic.ops_anomaly.arn]

  tags = merge(var.tags, {
    Purpose = "jobtech-sync-anomaly"
  })
}

# ---------------------------------------------------------------------------
# Alarm — SystemEventAuditor audit_write_failure.
#
# Zero-tolerance: threshold > 0 över 5-min-fönster. Audit-wire är GDPR
# Art. 30 record-of-processing-foundation; varje failure är signal.
# Stänger ADR 0035 §6 alarm-wire-gap explicit (best-effort-semantiken
# förutsätter CloudWatch-alarm vid retry-exhaustion).
#
# Aggregat över worker + api log-groups via shared metric-namn (båda
# metric filters skriver till samma SystemEventAuditorFailures-metric).
# ---------------------------------------------------------------------------

resource "aws_cloudwatch_metric_alarm" "auditor_failure" {
  alarm_name        = "${var.name_prefix}-auditor-write-failures"
  alarm_description = "Zero-tolerance för event_name=audit_write_failure (ADR 0035 §6 + GDPR Art. 30). SystemEventAuditor retry-exhaustion-signal. Vid trigger: kör CloudWatch Insights mot worker+api-log-groups filtrerad på event_type+aggregate_id för per-failure-drill-down + verifiera RDS-health (audit_log-tabell-skrivning är persistens-beroende)."

  metric_name = aws_cloudwatch_log_metric_filter.auditor_failure_worker.metric_transformation[0].name
  namespace   = aws_cloudwatch_log_metric_filter.auditor_failure_worker.metric_transformation[0].namespace
  statistic   = "Sum"

  comparison_operator = "GreaterThanThreshold"
  threshold           = var.auditor_failure_threshold
  period              = var.auditor_failure_period_seconds
  evaluation_periods  = 1

  treat_missing_data = "notBreaching"

  alarm_actions = [aws_sns_topic.ops_anomaly.arn]
  ok_actions    = [aws_sns_topic.ops_anomaly.arn]

  tags = merge(var.tags, {
    Purpose = "auditor-write-anomaly"
  })
}

# ---------------------------------------------------------------------------
# Health-alarm — worker-log-pipeline-health.
#
# Speglar cloudwatch_security_alarms.log_pipeline_health-pattern för api-
# log-gruppen. Utan denna invariant blir treat_missing_data=notBreaching
# på failure-alarms ovan bevisbart icke-funktionell (om FluentBit/ECS/IAM-
# fel tar ned log-pipelinen rapporteras 0 events = OK felaktigt).
#
# Säkerhet/integritets-vinst per security-auditor Minor-3 2026-05-12 +
# LSP (Martin 2017 kap. 9): ops-alarms och secops-alarms är subtypes av
# "log-pipeline-driven CloudWatch alarms" och ska uppfylla samma kontrakt.
# ---------------------------------------------------------------------------

resource "aws_cloudwatch_metric_alarm" "worker_log_pipeline_health" {
  alarm_name        = "${var.name_prefix}-worker-log-pipeline-health"
  alarm_description = "Bevakar att worker-log-gruppen tar emot events. 0 events över 15 min = log-pipeline bruten (FluentBit/ECS/IAM-fel). Kompletterar jobtech-sync-failures + auditor-write-failures — utan denna gör 'tyst pipeline' att ops-anomaly-detection blir bevisbart icke-funktionell."

  metric_name = "IncomingLogEvents"
  namespace   = "AWS/Logs"
  statistic   = "Sum"

  dimensions = {
    LogGroupName = var.worker_log_group_name
  }

  comparison_operator = "LessThanOrEqualToThreshold"
  threshold           = 0
  period              = var.log_pipeline_health_period_seconds
  evaluation_periods  = 1

  treat_missing_data = "breaching"

  alarm_actions = [aws_sns_topic.ops_anomaly.arn]
  ok_actions    = [aws_sns_topic.ops_anomaly.arn]

  tags = merge(var.tags, {
    Purpose = "worker-log-pipeline-health"
  })
}
