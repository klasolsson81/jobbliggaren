variable "account_id" {
  description = "AWS account ID."
  type        = string
}

variable "bedrock_source_regions" {
  description = "Regioner EU cross-region inference profile kan hämta kapacitet från."
  type        = list(string)
  default     = ["eu-central-1", "eu-west-1", "eu-north-1", "eu-south-1", "eu-south-2", "eu-west-3"]
}

variable "eu_inference_profile_ids" {
  description = <<-EOT
    EU inference profile-IDs som appen kommer anropa. Verifieras manuellt mot
    `aws bedrock list-inference-profiles --region eu-central-1 --profile jobbpilot`
    och uppdateras i terraform.tfvars efter approval.
  EOT
  type        = list(string)
  default = [
    "eu.anthropic.claude-haiku-4-5-20251001-v1:0",
    "eu.anthropic.claude-sonnet-4-6",
  ]
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
