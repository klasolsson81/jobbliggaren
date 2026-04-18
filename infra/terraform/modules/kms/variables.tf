variable "key_deletion_window_days" {
  description = "Waiting period innan key faktiskt raderas efter schemalagd borttagning."
  type        = number
  default     = 30
}

variable "account_id" {
  description = "AWS account ID (för key policy root-access)."
  type        = string
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
