# ---------------------------------------------------------------------------
# ECS Fargate cluster med Container Insights aktiverat.
# Capacity providers: FARGATE + FARGATE_SPOT. Default-strategi väljs via
# var.use_fargate_spot — services använder den valda providern via
# capacity_provider_strategy-block nedan.
# ---------------------------------------------------------------------------

resource "aws_ecs_cluster" "this" {
  name = "${var.name_prefix}-cluster"

  setting {
    name  = "containerInsights"
    value = "enabled"
  }

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-cluster"
  })
}

resource "aws_ecs_cluster_capacity_providers" "this" {
  cluster_name = aws_ecs_cluster.this.name

  capacity_providers = ["FARGATE", "FARGATE_SPOT"]

  default_capacity_provider_strategy {
    capacity_provider = var.use_fargate_spot ? "FARGATE_SPOT" : "FARGATE"
    weight            = 1
    base              = 0
  }
}

# ---------------------------------------------------------------------------
# Task-def: Api
# Container lyssnar på 8080 (matchar ECS-SG ingress från ALB-SG).
# Secrets injiceras via secrets-block; env-vars för icke-känsligt.
# Health-check via curl mot /api/ready (curl ingår i mcr.microsoft.com/dotnet/aspnet:10.0).
# Non-root user förutsätts av Dockerfile (USER 1000).
# ---------------------------------------------------------------------------

resource "aws_ecs_task_definition" "api" {
  family                   = "${var.name_prefix}-api"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = var.api_cpu
  memory                   = var.api_memory

  execution_role_arn = var.execution_role_arn
  task_role_arn      = var.task_api_role_arn

  container_definitions = jsonencode([
    {
      name      = "api"
      image     = var.api_image_uri
      essential = true

      portMappings = [
        {
          containerPort = var.api_container_port
          hostPort      = var.api_container_port
          protocol      = "tcp"
        }
      ]

      environment = [
        for k, v in var.api_environment : { name = k, value = v }
      ]

      secrets = [
        for k, arn in var.api_secrets : { name = k, valueFrom = arn }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = var.api_log_group_name
          awslogs-region        = var.aws_region
          awslogs-stream-prefix = "api"
        }
      }

      # Defense-in-depth: containern får inte eskalera privileges.
      readonlyRootFilesystem = false # ASP.NET behöver tmp-dir
      privileged             = false

      # Ingen container-level healthCheck här — kräver curl i image (extra
      # attack-yta + 3 MB). ALB target-group har egen health-check mot
      # /api/ready som är auktoritativ för registration. Container-crash
      # detekteras ändå av ECS via container exit code.
    }
  ])

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-api-taskdef"
    Service = "api"
  })
}

# ---------------------------------------------------------------------------
# Task-def: Worker
# HTTP-fri per ADR 0023 — inga portMappings. Health-check via process-status
# (Hangfire-server håller processen vid liv; om processen kraschar märker
# ECS det via container exit code).
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# Task-def: Migrate one-shot (STEG 14b)
#
# Körs via `aws ecs run-task` engångs vid Fas 0-stängning + future schema-
# mutationer. Inga services, ingen autoscaling, inga portMappings. Container
# exitar med code 0 efter Phase A-D klart (se src/JobbPilot.Migrate/).
#
# Skapas bara om migrate_image_uri != "" (count-pattern) — håller IaC
# backwards-compatibel.
# ---------------------------------------------------------------------------

resource "aws_ecs_task_definition" "migrate" {
  count = var.migrate_image_uri != "" ? 1 : 0

  family                   = "${var.name_prefix}-migrate"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = var.migrate_cpu
  memory                   = var.migrate_memory

  execution_role_arn = var.execution_role_arn
  task_role_arn      = var.task_migrate_role_arn

  container_definitions = jsonencode([
    {
      name      = "migrate"
      image     = var.migrate_image_uri
      essential = true

      # Inga portMappings — Migrate är HTTP-fri (one-shot console).

      environment = [
        for k, v in var.migrate_environment : { name = k, value = v }
      ]

      secrets = [
        for k, arn in var.migrate_secrets : { name = k, valueFrom = arn }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = var.migrate_log_group_name
          awslogs-region        = var.aws_region
          awslogs-stream-prefix = "migrate"
        }
      }

      readonlyRootFilesystem = false
      privileged             = false

      # Inga healthCheck — one-shot exitar deterministiskt.
      stopTimeout = 30
    }
  ])

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-migrate-taskdef"
    Service = "migrate"
  })
}

resource "aws_ecs_task_definition" "worker" {
  family                   = "${var.name_prefix}-worker"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = var.worker_cpu
  memory                   = var.worker_memory

  execution_role_arn = var.execution_role_arn
  task_role_arn      = var.task_worker_role_arn

  container_definitions = jsonencode([
    {
      name      = "worker"
      image     = var.worker_image_uri
      essential = true

      # Inga portMappings — Worker är HTTP-fri (ADR 0023).

      environment = [
        for k, v in var.worker_environment : { name = k, value = v }
      ]

      secrets = [
        for k, arn in var.worker_secrets : { name = k, valueFrom = arn }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = var.worker_log_group_name
          awslogs-region        = var.aws_region
          awslogs-stream-prefix = "worker"
        }
      }

      readonlyRootFilesystem = false
      privileged             = false

      # Inga healthCheck — Hangfire-process är sin egen liveness-signal.
      # Container exit code != 0 → ECS startar om automatiskt.

      # Worker SIGTERM-handling: Hangfire ShutdownTimeoutSeconds=25 + Fargate
      # stopTimeout=30 (default) ger 5s margin (TD-17 punkt 6).
      stopTimeout = 30
    }
  ])

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-worker-taskdef"
    Service = "worker"
  })
}

