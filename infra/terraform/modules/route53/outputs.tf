output "zone_id" {
  description = "Route53 zone-ID. Används i `aws_route53_record` + `aws_acm_certificate validation`."
  value       = aws_route53_zone.this.zone_id
}

output "name_servers" {
  description = "4 NS-records att kopiera till registrar för delegering till AWS Route53."
  value       = aws_route53_zone.this.name_servers
}

output "domain_name" {
  description = "Apex-domän (utan trailing dot)."
  value       = aws_route53_zone.this.name
}
