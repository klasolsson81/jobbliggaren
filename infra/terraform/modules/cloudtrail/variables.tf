variable "account_id" {
  description = "AWS account ID."
  type        = string
}

variable "region" {
  description = "Region trail skrivs till."
  type        = string
}

variable "log_retention_days" {
  description = "Hur många dagar S3-objekt bevaras innan lifecycle-expirering."
  type        = number
  default     = 90
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
