variable "domain_name" {
  description = "Primary domain (CN) för cert. Ex: \"dev.jobbpilot.se\"."
  type        = string
}

variable "subject_alternative_names" {
  description = "Ytterligare SANs som certet ska täcka. Tom lista = bara domain_name."
  type        = list(string)
  default     = []
}

variable "route53_zone_id" {
  description = "Route53 zone-ID där validation-CNAMEs skapas. Hämtas typiskt via `data \"aws_route53_zone\"`."
  type        = string
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
