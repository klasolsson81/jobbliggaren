variable "name_prefix" {
  description = "Prefix för ECS-resursnamn."
  type        = string
}

variable "aws_region" {
  description = "Region (för awslogs-driver-konfig + auto-scaling)."
  type        = string
}

variable "private_subnet_ids" {
  description = "Privata subnet-IDs där tasks placeras."
  type        = list(string)
}

variable "ecs_security_group_id" {
  description = "Security group för ECS-tasks (ingress från ALB-SG, egress per ADR 0025)."
  type        = string
}

variable "execution_role_arn" {
  description = "Task execution-role-ARN (ECR pull, log push, secrets-injection)."
  type        = string
}

variable "task_api_role_arn" {
  description = "Task-role för Api-runtime (Bedrock + Secrets + KMS)."
  type        = string
}

variable "task_worker_role_arn" {
  description = "Task-role för Worker-runtime (Secrets + KMS, ingen Bedrock i Fas 1)."
  type        = string
}

# ECR images
variable "api_image_uri" {
  description = "Full ECR image-URI för Api (t.ex. <account>.dkr.ecr.<region>.amazonaws.com/jobbpilot-dev-api:latest)."
  type        = string
}

variable "worker_image_uri" {
  description = "Full ECR image-URI för Worker."
  type        = string
}

# ALB-koppling
variable "api_target_group_arn" {
  description = "ALB target-group-ARN för Api-service."
  type        = string
}

variable "api_container_port" {
  description = "Port Api-containern lyssnar på."
  type        = number
  default     = 8080
}

# CloudWatch Logs
variable "api_log_group_name" {
  description = "CloudWatch LogGroup-namn för Api-container."
  type        = string
}

variable "worker_log_group_name" {
  description = "CloudWatch LogGroup-namn för Worker-container."
  type        = string
}

# Secrets injection via task-def secrets-block (ARNs).
variable "api_secrets" {
  description = "Map av env-var-namn till Secrets Manager-ARN för Api-injection."
  type        = map(string)
  default     = {}
}

variable "worker_secrets" {
  description = "Map av env-var-namn till Secrets Manager-ARN för Worker-injection."
  type        = map(string)
  default     = {}
}

# Env-vars i klartext (icke-känsligt).
variable "api_environment" {
  description = "Map av klartext-env-vars för Api."
  type        = map(string)
  default     = {}
}

variable "worker_environment" {
  description = "Map av klartext-env-vars för Worker."
  type        = map(string)
  default     = {}
}

# Sizing
variable "api_cpu" {
  description = "Fargate CPU-units för Api. 256=0.25, 512=0.5, 1024=1 vCPU."
  type        = number
  default     = 512
}

variable "api_memory" {
  description = "Fargate memory MB för Api."
  type        = number
  default     = 1024
}

variable "worker_cpu" {
  description = "Fargate CPU-units för Worker."
  type        = number
  default     = 256
}

variable "worker_memory" {
  description = "Fargate memory MB för Worker."
  type        = number
  default     = 512
}

# Service desired_count
variable "api_desired_count" {
  description = "Antal Api-tasks som körs. Lean dev = 1; staging/prod 2-10 via auto-scaling."
  type        = number
  default     = 1
}

variable "worker_desired_count" {
  description = "Antal Worker-tasks. Lean dev = 1; staging/prod 1-4."
  type        = number
  default     = 1
}

# Capacity provider — SPOT i dev (70% rabatt, kan tas tillbaka av AWS),
# FARGATE i staging/prod (säkrare).
variable "use_fargate_spot" {
  description = "Använd FARGATE_SPOT istället för FARGATE. Lean dev = true (spar ~70%); staging/prod = false."
  type        = bool
  default     = true
}

# Auto-scaling
variable "enable_autoscaling" {
  description = "Aktivera auto-scaling-targets + CPU-policies. Lean dev = false; staging/prod = true."
  type        = bool
  default     = false
}

variable "api_min_capacity" {
  description = "Min Api-tasks vid auto-scaling."
  type        = number
  default     = 1
}

variable "api_max_capacity" {
  description = "Max Api-tasks vid auto-scaling. BUILD.md §15.1: 10."
  type        = number
  default     = 10
}

variable "worker_min_capacity" {
  type    = number
  default = 1
}

variable "worker_max_capacity" {
  description = "Max Worker-tasks. BUILD.md §15.1: 4."
  type        = number
  default     = 4
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
