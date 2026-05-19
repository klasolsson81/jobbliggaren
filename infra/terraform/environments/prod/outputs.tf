output "kms_master_key_arn" {
  value = module.kms.master_key_arn
}

output "kms_master_key_alias" {
  value = module.kms.master_key_alias
}

output "kms_byok_key_arn" {
  value = module.kms.byok_key_arn
}

output "kms_byok_key_alias" {
  value = module.kms.byok_key_alias
}

output "kms_td13_field_key_arn" {
  value = module.kms.td13_field_key_arn
}

output "kms_td13_field_key_alias" {
  value = module.kms.td13_field_key_alias
}

output "cloudtrail_name" {
  value = module.cloudtrail.trail_name
}

output "cloudtrail_log_bucket" {
  value = module.cloudtrail.log_bucket_name
}

output "secret_arns" {
  value = module.secrets_manager.secret_arns
}

output "bedrock_invoke_policy_arn" {
  value = module.bedrock_model_access.bedrock_invoke_policy_arn
}

# ---------------------------------------------------------------------------
# Route53 — apex hosted zone (delad resurs)
# ---------------------------------------------------------------------------

output "route53_zone_id" {
  description = "Hosted-zone-ID för apex-domänen. Konsumeras av dev/staging/prod-stacks via `data \"aws_route53_zone\"`-lookup."
  value       = module.route53.zone_id
}

output "route53_domain_name" {
  description = "Apex-domän (utan trailing dot)."
  value       = module.route53.domain_name
}

output "route53_name_servers" {
  description = "4 NS-records — kopiera till registrar för delegering till AWS."
  value       = module.route53.name_servers
}

# ---------------------------------------------------------------------------
# GitHub OIDC (STEG 14a)
# ---------------------------------------------------------------------------

output "github_oidc_provider_arn" {
  description = "OIDC-providerns ARN. Delas av alla framtida deploy-roller (staging/prod)."
  value       = module.github_oidc.oidc_provider_arn
}

output "github_actions_deploy_dev_role_arn" {
  description = "ARN för dev-deploy-rollen. Sätts som GitHub Actions Secret AWS_DEPLOY_ROLE_ARN via `gh secret set AWS_DEPLOY_ROLE_ARN -R <owner>/<repo>` efter apply."
  value       = module.github_oidc.deploy_dev_role_arn
}

# ---------------------------------------------------------------------------
# Vercel DNS (frontend-host) — verifiera mot Vercel Domains-vy efter apply
# ---------------------------------------------------------------------------

output "vercel_dns_records" {
  description = "Vercel DNS-records för jobbpilot.se + www. Verifiera mot Vercel Domains-vy att 'Invalid Configuration' försvinner efter DNS-propagering (5-30 min)."
  value = {
    apex_a = {
      name  = aws_route53_record.vercel_apex.name
      type  = aws_route53_record.vercel_apex.type
      value = aws_route53_record.vercel_apex.records
      ttl   = aws_route53_record.vercel_apex.ttl
    }
    www_cname = {
      name  = aws_route53_record.vercel_www.name
      type  = aws_route53_record.vercel_www.type
      value = aws_route53_record.vercel_www.records
      ttl   = aws_route53_record.vercel_www.ttl
    }
    apex_caa = {
      name  = aws_route53_record.caa.name
      type  = aws_route53_record.caa.type
      value = aws_route53_record.caa.records
      ttl   = aws_route53_record.caa.ttl
    }
  }
}
