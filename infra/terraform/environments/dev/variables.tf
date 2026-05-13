variable "aws_region" {
  description = "Primär region."
  type        = string
  default     = "eu-north-1"
}

variable "account_id" {
  type    = string
  default = "710427215829"
}

variable "name_prefix" {
  description = "Prefix för resurs-namn i denna miljö."
  type        = string
  default     = "jobbpilot-dev"
}

variable "vpc_cidr" {
  description = "CIDR-block för dev-VPC:n. Reserverar 10.0/16; staging/prod tar 10.1/10.2."
  type        = string
  default     = "10.0.0.0/16"
}

variable "rds_engine_version" {
  description = "Postgres-version. BUILD.md §15.1: 18.3."
  type        = string
  default     = "18.3"
}

variable "rds_instance_class" {
  description = "RDS-instance-class. Lean dev = micro; staging/prod sätter db.t4g.medium explicit."
  type        = string
  default     = "db.t4g.micro"
}

variable "redis_engine" {
  description = "ElastiCache-engine: \"valkey\" (rekommenderad) eller \"redis\"."
  type        = string
  default     = "valkey"
}

variable "redis_engine_version" {
  description = "ElastiCache-version. Verifieras via describe-cache-engine-versions."
  type        = string
  default     = "8.0"
}

variable "redis_parameter_group_family" {
  description = "Parameter-group family. \"valkey8\" för Valkey 8.x."
  type        = string
  default     = "valkey8"
}

variable "redis_node_type" {
  description = "ElastiCache node-type. Lean dev = micro; staging/prod sätter cache.t4g.small explicit."
  type        = string
  default     = "cache.t4g.micro"
}

variable "common_tags" {
  type = map(string)
  default = {
    Project     = "JobbPilot"
    Environment = "dev"
    ManagedBy   = "terraform"
    Owner       = "klas"
  }
}

# ---------------------------------------------------------------------------
# STEG 13b — container-infra
# ---------------------------------------------------------------------------

variable "api_image_tag" {
  description = "Image-tag för Api-container. Default \"latest\" (mutable för dev). STEG 14 ersätter med SHA-tag via GitHub Actions."
  type        = string
  default     = "latest"
}

variable "worker_image_tag" {
  description = "Image-tag för Worker-container."
  type        = string
  default     = "latest"
}

variable "migrate_image_tag" {
  description = "Image-tag för Migrate one-shot DDL-init (STEG 14b). Tom = task-def skapas inte. Sätts efter docker push av migrate-image."
  type        = string
  default     = ""
}

variable "api_cpu" {
  description = "Fargate CPU för Api. Lean dev = 512 (0.5 vCPU)."
  type        = number
  default     = 512
}

variable "api_memory" {
  description = "Fargate memory MB för Api. Lean dev = 1024 (1 GB)."
  type        = number
  default     = 1024
}

variable "worker_cpu" {
  description = "Fargate CPU för Worker. Lean dev = 256 (0.25 vCPU)."
  type        = number
  default     = 256
}

variable "worker_memory" {
  description = "Fargate memory MB för Worker. Lean dev = 512 (0.5 GB)."
  type        = number
  default     = 512
}

variable "api_desired_count" {
  description = "Antal Api-tasks. Lean dev = 1."
  type        = number
  default     = 1
}

variable "worker_desired_count" {
  description = "Antal Worker-tasks. Lean dev = 1."
  type        = number
  default     = 1
}

variable "use_fargate_spot" {
  description = "FARGATE_SPOT för ~70% rabatt. Lean dev = true; staging/prod = false."
  type        = bool
  default     = true
}

variable "enable_autoscaling" {
  description = "ECS auto-scaling targets + CPU-policies. Lean dev = false (fast desired_count); staging/prod = true."
  type        = bool
  default     = false
}

variable "initial_admin_email" {
  description = "Email till första admin-användaren. IdempotentAdminRoleSeeder tilldelar Admin-rollen vid host-startup till user med matchande email. Sätts som env-var AdminBootstrap__InitialAdminEmail på Api-task-def (icke-känsligt; lösenord sätts vid registrering). Tom sträng = ingen auto-tilldelning. Se ADR 0028."
  type        = string
  default     = ""
}

