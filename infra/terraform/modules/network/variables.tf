variable "name_prefix" {
  description = "Prefix för resurs-namn (t.ex. \"jobbpilot-dev\")."
  type        = string
}

variable "vpc_cidr" {
  description = "CIDR-block för VPC:n."
  type        = string
  default     = "10.0.0.0/16"
}

variable "az_count" {
  description = "Antal AZs att sprida subnets över. Standard 3 i eu-north-1."
  type        = number
  default     = 3
}

variable "public_subnet_cidrs" {
  description = "CIDR-block per AZ för publika subnets (ALB + NAT)."
  type        = list(string)
  default     = ["10.0.0.0/24", "10.0.1.0/24", "10.0.2.0/24"]
}

variable "private_subnet_cidrs" {
  description = "CIDR-block per AZ för privata subnets (ECS + RDS + Redis)."
  type        = list(string)
  default     = ["10.0.10.0/24", "10.0.11.0/24", "10.0.12.0/24"]
}

variable "single_nat_gateway" {
  description = "Om true: en NAT-Gateway delas av alla privata subnets (cost-optimized, ej HA)."
  type        = bool
  default     = true
}

variable "enable_vpc_endpoints" {
  description = "Skapa Interface VPC Endpoints för Secrets Manager + KMS samt Gateway-endpoint för S3."
  type        = bool
  default     = true
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
