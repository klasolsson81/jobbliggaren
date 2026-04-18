provider "aws" {
  region = var.aws_region

  default_tags {
    tags = var.common_tags
  }
}

# ---------------------------------------------------------------------------
# Budgets — zero-spend + monthly med alerts på 50/80/100% (+ forecast 100%).
# Klas-beslut #9 (kostnadskontroll från dag 1).
# ---------------------------------------------------------------------------

module "budgets" {
  source = "../../modules/budgets"

  monthly_limit_usd   = var.monthly_budget_usd
  notification_emails = var.notification_emails
}

# ---------------------------------------------------------------------------
# CloudTrail — multi-region management events, log file validation.
# ---------------------------------------------------------------------------

module "cloudtrail" {
  source = "../../modules/cloudtrail"

  account_id         = var.account_id
  region             = var.aws_region
  log_retention_days = var.cloudtrail_retention_days
  tags               = var.common_tags
}

# ---------------------------------------------------------------------------
# KMS — master + BYOK keys (BUILD.md §8.4, §13.2).
# ---------------------------------------------------------------------------

module "kms" {
  source = "../../modules/kms"

  account_id = var.account_id
  tags       = var.common_tags
}

# ---------------------------------------------------------------------------
# Secrets Manager — placeholder-secrets för kommande app-behov.
# Värden sätts manuellt när appen behöver dem.
# ---------------------------------------------------------------------------

module "secrets_manager" {
  source = "../../modules/secrets_manager"

  kms_key_arn = module.kms.master_key_arn
  tags        = var.common_tags
}

# ---------------------------------------------------------------------------
# Bedrock — IAM-policy för EU inference profile-invocation.
# Model access approval är MANUELL (se runbook).
# ---------------------------------------------------------------------------

module "bedrock_model_access" {
  source = "../../modules/bedrock_model_access"

  account_id               = var.account_id
  eu_inference_profile_ids = var.eu_inference_profile_ids
  tags                     = var.common_tags
}
