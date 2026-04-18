variable "kms_key_arn" {
  description = "KMS-CMK (master-nyckel) som krypterar secrets at rest."
  type        = string
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
