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

# ---------------------------------------------------------------------------
# Migrate-task secret-yta (STEG 14b). Separat från `secret_arns` eftersom
# migrate-rollen behöver:
#   1. GetSecretValue på master-secret (RDS-managerad creds-secret)
#   2. PutSecretValue + GetSecretValue på app + hangfire connection-string-secrets
# Övriga task-roller (api/worker) ska INTE ha PutSecretValue-yta.
# ---------------------------------------------------------------------------

variable "migrate_master_secret_arn" {
  description = "ARN för RDS-master-secret (AWS-managerad). Migrate-rollen läser denna för superuser-creds. Tom sträng = migrate-roll skapas inte."
  type        = string
  default     = ""
}

variable "migrate_writable_secret_arns" {
  description = "ARNs för secrets där migrate-rollen får skriva final connection-strings (app + hangfire). Tom lista = migrate-roll skapas inte."
  type        = list(string)
  default     = []
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
