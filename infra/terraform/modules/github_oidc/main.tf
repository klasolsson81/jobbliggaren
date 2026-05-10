# ---------------------------------------------------------------------------
# GitHub OIDC-federation till AWS — STEG 14a (BUILD.md §15.3).
#
# Skapar OIDC-providern + roll(er) som GitHub Actions assumar via short-lived
# tokens (sts:AssumeRoleWithWebIdentity). Ersätter långlivade access-keys per
# Bootstrap-IAM-user (raderad som sista steg av Fas 0). Trust-policy är scope:ad
# till specifika sub-claims så bara förväntade workflows kan assumas rollen —
# kompromisserad GitHub-token utanför scope ger inte AWS-access.
#
# Provider är delad — alla framtida deploy-roller (staging/prod) hänger på
# samma OIDC-provider. Bara en provider per AWS-konto + GitHub.
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# OIDC identity provider
#
# AWS validerar GitHub:s OIDC-cert via Trust Store sedan juli 2023 — ingen
# manuell thumbprint behövs (AWS rekommenderar att utelämna `thumbprint_list`
# eller lämna [] så AWS hanterar rotation automatiskt). Lämnas `null` → AWS
# återanvänder default-thumbprint som sköts av AWS.
#
# client_id_list = audience-claim som tokens ska ha. GitHub Actions sätter
# audience `sts.amazonaws.com` när `aws-actions/configure-aws-credentials@v4`
# används (default).
# ---------------------------------------------------------------------------

resource "aws_iam_openid_connect_provider" "github" {
  url             = "https://token.actions.githubusercontent.com"
  client_id_list  = ["sts.amazonaws.com"]
  thumbprint_list = []

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-github-oidc"
    Purpose = "github-actions-federation"
  })
}

# ---------------------------------------------------------------------------
# Dev deploy-roll
#
# Trust-policy:
#   - Federated principal: OIDC-providern ovan
#   - Action: sts:AssumeRoleWithWebIdentity
#   - aud-claim: sts.amazonaws.com (StringEquals — exakt match)
#   - sub-claim: StringLike, scope:at till EXAKT en use case:
#       ref:refs/tags/v*-dev → deploy-dev.yml-trigger
#
# Strikt scope per security-auditor + code-reviewer 2026-05-10. Bakgrund:
# build.yml (push på main + PR) körs med `permissions: contents: read` och
# anropar inte AWS — den behöver inte assumera deploy-rollen. Att lämna
# main/pull_request i sub-claim öppnar för accidental scope-expansion vid
# framtida workflow-edit (privilege-escalation om någon lägger till
# id-token: write i build.yml). Read-only AWS-anrop i build.yml får i så fall
# en separat read_only-roll med eget scope.
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "assume_role_deploy_dev" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values = [
        "repo:${var.github_owner}/${var.github_repo}:ref:refs/tags/${var.dev_tag_pattern}",
      ]
    }
  }
}

resource "aws_iam_role" "deploy_dev" {
  name        = "${var.name_prefix}-github-actions-deploy-dev"
  description = "GitHub Actions deploy-roll för dev-miljön (STEG 14a). Assumas via OIDC från workflows i ${var.github_owner}/${var.github_repo}."

  assume_role_policy = data.aws_iam_policy_document.assume_role_deploy_dev.json

  # Max 1h sessions — kort blast-radius vid token-läckage. Räcker för en deploy
  # (ECR push + ECS update + smoke-test ~5-10 min normalt).
  max_session_duration = 3600

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-github-actions-deploy-dev"
    Purpose = "github-actions-deploy"
    Service = "dev"
  })
}

# ---------------------------------------------------------------------------
# Inline-policy — least-privilege för dev-deploy.
#
# ARN-konstruktion via string-format (inte cross-stack data-source) — modulen
# kör i prod/baseline-stacken som inte har dev-stackens state. Resurs-namn
# följer var.dev_name_prefix-konventionen (jobbpilot-dev-api, -worker, etc.).
# ---------------------------------------------------------------------------

locals {
  ecr_api_arn    = "arn:aws:ecr:${var.aws_region}:${var.account_id}:repository/${var.dev_name_prefix}-api"
  ecr_worker_arn = "arn:aws:ecr:${var.aws_region}:${var.account_id}:repository/${var.dev_name_prefix}-worker"

  ecs_cluster_arn = "arn:aws:ecs:${var.aws_region}:${var.account_id}:cluster/${var.dev_name_prefix}-cluster"
  ecs_api_service_arn    = "arn:aws:ecs:${var.aws_region}:${var.account_id}:service/${var.dev_name_prefix}-cluster/${var.dev_name_prefix}-api"
  ecs_worker_service_arn = "arn:aws:ecs:${var.aws_region}:${var.account_id}:service/${var.dev_name_prefix}-cluster/${var.dev_name_prefix}-worker"
  ecs_api_taskdef_arn    = "arn:aws:ecs:${var.aws_region}:${var.account_id}:task-definition/${var.dev_name_prefix}-api:*"
  ecs_worker_taskdef_arn = "arn:aws:ecs:${var.aws_region}:${var.account_id}:task-definition/${var.dev_name_prefix}-worker:*"

  iam_execution_role_arn   = "arn:aws:iam::${var.account_id}:role/${var.dev_name_prefix}-ecs-execution"
  iam_task_api_role_arn    = "arn:aws:iam::${var.account_id}:role/${var.dev_name_prefix}-ecs-task-api"
  iam_task_worker_role_arn = "arn:aws:iam::${var.account_id}:role/${var.dev_name_prefix}-ecs-task-worker"

  logs_group_arn_pattern = "arn:aws:logs:${var.aws_region}:${var.account_id}:log-group:/aws/ecs/${var.dev_name_prefix}/*"
}

