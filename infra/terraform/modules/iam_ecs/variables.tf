variable "name_prefix" {
  description = "Prefix för IAM-resursnamn (t.ex. \"jobbpilot-dev\")."
  type        = string
}

variable "account_id" {
  description = "AWS account ID. Används i conditions för defense-in-depth."
  type        = string
}

variable "ecr_repository_arns" {
  description = "ARNs för ECR-repos som execution-rollen får dra images från."
  type        = list(string)
}

variable "log_group_arns" {
  description = "ARNs för CloudWatch LogGroups som tasks skriver till."
  type        = list(string)
}

variable "secret_arns" {
  description = "ARNs för Secrets Manager-secrets som task-rollen + execution-rollen får läsa."
  type        = list(string)
}

variable "kms_key_arn" {
  description = "Master-KMS-key-ARN. Används för Secrets Manager-decrypt + KMS-encrypted log-decrypt."
  type        = string
}

variable "bedrock_invoke_policy_arn" {
  description = "ARN för baseline JobbPilotBedrockInvoke-policy som task-role-api attachas till."
  type        = string
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
