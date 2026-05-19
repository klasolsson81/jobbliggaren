terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.80"
    }
  }
}

data "aws_region" "current" {}

# Key policy som tillåter kontots root (via IAM) att administrera; övriga
# behörigheter delas ut per konsument via IAM-policies mot key-ARN.
# CloudWatch Logs måste få explicit Service-principal-grant per
# https://docs.aws.amazon.com/AmazonCloudWatch/latest/logs/encrypt-log-data-kms.html
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

  statement {
    sid    = "AllowCloudWatchLogsEncryption"
    effect = "Allow"
    actions = [
      "kms:Encrypt*",
      "kms:Decrypt*",
      "kms:ReEncrypt*",
      "kms:GenerateDataKey*",
      "kms:Describe*",
    ]
    resources = ["*"]

    principals {
      type        = "Service"
      identifiers = ["logs.${data.aws_region.current.name}.amazonaws.com"]
    }

    condition {
      test     = "ArnLike"
      variable = "kms:EncryptionContext:aws:logs:arn"
      values   = ["arn:aws:logs:${data.aws_region.current.name}:${var.account_id}:log-group:*"]
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

# ---------------------------------------------------------------------------
# TD-13-nyckel (ADR 0049 Beslut 1): envelope encryption för per-användare-DEK
# som wrappar de fyra user-ägda PII-kolumnerna (cover_letter /
# application_notes.content / follow_ups.note / resume_versions.content_enc).
# Separat från master/byok så rotation, key-policy och crypto-erasure-
# livscykel (Beslut 2) isoleras. dotnet-architect 2026-05-19 (§9.2 IaC-
# obligatorisk, ADR 0036 CTO+architect-tandem) — ren IaC-impl av Beslut 1,
# ingen ADR-amendment.
# ---------------------------------------------------------------------------

resource "aws_kms_key" "td13_field" {
  description              = "JobbPilot TD-13 field-encryption — wraps per-user DEKs (ADR 0049 Beslut 1)"
  key_usage                = "ENCRYPT_DECRYPT"
  customer_master_key_spec = "SYMMETRIC_DEFAULT"
  deletion_window_in_days  = var.key_deletion_window_days
  enable_key_rotation      = true
  is_enabled               = true

  policy = data.aws_iam_policy_document.key_policy.json

  tags = merge(var.tags, {
    Name    = "jobbpilot-td13-field-key"
    Purpose = "td13-field-envelope"
  })
}

resource "aws_kms_alias" "td13_field" {
  name          = "alias/jobbpilot-td13-field-key"
  target_key_id = aws_kms_key.td13_field.key_id
}