data "aws_iam_policy_document" "deploy_dev" {
  # ECR auth-token är registry-globalt — kan inte begränsas per repo.
  statement {
    sid       = "EcrAuthToken"
    effect    = "Allow"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }

  # ECR push på api + worker (inkl. read för image-existens-check innan push).
  statement {
    sid    = "EcrPushOurRepos"
    effect = "Allow"
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:GetDownloadUrlForLayer",
      "ecr:BatchGetImage",
      "ecr:DescribeImages",
      "ecr:DescribeRepositories",
      "ecr:InitiateLayerUpload",
      "ecr:UploadLayerPart",
      "ecr:CompleteLayerUpload",
      "ecr:PutImage",
    ]
    resources = [local.ecr_api_arn, local.ecr_worker_arn]
  }

  # ECS service + task-def — Describe (read) + Update (mutate).
  statement {
    sid    = "EcsReadOurCluster"
    effect = "Allow"
    actions = [
      "ecs:DescribeClusters",
      "ecs:DescribeServices",
      "ecs:DescribeTasks",
      "ecs:DescribeTaskDefinition",
      "ecs:ListTasks",
    ]
    resources = [
      local.ecs_cluster_arn,
      local.ecs_api_service_arn,
      local.ecs_worker_service_arn,
      local.ecs_api_taskdef_arn,
      local.ecs_worker_taskdef_arn,
    ]
  }

  # RegisterTaskDefinition kan inte begränsas per resource (AWS-API tar inga
  # resource-arn:s — alla register-anrop kör mot "*"). Mitigation: PassRole-
  # statement nedan begränsar VILKA roller task-defen får referera, vilket
  # förhindrar att en kompromisserad workflow registrerar en task-def som
  # exfiltrerar via en privilegierad task-roll.
  statement {
    sid       = "EcsRegisterTaskDefinition"
    effect    = "Allow"
    actions   = ["ecs:RegisterTaskDefinition"]
    resources = ["*"]
  }

  statement {
    sid    = "EcsUpdateOurServices"
    effect = "Allow"
    actions = [
      "ecs:UpdateService",
    ]
    resources = [
      local.ecs_api_service_arn,
      local.ecs_worker_service_arn,
    ]
  }

  # PassRole begränsar VILKA IAM-roller deploy-rollen kan koppla till en
  # task-def. Utan condition kunde en workflow registrera en task-def med
  # admin-roll → privilege-escalation. iam:PassedToService = ecs-tasks.amazonaws.com
  # förhindrar PassRole till andra services (Lambda, EC2 etc.).
  statement {
    sid    = "IamPassRoleForTaskDef"
    effect = "Allow"
    actions = ["iam:PassRole"]
    resources = [
      local.iam_execution_role_arn,
      local.iam_task_api_role_arn,
      local.iam_task_worker_role_arn,
    ]

    condition {
      test     = "StringEquals"
      variable = "iam:PassedToService"
      values   = ["ecs-tasks.amazonaws.com"]
    }
  }

  # CloudWatch Logs read-only för deploy-failure-debug. Workflow läser senaste
  # task-loggarna vid deployment-fail för att inkludera i workflow-output.
  # Skriv-rättigheter saknas medvetet — Logs skrivs av execution-rollen, inte
  # av deploy-rollen.
  statement {
    sid    = "LogsReadForSmoke"
    effect = "Allow"
    actions = [
      "logs:DescribeLogGroups",
      "logs:DescribeLogStreams",
      "logs:GetLogEvents",
      "logs:FilterLogEvents",
    ]
    resources = [local.logs_group_arn_pattern]
  }
}

resource "aws_iam_policy" "deploy_dev" {
  name        = "${var.name_prefix}-github-actions-deploy-dev"
  description = "Least-privilege deploy-permissions för GitHub Actions mot dev-miljön (ECR push + ECS service-update + Logs read)."
  policy      = data.aws_iam_policy_document.deploy_dev.json

  tags = var.tags
}

resource "aws_iam_role_policy_attachment" "deploy_dev" {
  role       = aws_iam_role.deploy_dev.name
  policy_arn = aws_iam_policy.deploy_dev.arn
}
