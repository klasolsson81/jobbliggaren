variable "name_prefix" {
  description = "Prefix för resurs-namn (t.ex. \"jobbpilot-dev\")."
  type        = string
}

variable "log_group_name" {
  description = "CloudWatch LogGroup-namnet där api-loggarna skrivs (t.ex. /aws/ecs/jobbpilot-dev/api). FailedAccessLogger producerar events i denna grupp via standard ASP.NET ILogger-pipeline."
  type        = string
}

variable "kms_key_id" {
  description = "KMS-nyckel för SNS-topic-encryption (in-transit + at-rest). Återanvänd master-key per BUILD.md §14. OBS: master-nyckeln delas med RDS, S3, andra SNS-topics — blast-radius vid compromise/rotation är hela datalager. Vid framtida key-segmentering kan secops-topic flyttas till dedikerad nyckel utan modul-ändring (endast caller justerar input)."
  type        = string
}

variable "alert_email" {
  description = "Email-adress för SNS-subscription. Tom sträng = ingen subscription skapas (konfigurera manuellt via console eller separat tf-changeset). Subscription-confirmation kräver att mottagaren opt-in via AWS-mail."
  type        = string
  default     = ""
}

variable "alarm_threshold" {
  description = <<-EOT
    Threshold för failed_access_attempt-events per period innan alarm-trigger.

    OBS: mäter TOTAL count över alla users (CloudWatch metric utan dimension).
    ADR 0031 rekommenderar 20/min/user men det kräver per-user-dimensions
    (defereras till user-base tillväxt).

    Recommended:
    - Dev: 50/min (utveckling + integration-tester triggar legitim trafik)
    - Prod initial: 20/min (matchar ADR 0031-mål aggregerat)
    - Prod tuning: monitora false-positive-rate i 2 veckor post-apply; sänk
      mot 10/min om legitima 404 från typo-URLs ligger lågt.

    Vid distribuerad attack (50 users × 1 event vardera) triggar alarm.
    False-positive vid legitim hög-traffic-rampup mitigeras genom CloudWatch
    Insights-query för per-user-drill-down (se runbook).
  EOT
  type        = number
  default     = 50
}

variable "alarm_period_seconds" {
  description = "Aggregeringsperiod för alarm-evaluering i sekunder. Default 60 = 1 min (matchar ADR 0031:s '20 events/min/user'-rekommendation aggregerat över alla users)."
  type        = number
  default     = 60
}

variable "log_pipeline_health_period_seconds" {
  description = "Period för log-pipeline-health-alarm. 900 = 15 min (toleranser för temporary log-buffering eller låg-trafik-perioder utan att false-trigga vid normalt drift)."
  type        = number
  default     = 900
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