variable "alb_https_enabled" {
  description = "ALB HTTPS-listener på 443. Default false per ADR 0026 (HTTP-only acceptance under Fas 0 med tidsfönster + triggers). Sätts true när ADR 0026-trigger uppfylls (domän + ACM-cert, eller superseder-ADR). Värdet injiceras också som env-var Alb__HttpsEnabled till Api-tasken som gate:ar app.UseHttpsRedirection() i Program.cs (Sec-Major-2-fix STEG 13b)."
  type        = bool
  default     = false
}

variable "alb_acm_certificate_arn" {
  description = "ACM-cert-ARN för HTTPS-listener. Sätts efter domän-registrering (TD-30) + ACM-utfärdande. När detta sätts → flippa alb_https_enabled = true samtidigt. Värdet hämtas från output `acm_dev_certificate_arn` post-validering."
  type        = string
  default     = null
}

# ---------------------------------------------------------------------------
# STEG 13c — DNS + TLS (ADR 0026 trigger 1 / TD-30)
# ---------------------------------------------------------------------------

variable "apex_domain_name" {
  description = "Apex-domän vars hosted zone bor i prod/baseline. Dev-stack lookar upp via `data \"aws_route53_zone\"`. Måste matcha `domain_name` i prod-stack."
  type        = string
  default     = "jobbpilot.se"
}

variable "dev_subdomain" {
  description = "Subdomän under apex för dev-miljön. Resulterar i FQDN `<subdomain>.<apex>` (ex: dev.jobbpilot.se)."
  type        = string
  default     = "dev"
}

# ---------------------------------------------------------------------------
# F2-P3 — Cost controls (Budget Actions, ADR 0005-amendment 2026-05-12)
# ---------------------------------------------------------------------------

variable "cost_anomaly_alert_email" {
  description = "Email-adress för SNS-subscription av cost-anomaly-events (publiceras vid Budget Action $50/mån-threshold-trigger). Tom sträng = ingen subscription skapas (konfigurera manuellt via console eller separat tf-changeset)."
  type        = string
  default     = ""
}

variable "baseline_budget_name" {
  description = "Namn på AWS Budget i prod/baseline-stack som F2-P3 Budget Action ska binda mot. Måste matcha `aws_budgets_budget.monthly.name` i modules/budgets/. Default \"jobbpilot-monthly\" (befintlig)."
  type        = string
  default     = "jobbpilot-monthly"
}

# ---------------------------------------------------------------------------
# TD-68 — Security anomaly detection (ADR 0031)
# ---------------------------------------------------------------------------

variable "secops_alert_email" {
  description = "Email-adress för SNS-subscription av secops-anomaly-alarms. Tom sträng = ingen subscription skapas (konfigurera manuellt via console eller separat tf-changeset). Subscription-confirmation kräver opt-in via AWS-mail."
  type        = string
  default     = ""
}

variable "failed_access_alarm_threshold" {
  description = "Threshold för failed_access_attempt-events per period (TD-68 / ADR 0031). Dev-default 50 är högt eftersom utveckling triggar via integration-tester. Prod sänker detta."
  type        = number
  default     = 50
}

# ---------------------------------------------------------------------------
# ADR 0036 — Ops anomaly detection (cohesion-pendant till ADR 0031/TD-68)
# ---------------------------------------------------------------------------

variable "ops_alert_email" {
  description = "Email-adress för SNS-subscription av ops-anomaly-alarms (jobtech-sync-failures, auditor-write-failures, worker-log-pipeline-health). Separat variabel från secops_alert_email per ISP (Martin 2017 kap. 10) — ops-on-call och security-on-call är distinkta triage-flöden. Kan vara samma adress i dev-tfvars utan att låsa designen. Tom sträng = ingen subscription skapas."
  type        = string
  default     = ""
}
