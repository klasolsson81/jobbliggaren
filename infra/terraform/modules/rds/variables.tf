variable "name_prefix" {
  description = "Prefix för resurs-namn (t.ex. \"jobbpilot-dev\")."
  type        = string
}

variable "subnet_ids" {
  description = "Privata subnet-IDs för DB Subnet Group (minst 2 AZs för Multi-AZ)."
  type        = list(string)
}

variable "vpc_security_group_ids" {
  description = "Security group-IDs som tillåter access till RDS (typiskt rds_security_group_id från network)."
  type        = list(string)
}

variable "kms_key_id" {
  description = "KMS-nyckel för storage encryption + Performance Insights + master-secret."
  type        = string
}

variable "engine_version" {
  description = "Postgres-version (BUILD.md §15.1: 18.3)."
  type        = string
  default     = "18.3"
}

variable "instance_class" {
  description = "RDS-instance-class. BUILD.md §15.1: db.t4g.medium."
  type        = string
  default     = "db.t4g.medium"
}

variable "allocated_storage" {
  description = "Initial storage i GB. Auto-scale upp till max_allocated_storage."
  type        = number
  default     = 20
}

variable "max_allocated_storage" {
  description = "Max auto-scaled storage i GB."
  type        = number
  default     = 100
}

variable "multi_az" {
  description = "Multi-AZ deployment (HA + automatisk failover). BUILD.md §15.1: true."
  type        = bool
  default     = true
}

variable "db_name" {
  description = "Initial databasnamn."
  type        = string
  default     = "jobbpilot"
}

variable "master_username" {
  description = "Master-användare. AWS-Postgres reserverar \"postgres\" — använd annat."
  type        = string
  default     = "jobbpilot_admin"
}

variable "backup_retention_days" {
  description = "Antal dagar automated backups behålls."
  type        = number
  default     = 7
}

variable "backup_window" {
  description = "UTC-fönster för automated backup (HH:MM-HH:MM)."
  type        = string
  default     = "02:00-03:00"
}

variable "maintenance_window" {
  description = "UTC-fönster för maintenance (Day:HH:MM-Day:HH:MM). Sön 04:00-05:00 = efter retention-jobb."
  type        = string
  default     = "Sun:04:00-Sun:05:00"
}

variable "deletion_protection" {
  description = "Hindrar terraform destroy + AWS Console-radering."
  type        = bool
  default     = true
}

variable "performance_insights_enabled" {
  description = "Aktivera Performance Insights (gratis 7-dagars retention)."
  type        = bool
  default     = true
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
