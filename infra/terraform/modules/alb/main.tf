# ---------------------------------------------------------------------------
# Application Load Balancer — internet-facing, lyssnar på publika subnets.
# HTTP-listener default; HTTPS adderas senare när domän + ACM-cert finns.
# Frontend = Vercel (BUILD.md §15.3) — ALB är Api-only.
# ---------------------------------------------------------------------------

resource "aws_lb" "this" {
  name               = "${var.name_prefix}-alb"
  internal           = false
  load_balancer_type = "application"
  security_groups    = [var.alb_security_group_id]
  subnets            = var.public_subnet_ids

  enable_deletion_protection = var.enable_deletion_protection

  # Drop ogiltiga headers innan vidarebefordran (defense mot HTTP smuggling).
  drop_invalid_header_fields = true

  # Idle-timeout: 60s default räcker för vanliga API-anrop. Höjs om
  # streaming/long-polling införs.
  idle_timeout = 60

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-alb"
  })
}

# ---------------------------------------------------------------------------
# Target group för Api-tasks (Fargate awsvpc-mode → target_type = ip).
# ---------------------------------------------------------------------------

resource "aws_lb_target_group" "api" {
  name        = "${var.name_prefix}-api-tg"
  vpc_id      = var.vpc_id
  port        = var.api_target_port
  protocol    = "HTTP"
  target_type = "ip"

  deregistration_delay = var.deregistration_delay_seconds

  health_check {
    enabled             = true
    path                = var.health_check_path
    protocol            = "HTTP"
    port                = "traffic-port"
    matcher             = "200"
    interval            = var.health_check_interval_seconds
    timeout             = var.health_check_timeout_seconds
    healthy_threshold   = var.healthy_threshold_count
    unhealthy_threshold = var.unhealthy_threshold_count
  }

  # Stickiness av — Api är stateless. Sessions sitter i Redis (ADR 0014, 0017).
  stickiness {
    type    = "lb_cookie"
    enabled = false
  }

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-api-tg"
    Service = "api"
  })

  lifecycle {
    create_before_destroy = true
  }
}

# ---------------------------------------------------------------------------
# Listeners
#
# HTTP-80 default-action:
#   - Om HTTPS aktivt: redirect 80 → 443 (säker default)
#   - Om inte (lean dev utan domän): forward direkt till api-target-group
# HTTPS-443 (gated på var.https_listener_enabled):
#   - Forward till api-target-group + ACM-cert
#
# När jobbpilot.se registreras + ACM-cert utfärdas: toggle https_listener_enabled
# = true + sätt acm_certificate_arn → HTTP redirectar automatiskt till HTTPS.
# ---------------------------------------------------------------------------

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.this.arn
  port              = 80
  protocol          = "HTTP"

  dynamic "default_action" {
    for_each = var.https_listener_enabled ? [1] : []
    content {
      type = "redirect"
      redirect {
        port        = "443"
        protocol    = "HTTPS"
        status_code = "HTTP_301"
      }
    }
  }

  dynamic "default_action" {
    for_each = var.https_listener_enabled ? [] : [1]
    content {
      type             = "forward"
      target_group_arn = aws_lb_target_group.api.arn
    }
  }

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-alb-http"
  })
}

resource "aws_lb_listener" "https" {
  count = var.https_listener_enabled ? 1 : 0

  load_balancer_arn = aws_lb.this.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = var.acm_certificate_arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-alb-https"
  })

  # Fail-fast vid plan-tid om operatör glömmer ACM-cert. Utan detta failar
  # apply silent på AWS-API-svaret (med kryptisk error). Pre-condition stoppar
  # vid plan så Klas ser felet innan resurser börjar skapas.
  lifecycle {
    precondition {
      condition     = var.acm_certificate_arn != null
      error_message = "var.acm_certificate_arn krävs när var.https_listener_enabled = true. Sätts efter ACM-cert-utfärdande för dev.jobbpilot.se (TD-30 / ADR 0026-trigger 1)."
    }
  }
}
