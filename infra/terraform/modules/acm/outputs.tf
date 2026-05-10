output "certificate_arn" {
  description = "Validerad ACM-cert-ARN. Refererar `aws_acm_certificate_validation` så konsumenter inte plockar upp ovaliderad cert."
  value       = aws_acm_certificate_validation.this.certificate_arn
}

output "domain_name" {
  description = "Primary domain (CN) på certet."
  value       = aws_acm_certificate.this.domain_name
}

output "subject_alternative_names" {
  description = "Alla SANs på certet (inklusive CN)."
  value       = aws_acm_certificate.this.subject_alternative_names
}
