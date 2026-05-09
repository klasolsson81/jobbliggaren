variable "name_prefix" {
  description = "Prefix för resurs-namn (t.ex. \"jobbpilot-dev\")."
  type        = string
}

variable "subnet_ids" {
  description = "Privata subnet-IDs för subnet group."
  type        = list(string)
}

variable "vpc_security_group_ids" {
  description = "Security group-IDs som tillåter access (typiskt redis_security_group_id från network)."
  type        = list(string)
}

variable "kms_key_id" {
  description = "KMS-nyckel för at-rest encryption + AUTH-token-secret."
  type        = string
}

variable "engine" {
  description = "Engine: \"valkey\" (rekommenderad) eller \"redis\". AWS migrerar Redis OSS-användare till Valkey."
  type        = string
  default     = "valkey"
}

variable "engine_version" {
  description = "Engine-version. Valkey 8.x = Redis 8-kompatibel; verifieras med describe-cache-engine-versions."
  type        = string
  default     = "8.0"
}

variable "parameter_group_family" {
  description = "Parameter-group family — \"valkey8\" för Valkey 8.x. AWS-defaulta family ändras med engine-version."
  type        = string
  default     = "valkey8"
}

variable "node_type" {
  description = "Node-type. Lean dev-default; staging/prod sätter cache.t4g.small explicit (BUILD.md §15.1)."
  type        = string
  default     = "cache.t4g.micro"
}

variable "num_cache_clusters" {
  description = "Antal noder i replication-gruppen (1 primary + N-1 replicas). Lean dev = 1; staging/prod sätter 2 explicit."
  type        = number
  default     = 1
}

variable "automatic_failover_enabled" {
  description = "Aktivera automatisk failover. Kräver minst 2 noder. Lean dev = false."
  type        = bool
  default     = false
}

variable "multi_az_enabled" {
  description = "Sprid replicas över AZs. Kräver automatic_failover_enabled = true. Lean dev = false."
  type        = bool
  default     = false
}

variable "snapshot_retention_days" {
  description = "Antal dagar daily snapshots behålls. Lean dev = 1; staging/prod sätter 7 explicit."
  type        = number
  default     = 1
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
