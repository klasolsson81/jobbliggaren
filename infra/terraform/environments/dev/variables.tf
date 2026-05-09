variable "aws_region" {
  description = "Primär region."
  type        = string
  default     = "eu-north-1"
}

variable "account_id" {
  type    = string
  default = "710427215829"
}

variable "name_prefix" {
  description = "Prefix för resurs-namn i denna miljö."
  type        = string
  default     = "jobbpilot-dev"
}

variable "vpc_cidr" {
  description = "CIDR-block för dev-VPC:n. Reserverar 10.0/16; staging/prod tar 10.1/10.2."
  type        = string
  default     = "10.0.0.0/16"
}

variable "rds_engine_version" {
  description = "Postgres-version. BUILD.md §15.1: 18.3."
  type        = string
  default     = "18.3"
}

variable "rds_instance_class" {
  description = "RDS-instance-class. Lean dev = micro; staging/prod sätter db.t4g.medium explicit."
  type        = string
  default     = "db.t4g.micro"
}

variable "redis_engine" {
  description = "ElastiCache-engine: \"valkey\" (rekommenderad) eller \"redis\"."
  type        = string
  default     = "valkey"
}

variable "redis_engine_version" {
  description = "ElastiCache-version. Verifieras via describe-cache-engine-versions."
  type        = string
  default     = "8.0"
}

variable "redis_parameter_group_family" {
  description = "Parameter-group family. \"valkey8\" för Valkey 8.x."
  type        = string
  default     = "valkey8"
}

variable "redis_node_type" {
  description = "ElastiCache node-type. Lean dev = micro; staging/prod sätter cache.t4g.small explicit."
  type        = string
  default     = "cache.t4g.micro"
}

variable "common_tags" {
  type = map(string)
  default = {
    Project     = "JobbPilot"
    Environment = "dev"
    ManagedBy   = "terraform"
    Owner       = "klas"
  }
}
