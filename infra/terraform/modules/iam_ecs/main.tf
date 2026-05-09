# ---------------------------------------------------------------------------
# Assume-role-policy: bara ecs-tasks.amazonaws.com får assume rollerna.
# Defense-in-depth: aws:SourceAccount-condition förhindrar confused-deputy.
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "assume_role_ecs_tasks" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "aws:SourceAccount"
      values   = [var.account_id]
    }
  }
}

# ---------------------------------------------------------------------------
# Execution-role — använd av ECS-agenten för att STARTA containrarna.
# Delas av alla tasks (api + worker) eftersom dragnings-rättigheterna är
# identiska. Defensiv: får inte förväxlas med task-role (= app-runtime).
# ---------------------------------------------------------------------------

resource "aws_iam_role" "execution" {
  name               = "${var.name_prefix}-ecs-execution"
  description        = "ECS task execution-role: ECR pull + CloudWatch push + Secrets injection vid task-startup."
  assume_role_policy = data.aws_iam_policy_document.assume_role_ecs_tasks.json

  tags = merge(var.tags, {
    Purpose = "ecs-execution"
  })
}

# ECR pull-rättigheter — bara mot våra repos. GetAuthorizationToken kräver "*"
# eftersom den returnerar en token för registry-nivå-auth, inte per-repo.
data "aws_iam_policy_document" "execution" {
  statement {
    sid       = "EcrAuthToken"
    effect    = "Allow"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }

  statement {
    sid    = "EcrPullImages"
    effect = "Allow"
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:GetDownloadUrlForLayer",
      "ecr:BatchGetImage",
    ]
    resources = var.ecr_repository_arns
  }

  # CloudWatch push — bara till våra log-groups + deras streams.
  statement {
    sid    = "CloudWatchLogsWrite"
    effect = "Allow"
    actions = [
      "logs:CreateLogStream",
      "logs:PutLogEvents",
    ]
    resources = concat(
      var.log_group_arns,
      [for arn in var.log_group_arns : "${arn}:*"]
    )
  }

  # Secrets Manager-injection vid task-startup. Task-def `secrets`-block
  # läser secrets via execution-rollen INNAN containern startar.
  statement {
    sid    = "SecretsManagerGetForTaskStartup"
    effect = "Allow"
    actions = [
      "secretsmanager:GetSecretValue",
      "secretsmanager:DescribeSecret",
    ]
    resources = var.secret_arns
  }

  # KMS Decrypt för Secrets Manager-encrypted secrets.
  statement {
    sid       = "KmsDecryptForSecrets"
    effect    = "Allow"
    actions   = ["kms:Decrypt"]
    resources = [var.kms_key_arn]

    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values   = ["secretsmanager.${data.aws_region.current.name}.amazonaws.com"]
    }
  }
}

data "aws_region" "current" {}

resource "aws_iam_policy" "execution" {
  name        = "${var.name_prefix}-ecs-execution"
  description = "Permissions för ECS task execution-rollen."
  policy      = data.aws_iam_policy_document.execution.json

  tags = var.tags
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = aws_iam_policy.execution.arn
}

# ---------------------------------------------------------------------------
# Task-role-api — appens runtime-permissions.
# Bedrock InvokeModel via baseline-policy (data-source-lookup).
# Secrets/KMS för runtime-readers.
# ECS Exec för debug-sessioner via "aws ecs execute-command".
# ---------------------------------------------------------------------------

resource "aws_iam_role" "task_api" {
  name               = "${var.name_prefix}-ecs-task-api"
  description        = "Task-role för Api-runtime: Bedrock + Secrets + KMS + ECS Exec."
  assume_role_policy = data.aws_iam_policy_document.assume_role_ecs_tasks.json

  tags = merge(var.tags, {
    Purpose = "ecs-task-api"
    Service = "api"
  })
}

