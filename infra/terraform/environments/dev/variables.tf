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
