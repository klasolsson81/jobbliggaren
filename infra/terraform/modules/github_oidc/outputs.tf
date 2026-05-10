output "oidc_provider_arn" {
  description = "OIDC-providerns ARN. Återanvänds av framtida deploy-roller (staging/prod)."
  value       = aws_iam_openid_connect_provider.github.arn
}

output "deploy_dev_role_arn" {
  description = "ARN för dev-deploy-rollen. Sätts som GitHub Actions Secret AWS_DEPLOY_ROLE_ARN."
  value       = aws_iam_role.deploy_dev.arn
}

output "deploy_dev_role_name" {
  description = "Namn på dev-deploy-rollen (utan ARN-prefix). Användbart i CLI-debug."
  value       = aws_iam_role.deploy_dev.name
}