data "aws_iam_policy_document" "task_api" {
  # Secrets Manager runtime-läsning (utöver task-startup).
  statement {
    sid    = "SecretsManagerRuntimeRead"
    effect = "Allow"
    actions = [
      "secretsmanager:GetSecretValue",
      "secretsmanager:DescribeSecret",
    ]
    resources = var.secret_arns
  }

  statement {
    sid       = "KmsDecryptRuntime"
    effect    = "Allow"
    actions   = ["kms:Decrypt"]
    resources = [var.kms_key_arn]

    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values = [
        "secretsmanager.${data.aws_region.current.name}.amazonaws.com",
        "rds.${data.aws_region.current.name}.amazonaws.com",
        "elasticache.${data.aws_region.current.name}.amazonaws.com",
      ]
    }
  }

  # ECS Exec — möjliggör "aws ecs execute-command" för debug.
  # Replikerar AmazonECSTaskExecutionRolePolicy:s exec-bitar utan att inkludera
  # fulla AWS-managed-policy:n (least-privilege).
  statement {
    sid    = "EcsExecMessaging"
    effect = "Allow"
    actions = [
      "ssmmessages:CreateControlChannel",
      "ssmmessages:CreateDataChannel",
      "ssmmessages:OpenControlChannel",
      "ssmmessages:OpenDataChannel",
    ]
    resources = ["*"] # SSM Messages-API är region-globalt, kan inte begränsas per resurs
  }
}

resource "aws_iam_policy" "task_api" {
  name        = "${var.name_prefix}-ecs-task-api"
  description = "Permissions för Api-task-runtime."
  policy      = data.aws_iam_policy_document.task_api.json

  tags = var.tags
}

resource "aws_iam_role_policy_attachment" "task_api" {
  role       = aws_iam_role.task_api.name
  policy_arn = aws_iam_policy.task_api.arn
}

# Attach baseline JobbPilotBedrockInvoke-policy till Api-task-rollen.
resource "aws_iam_role_policy_attachment" "task_api_bedrock" {
  role       = aws_iam_role.task_api.name
  policy_arn = var.bedrock_invoke_policy_arn
}

# ---------------------------------------------------------------------------
# Task-role-worker — minimi: Secrets + KMS + ECS Exec.
# INGEN Bedrock-access (Fas 1 — Worker har inga AI-jobb. Lyfts vid Fas 4 när
# AI-jobb introduceras via Hangfire.)
# ---------------------------------------------------------------------------

resource "aws_iam_role" "task_worker" {
  name               = "${var.name_prefix}-ecs-task-worker"
  description        = "Task-role för Worker-runtime: Secrets + KMS + ECS Exec. Ingen Bedrock i Fas 1."
  assume_role_policy = data.aws_iam_policy_document.assume_role_ecs_tasks.json

  tags = merge(var.tags, {
    Purpose = "ecs-task-worker"
    Service = "worker"
  })
}

data "aws_iam_policy_document" "task_worker" {
  statement {
    sid    = "SecretsManagerRuntimeRead"
    effect = "Allow"
    actions = [
      "secretsmanager:GetSecretValue",
      "secretsmanager:DescribeSecret",
    ]
    resources = var.secret_arns
  }

  statement {
    sid       = "KmsDecryptRuntime"
    effect    = "Allow"
    actions   = ["kms:Decrypt"]
    resources = [var.kms_key_arn]

    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values = [
        "secretsmanager.${data.aws_region.current.name}.amazonaws.com",
        "rds.${data.aws_region.current.name}.amazonaws.com",
        "elasticache.${data.aws_region.current.name}.amazonaws.com",
      ]
    }
  }

  statement {
    sid    = "EcsExecMessaging"
    effect = "Allow"
    actions = [
      "ssmmessages:CreateControlChannel",
      "ssmmessages:CreateDataChannel",
      "ssmmessages:OpenControlChannel",
      "ssmmessages:OpenDataChannel",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_policy" "task_worker" {
  name        = "${var.name_prefix}-ecs-task-worker"
  description = "Permissions för Worker-task-runtime."
  policy      = data.aws_iam_policy_document.task_worker.json

  tags = var.tags
}

resource "aws_iam_role_policy_attachment" "task_worker" {
  role       = aws_iam_role.task_worker.name
  policy_arn = aws_iam_policy.task_worker.arn
}
