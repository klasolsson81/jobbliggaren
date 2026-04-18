terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.80"
    }
  }
}

# Key policy som tillåter kontots root (via IAM) att administrera; övriga
# behörigheter delas ut per konsument via IAM-policies mot key-ARN.
data "aws_iam_policy_document" "key_policy" {
  statement {
    sid       = "EnableIAMUserPermissions"
    effect    = "Allow"
    actions   = ["kms:*"]
    resources = ["*"]

    principals {
      type        = "AWS"
      identifiers = ["arn:aws:iam::${var.account_id}:root"]
    }
  }
}

# ---------------------------------------------------------------------------
# Master-nyckel: används för app-secrets (Secrets Manager), CV-uploads (S3),
# RDS/ElastiCache-kryptering där KMS-CMK önskas över AWS-managed.
# ---------------------------------------------------------------------------

resource "aws_kms_key" "master" {
  description              = "JobbPilot master encryption key — app-secrets, CVs, S3 SSE-KMS"
  key_usage                = "ENCRYPT_DECRYPT"
  customer_master_key_spec = "SYMMETRIC_DEFAULT"
  deletion_window_in_days  = var.key_deletion_window_days
  enable_key_rotation      = true
  is_enabled               = true

  policy = data.aws_iam_policy_document.key_policy.json

  tags = merge(var.tags, {
    Name    = "jobbpilot-master-key"
    Purpose = "app-encryption"
  })
}

resource "aws_kms_alias" "master" {
  name          = "alias/jobbpilot-master-key"
  target_key_id = aws_kms_key.master.key_id
}

# ---------------------------------------------------------------------------
# BYOK-nyckel: envelope encryption för användarnas AI-API-nycklar per BUILD.md
# §8.4. Separat från master så att rotation och policy kan hanteras isolerat.
# ---------------------------------------------------------------------------

resource "aws_kms_key" "byok" {
  description              = "JobbPilot BYOK envelope encryption — wraps user-supplied AI API keys"
  key_usage                = "ENCRYPT_DECRYPT"
  customer_master_key_spec = "SYMMETRIC_DEFAULT"
  deletion_window_in_days  = var.key_deletion_window_days
  enable_key_rotation      = true
  is_enabled               = true

  policy = data.aws_iam_policy_document.key_policy.json

  tags = merge(var.tags, {
    Name    = "jobbpilot-byok-key"
    Purpose = "byok-envelope"
  })
}

resource "aws_kms_alias" "byok" {
  name          = "alias/jobbpilot-byok-key"
  target_key_id = aws_kms_key.byok.key_id
}
