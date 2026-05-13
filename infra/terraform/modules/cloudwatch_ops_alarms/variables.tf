variable "name_prefix" {
  description = "Prefix för resurs-namn (t.ex. \"jobbpilot-dev\")."
  type        = string
}

variable "worker_log_group_name" {
  description = "CloudWatch LogGroup-namnet där Worker-loggarna skrivs (t.ex. /aws/ecs/jobbpilot-dev/worker). SyncPlatsbankenStreamJob + SystemEventAuditor producerar events i denna grupp via standard ILogger-pipeline."
  type        = string
}

variable "api_log_group_name" {
  description = "CloudWatch LogGroup-namnet där Api-loggarna skrivs (t.ex. /aws/ecs/jobbpilot-dev/api). SystemEventAuditor kan triggas av admin-endpoint (RedactRecruiterPiiCommand) → audit_write_failure-events kan synas på api-sidan också. Metric filter speglas över båda groups så aggregat-metricen täcker full yta."
  type        = string
}

variable "kms_key_id" {
  description = "KMS-nyckel för SNS-topic-encryption (in-transit + at-rest). Återanvänder master-key per BUILD.md §14. OBS: master-nyckeln delas med RDS, S3, andra SNS-topics — blast-radius vid compromise/rotation är hela datalager. Vid framtida key-segmentering kan ops-topic flyttas till dedikerad nyckel utan modul-ändring (endast caller justerar input)."
  type        = string
}

variable "alert_email" {
  description = "Email-adress för SNS-subscription av ops-anomaly-alarms. Tom sträng = ingen subscription skapas (konfigurera manuellt via console eller separat tf-changeset). ISP per Martin 2017 kap. 10: dedicerad ops-channel, inte återbruk av secops_alert_email — när JobbPilot anställer dedikerad ops-on-call eller använder PagerDuty-routing per kategori bryts aliasing-antagandet."
  type        = string
  default     = ""
}

variable "jobtech_failure_threshold" {
  description = <<-EOT
    Threshold för JobTech-sync-failure-events över aggregat-fönstret innan alarm-trigger.

    Mäter TOTAL count av event_name=job_event_failure-events över alla cron-tick i fönstret.

    Recommended:
    - Dev: 3/30min (default — räcker för att skilja transient single-event-blips från sustained degradation)
    - Prod: tuna efter 2 veckors observation av false-positive-rate

    AWS Well-Architected REL06-BP02: aggregate-thresholds över single-period-fönster
    ger tydligare signal än multi-period-evaluation_periods-multiplikation.
  EOT
  type        = number
  default     = 3
}

variable "jobtech_failure_period_seconds" {
  description = "Aggregeringsperiod för JobTech-failure-alarm. Default 1800 = 30 min (täcker 3 stream-cron-cykler à 10 min, matchar BUILD.md §14.4 'JobTech sync misslyckas 3 gånger i rad'-spec aggregerat över fönstret)."
  type        = number
  default     = 1800
}

variable "auditor_failure_threshold" {
  description = <<-EOT
    Threshold för SystemEventAuditor audit_write_failure-events innan alarm-trigger.

    Mäter TOTAL count av event_name=audit_write_failure-events över 5-min-fönstret.

    Default 0 (zero-tolerance): varje audit-write-failure är signal eftersom audit-wire
    är GDPR Art. 30 record-of-processing-foundation. Best-effort-semantiken i ADR 0035 §6
    förutsätter alarm-wire vid retry-exhaustion.
  EOT
  type        = number
  default     = 0
}

variable "auditor_failure_period_seconds" {
  description = "Aggregeringsperiod för SystemEventAuditor-failure-alarm. Default 300 = 5 min (varje audit-failure är signal — kort fönster för snabb operator-respons)."
  type        = number
  default     = 300
}

variable "log_pipeline_health_period_seconds" {
  description = "Period för worker-log-pipeline-health-alarm. 900 = 15 min (toleranser för temporary log-buffering eller låg-trafik-perioder utan att false-trigga vid normalt drift). Speglar cloudwatch_security_alarms.log_pipeline_health-pattern för cohesion (ADR 0031 + security-auditor Minor-3 2026-05-12)."
  type        = number
  default     = 900
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