# ---------------------------------------------------------------------------
# Service: Api — registreras hos ALB target-group.
# ---------------------------------------------------------------------------

resource "aws_ecs_service" "api" {
  name                   = "${var.name_prefix}-api"
  cluster                = aws_ecs_cluster.this.id
  task_definition        = aws_ecs_task_definition.api.arn
  desired_count          = var.api_desired_count
  launch_type            = null # capacity_provider_strategy används istället
  enable_execute_command = true # för "aws ecs execute-command"-debug

  capacity_provider_strategy {
    capacity_provider = var.use_fargate_spot ? "FARGATE_SPOT" : "FARGATE"
    weight            = 1
    base              = 0
  }

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [var.ecs_security_group_id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = var.api_target_group_arn
    container_name   = "api"
    container_port   = var.api_container_port
  }

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  deployment_minimum_healthy_percent = 50
  deployment_maximum_percent         = 200

  # Vänta tills tasks är healthy innan service-skapning slutförs.
  wait_for_steady_state = false # för dev — höj till true i prod

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-api-service"
    Service = "api"
  })

  lifecycle {
    # Auto-scaling kan ändra desired_count efter initial deploy. Ignorera
    # drift så Terraform inte tar tillbaka skalnings-beslut.
    ignore_changes = [desired_count]
  }
}

# ---------------------------------------------------------------------------
# Service: Worker — ingen ALB-koppling.
# ---------------------------------------------------------------------------

resource "aws_ecs_service" "worker" {
  name                   = "${var.name_prefix}-worker"
  cluster                = aws_ecs_cluster.this.id
  task_definition        = aws_ecs_task_definition.worker.arn
  desired_count          = var.worker_desired_count
  launch_type            = null
  enable_execute_command = true

  capacity_provider_strategy {
    capacity_provider = var.use_fargate_spot ? "FARGATE_SPOT" : "FARGATE"
    weight            = 1
    base              = 0
  }

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [var.ecs_security_group_id]
    assign_public_ip = false
  }

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  deployment_minimum_healthy_percent = 0 # Worker tål total restart
  deployment_maximum_percent         = 200

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-worker-service"
    Service = "worker"
  })

  lifecycle {
    ignore_changes = [desired_count]
  }
}

# ---------------------------------------------------------------------------
# Auto-scaling — gated på var.enable_autoscaling.
# Default av i lean-dev (desired_count fastlåst). Slå på vid staging/prod.
# Policy: target-tracking på CPU 70%.
# ---------------------------------------------------------------------------

resource "aws_appautoscaling_target" "api" {
  count = var.enable_autoscaling ? 1 : 0

  service_namespace  = "ecs"
  scalable_dimension = "ecs:service:DesiredCount"
  resource_id        = "service/${aws_ecs_cluster.this.name}/${aws_ecs_service.api.name}"
  min_capacity       = var.api_min_capacity
  max_capacity       = var.api_max_capacity
}

resource "aws_appautoscaling_policy" "api_cpu" {
  count = var.enable_autoscaling ? 1 : 0

  name               = "${var.name_prefix}-api-cpu-tracking"
  policy_type        = "TargetTrackingScaling"
  service_namespace  = aws_appautoscaling_target.api[0].service_namespace
  scalable_dimension = aws_appautoscaling_target.api[0].scalable_dimension
  resource_id        = aws_appautoscaling_target.api[0].resource_id

  target_tracking_scaling_policy_configuration {
    target_value = 70.0

    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }

    scale_in_cooldown  = 300
    scale_out_cooldown = 60
  }
}

resource "aws_appautoscaling_target" "worker" {
  count = var.enable_autoscaling ? 1 : 0

  service_namespace  = "ecs"
  scalable_dimension = "ecs:service:DesiredCount"
  resource_id        = "service/${aws_ecs_cluster.this.name}/${aws_ecs_service.worker.name}"
  min_capacity       = var.worker_min_capacity
  max_capacity       = var.worker_max_capacity
}

resource "aws_appautoscaling_policy" "worker_cpu" {
  count = var.enable_autoscaling ? 1 : 0

  name               = "${var.name_prefix}-worker-cpu-tracking"
  policy_type        = "TargetTrackingScaling"
  service_namespace  = aws_appautoscaling_target.worker[0].service_namespace
  scalable_dimension = aws_appautoscaling_target.worker[0].scalable_dimension
  resource_id        = aws_appautoscaling_target.worker[0].resource_id

  target_tracking_scaling_policy_configuration {
    target_value = 70.0

    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }

    scale_in_cooldown  = 300
    scale_out_cooldown = 60
  }
}
