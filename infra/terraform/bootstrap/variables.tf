variable "aws_region" {
  description = "Primary AWS region for state resources."
  type        = string
  default     = "eu-north-1"
}

variable "account_id" {
  description = "AWS account ID. Ingår i bucket-namnet för unikhet."
  type        = string
  default     = "710427215829"
}

variable "project" {
  description = "Projekt-tag."
  type        = string
  default     = "JobbPilot"
}
