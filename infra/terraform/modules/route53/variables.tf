variable "domain_name" {
  description = "Apex-domän för hosted zone (utan trailing dot). Ex: \"jobbpilot.se\"."
  type        = string
}

variable "comment" {
  description = "Hosted-zone-beskrivning. Visas i AWS Console."
  type        = string
  default     = "JobbPilot DNS — managed by Terraform"
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
