variable "name_prefix" {
  description = "Prefix för repo-namn (t.ex. \"jobbpilot\" → repos blir \"jobbpilot-api\", \"jobbpilot-worker\")."
  type        = string
}

variable "repository_names" {
  description = "Lista av service-namn som ska få egen ECR-repo."
  type        = list(string)
  default     = ["api", "worker"]
}

variable "image_tag_mutability" {
  description = "MUTABLE eller IMMUTABLE. Lean dev = MUTABLE (latest-tag återanvänds); staging/prod sätter IMMUTABLE."
  type        = string
  default     = "MUTABLE"
}

variable "scan_on_push" {
  description = "ECR vulnerability scanning vid push. Default på."
  type        = bool
  default     = true
}

variable "kms_key_id" {
  description = "KMS-nyckel för image-encryption. Om null används AES256."
  type        = string
  default     = null
}

variable "keep_last_n_images" {
  description = "Lifecycle policy: behåll bara senaste N images per repo."
  type        = number
  default     = 10
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
