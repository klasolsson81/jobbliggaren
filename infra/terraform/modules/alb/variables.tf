variable "name_prefix" {
  description = "Prefix för ALB-resursnamn."
  type        = string
}

variable "vpc_id" {
  description = "VPC där ALB + target-groups skapas."
  type        = string
}

variable "public_subnet_ids" {
  description = "Publika subnet-IDs (minst 2 AZs) där ALB lyssnar."
  type        = list(string)
}

variable "alb_security_group_id" {
  description = "Security group som tillåter inkommande 80/443 från internet."
  type        = string
}

variable "api_target_port" {
  description = "Port som Api-task-containrar lyssnar på."
  type        = number
  default     = 8080
}

variable "health_check_path" {
  description = "Health-check-path. BUILD.md §15.4: /api/ready."
  type        = string
  default     = "/api/ready"
}

variable "health_check_interval_seconds" {
  description = "Sekunder mellan health-checks."
  type        = number
  default     = 30
}

variable "health_check_timeout_seconds" {
  description = "Sekunder innan health-check anses misslyckad."
  type        = number
  default     = 5
}

variable "healthy_threshold_count" {
  description = "Antal lyckade checks innan target marked healthy."
  type        = number
  default     = 2
}

variable "unhealthy_threshold_count" {
  description = "Antal misslyckade checks innan target marked unhealthy."
  type        = number
  default     = 3
}

variable "deregistration_delay_seconds" {
  description = "Connection-drain-tid vid deregister. Lägre = snabbare deploys, högre = säkrare."
  type        = number
  default     = 30
}

variable "enable_deletion_protection" {
  description = "Hindra terraform destroy + AWS Console-radering. Lean dev = false; staging/prod = true."
  type        = bool
  default     = false
}

variable "https_listener_enabled" {
  description = "Aktivera HTTPS-listener på 443. Kräver ACM-cert mot egen domän. Lean dev utan domän = false."
  type        = bool
  default     = false
}

variable "acm_certificate_arn" {
  description = "ACM-cert-ARN för HTTPS-listener. Krävs om https_listener_enabled = true."
  type        = string
  default     = null
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
