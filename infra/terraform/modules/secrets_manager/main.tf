terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.80"
    }
  }
}

# ---------------------------------------------------------------------------
# Placeholder-secrets som senare fylls med riktiga värden under Fas 0+.
# Vi skapar SJÄLVA secret-resursen nu så IAM-policies kan referera dess ARN,
# men hoppar över `aws_secretsmanager_secret_version` — värdet sätts via CLI
# eller console när appen faktiskt behöver det.
#
# Viktigt: BYOK-nycklar sparas INTE här (se BUILD.md §8.4 — de går alltid
# genom KMS envelope per användare, inte delade i Secrets Manager).
# ---------------------------------------------------------------------------

locals {
  # Per BUILD.md §8: systemflöden går ALLTID via Bedrock EU-profil (GDPR).
  # Direkt Anthropic-API är reserverat för BYOK där användaren samtyckt.
  # Därför finns ingen "anthropic-system-key"-placeholder här — den tillkommer
  # bara om en framtida ADR motiverar avsteg från EU-krav.
  placeholders = {
    "jobbpilot/db/app" = {
      description = "Postgres-connection-string för app-usern (prod). Sätts när RDS finns."
    }
    "jobbpilot/jwt/signing-key" = {
      description = "RSA private key för JWT-signering (RS256). Roteras manuellt."
    }
    "jobbpilot/oauth/google" = {
      description = "Google OAuth client ID + secret (Gmail + Calendar integrationer)."
    }
    "jobbpilot/oauth/microsoft" = {
      description = "Microsoft OAuth client ID + secret (framtida)."
    }
    "jobbpilot/sentry/dsn" = {
      description = "Sentry DSN för backend + worker."
    }
  }
}

resource "aws_secretsmanager_secret" "placeholder" {
  for_each = local.placeholders

  name                    = each.key
  description             = each.value.description
  kms_key_id              = var.kms_key_arn
  recovery_window_in_days = 7

  tags = merge(var.tags, {
    Purpose = "app-secret-placeholder"
  })
}
