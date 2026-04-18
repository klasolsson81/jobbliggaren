variable "monthly_limit_usd" {
  description = "Månatlig budget-tak (USD)."
  type        = number
  default     = 50
}

variable "notification_emails" {
  description = "Mail-adresser som får notifieringar vid budget-trösklar."
  type        = list(string)
}

variable "project" {
  description = "Projekt-tag."
  type        = string
  default     = "JobbPilot"
}
