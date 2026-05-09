output "alb_arn" {
  value = aws_lb.this.arn
}

output "alb_dns_name" {
  description = "ALB-default-DNS (*.elb.amazonaws.com). Pekas mot via Route53 när domän finns."
  value       = aws_lb.this.dns_name
}

output "alb_zone_id" {
  description = "Hosted-zone-ID för ALB. Används i Route53 ALIAS-records."
  value       = aws_lb.this.zone_id
}

output "api_target_group_arn" {
  description = "Target group för Api-service. Används i ECS service `load_balancer`-block."
  value       = aws_lb_target_group.api.arn
}

output "http_listener_arn" {
  value = aws_lb_listener.http.arn
}

output "https_listener_arn" {
  value = var.https_listener_enabled ? aws_lb_listener.https[0].arn : null
}
