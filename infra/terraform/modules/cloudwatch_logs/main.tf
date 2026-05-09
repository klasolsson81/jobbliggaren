# ---------------------------------------------------------------------------
# CloudWatch LogGroups för ECS-task-output + ecs-exec-sessions.
# Explicit deklaration hindrar AWS-default "Never expire" som bryter ADR 0024 D7.
# Path-konvention: /aws/ecs/<name_prefix>/<service>
# ---------------------------------------------------------------------------

resource "aws_cloudwatch_log_group" "this" {
  for_each = toset(var.log_group_names)

  name              = "/aws/ecs/${var.name_prefix}/${each.key}"
  retention_in_days = var.retention_in_days
  kms_key_id        = var.kms_key_id

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-${each.key}-log"
    Service = each.key
    Purpose = "ecs-log"
  })
}
