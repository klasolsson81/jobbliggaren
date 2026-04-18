variable "aws_region" {
  description = "Primär region."
  type        = string
  default     = "eu-north-1"
}

variable "account_id" {
  type    = string
  default = "710427215829"
}

variable "notification_emails" {
  description = "Mail-adresser för budget-alerts."
  type        = list(string)
}

variable "monthly_budget_usd" {
  type    = number
  default = 50
}

variable "cloudtrail_retention_days" {
  type    = number
  default = 90
}

variable "eu_inference_profile_ids" {
  description = "EU inference profile-IDs appen får anropa. Uppdateras efter Bedrock model-access-approval."
  type        = list(string)
  default = [
    "eu.anthropic.claude-haiku-4-5-20251001-v1:0",
    "eu.anthropic.claude-sonnet-4-6",
  ]
}

variable "common_tags" {
  type = map(string)
  default = {
    Project     = "JobbPilot"
    Environment = "prod"
    ManagedBy   = "terraform"
    Owner       = "klas"
  }
}
