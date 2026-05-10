variable "name_prefix" {
  description = "Prefix för IAM-resursnamn (t.ex. \"jobbpilot\"). Suffix:as med \"-github-actions-deploy-<env>\" per roll."
  type        = string
}

variable "account_id" {
  description = "AWS account ID. Används i ARN-konstruktion + assume-role-condition."
  type        = string
}

variable "aws_region" {
  description = "Primär region — ARNs konstrueras mot denna (ECR/ECS/Logs)."
  type        = string
}

variable "github_owner" {
  description = "GitHub-org eller -user som äger repot (case-sensitivt). T.ex. \"klasolsson81\"."
  type        = string
}

variable "github_repo" {
  description = "GitHub-repo-namn utan owner-prefix (case-sensitivt). T.ex. \"jobbpilot\"."
  type        = string
}

# ---------------------------------------------------------------------------
# Dev-roll-konfig — STEG 14a. Staging/prod-roller skapas i framtida STEG när
# respektive miljö-stack existerar (least-privilege per miljö, separata blast-
# radius vid GitHub-token-kompromiss).
# ---------------------------------------------------------------------------

variable "dev_name_prefix" {
  description = "Resource-namn-prefix för dev-stacken (matchar var.name_prefix i environments/dev). Används för att konstruera ECR/ECS/Logs-ARNs som dev-rollen får mutera."
  type        = string
  default     = "jobbpilot-dev"
}

variable "dev_tag_pattern" {
  description = "Git-tag-mönster som triggar deploy-dev (sub-claim-StringLike). v*-dev matchar v0.1.0-dev, v1.0.0-dev etc."
  type        = string
  default     = "v*-dev"
}

variable "tags" {
  description = "Common tags."
  type        = map(string)
  default     = {}
}
