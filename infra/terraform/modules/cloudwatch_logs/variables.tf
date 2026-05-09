variable "name_prefix" {
  description = "Prefix för LogGroup-namn (t.ex. \"jobbpilot-dev\")."
  type        = string
}

variable "log_group_names" {
  description = "Lista av log-group-suffix. Default: api, worker, ecs-exec."
  type        = list(string)
  default     = ["api", "worker", "ecs-exec"]
}

variable "retention_in_days" {
  description = "Retention-fönster i dagar. ADR 0024 D7: 30d för app/audit-logs."
  type        = number
  default     = 30
}

variable "kms_key_id" {
  description = "KMS-nyckel för log-encryption. Använder typiskt master-key."
  type        = string
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
