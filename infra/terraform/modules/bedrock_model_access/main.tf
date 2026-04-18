terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.80"
    }
  }
}

# ---------------------------------------------------------------------------
# OBS: Själva "model access"-approvalet görs MANUELLT via AWS Console.
# AWS har ingen first-party Terraform-resource för `bedrock:RequestModelAccess`.
# Denna modul sätter upp IAM-policyn som appen använder när access väl finns.
# Se docs/runbooks/aws-setup.md §3.1 för approval-procedur.
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "bedrock_invoke" {
  statement {
    sid    = "AllowConverseAndInvokeOnEuProfiles"
    effect = "Allow"

    actions = [
      "bedrock:InvokeModel",
      "bedrock:InvokeModelWithResponseStream",
      "bedrock:Converse",
      "bedrock:ConverseStream",
    ]

    # EU inference profile-ARNs (callable objekt) + underliggande foundation
    # models (required för cross-region-dispatch).
    resources = concat(
      [
        for profile_id in var.eu_inference_profile_ids :
        "arn:aws:bedrock:*:${var.account_id}:inference-profile/${profile_id}"
      ],
      [
        for region in var.bedrock_source_regions :
        "arn:aws:bedrock:${region}::foundation-model/anthropic.claude-*"
      ]
    )
  }

  # List / describe — read-only, krävs för app-startup health checks.
  statement {
    sid    = "AllowReadOnlyMetadata"
    effect = "Allow"

    actions = [
      "bedrock:ListFoundationModels",
      "bedrock:ListInferenceProfiles",
      "bedrock:GetFoundationModel",
      "bedrock:GetInferenceProfile",
    ]

    resources = ["*"]
  }
}

resource "aws_iam_policy" "bedrock_invoke" {
  name        = "JobbPilotBedrockInvoke"
  description = "Tillåt appen att anropa Claude-modeller via EU inference profile."
  policy      = data.aws_iam_policy_document.bedrock_invoke.json

  tags = merge(var.tags, { Purpose = "bedrock-invocation" })
}
