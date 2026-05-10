# ---------------------------------------------------------------------------
# ACM-cert med DNS-validering via Route53.
#
# Auto-renewal sker så länge validation-CNAMEs ligger kvar i Route53. Inga
# manuella förnyelse-steg krävs.
#
# Region: certet skapas i providerns aktiva region. För ALB i eu-north-1 →
# kör denna modul från eu-north-1-stack. CloudFront-cert kräver us-east-1 →
# instansiera modul med separat aliased provider. Inte aktuellt i Fas 0.
# ---------------------------------------------------------------------------

resource "aws_acm_certificate" "this" {
  domain_name               = var.domain_name
  subject_alternative_names = var.subject_alternative_names
  validation_method         = "DNS"

  # ACM tillåter inte mutation av CN — replace istället. create_before_destroy
  # förhindrar listener-downtime vid cert-byte.
  lifecycle {
    create_before_destroy = true
  }

  tags = merge(var.tags, {
    Name = var.domain_name
  })
}

# ---------------------------------------------------------------------------
# Route53 CNAME-records som bevisar domän-ägarskap för ACM.
# `for_each` över domain_validation_options hanterar både CN + SANs (varje
# domän får egen unique CNAME). `allow_overwrite=true` skyddar mot kollision
# om samma name råkar dyka upp för flera SANs (sker inte i normalfall, men
# AWS-pattern rekommenderar flaggan).
# ---------------------------------------------------------------------------

resource "aws_route53_record" "validation" {
  for_each = {
    for dvo in aws_acm_certificate.this.domain_validation_options : dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  }

  allow_overwrite = true
  name            = each.value.name
  records         = [each.value.record]
  ttl             = 60
  type            = each.value.type
  zone_id         = var.route53_zone_id
}

# ---------------------------------------------------------------------------
# Blockerar plan/apply tills ACM observerat validation-CNAME och utfärdat
# certet. Typiskt 5-30 min. Konsumenter (ALB) ska referera
# `aws_acm_certificate_validation.this.certificate_arn` (inte direkt
# `aws_acm_certificate.this.arn`) så att downstream-resurser inte plockar
# upp en ovaliderad cert-ARN.
# ---------------------------------------------------------------------------

resource "aws_acm_certificate_validation" "this" {
  certificate_arn         = aws_acm_certificate.this.arn
  validation_record_fqdns = [for r in aws_route53_record.validation : r.fqdn]

  # Default-timeout är 45 min. Svenska registrarer (Loopia/Inleed/Binero) kan
  # ha långsam NS-propagering — höjt till 75 min för marginal (security-auditor
  # Sec-Minor-3 STEG 13c). Om validering hänger >75 min: kontrollera `dig NS
  # <apex> +short` returnerar AWS-NS innan ny apply (registrar-delegering ofta
  # orsaken).
  timeouts {
    create = "75m"
  }
}
