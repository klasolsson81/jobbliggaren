# ---------------------------------------------------------------------------
# Networking
# ---------------------------------------------------------------------------

output "vpc_id" {
  value = module.network.vpc_id
}

output "vpc_cidr" {
  value = module.network.vpc_cidr
}

output "public_subnet_ids" {
  value = module.network.public_subnet_ids
}

output "private_subnet_ids" {
  value = module.network.private_subnet_ids
}

output "alb_security_group_id" {
  value = module.network.alb_security_group_id
}

output "ecs_security_group_id" {
  value = module.network.ecs_security_group_id
}

# ---------------------------------------------------------------------------
# Databas
# ---------------------------------------------------------------------------

output "rds_endpoint" {
  description = "RDS endpoint host:port."
  value       = module.rds.endpoint
}

output "rds_address" {
  value = module.rds.address
}

output "rds_port" {
  value = module.rds.port
}

output "rds_db_name" {
  value = module.rds.db_name
}

output "rds_master_user_secret_arn" {
  description = "AWS-managed Secrets Manager-secret med master-password (auto-roterad). Används bara för DDL-init."
  value       = module.rds.master_user_secret_arn
  sensitive   = true
}

# ---------------------------------------------------------------------------
# Cache
# ---------------------------------------------------------------------------

output "redis_primary_endpoint_address" {
  value = module.redis.primary_endpoint_address
}

output "redis_reader_endpoint_address" {
  value = module.redis.reader_endpoint_address
}

output "redis_port" {
  value = module.redis.port
}

output "redis_auth_token_secret_arn" {
  description = "Secrets Manager-secret med Redis AUTH-token."
  value       = module.redis.auth_token_secret_arn
  sensitive   = true
}

# ---------------------------------------------------------------------------
# Dev-secrets (placeholder, sätts post-DDL)
# ---------------------------------------------------------------------------

output "db_app_connection_secret_arn" {
  value = aws_secretsmanager_secret.db_app_connection.arn
}

output "db_hangfire_connection_secret_arn" {
  value = aws_secretsmanager_secret.db_hangfire_connection.arn
}

# ---------------------------------------------------------------------------
# STEG 13b — container-infra
# ---------------------------------------------------------------------------

output "ecr_api_repository_url" {
  description = "ECR-repo-URL för Api. Används vid `docker push` (kräver `aws ecr get-login-password` först)."
  value       = module.ecr.repository_urls["api"]
}

output "ecr_worker_repository_url" {
  description = "ECR-repo-URL för Worker."
  value       = module.ecr.repository_urls["worker"]
}

output "alb_dns_name" {
  description = "ALB-default-DNS. Pekas mot via Route53 ALIAS-record när domän registreras (jobbpilot.se → ALB)."
  value       = module.alb.alb_dns_name
}

output "alb_url_http" {
  description = "Klickbart HTTP-URL till Api via ALB. Svarar på /api/ready, /api/v1/* etc."
  value       = "http://${module.alb.alb_dns_name}"
}

output "ecs_cluster_name" {
  value = module.ecs.cluster_name
}

output "ecs_api_service_name" {
  value = module.ecs.api_service_name
}

output "ecs_worker_service_name" {
  value = module.ecs.worker_service_name
}

output "ecs_execution_role_arn" {
  value = module.iam_ecs.execution_role_arn
}

output "ecs_task_api_role_arn" {
  value = module.iam_ecs.task_api_role_arn
}

output "ecs_task_worker_role_arn" {
  value = module.iam_ecs.task_worker_role_arn
}

# ---------------------------------------------------------------------------
# STEG 13c — DNS + TLS
# ---------------------------------------------------------------------------

output "dev_fqdn" {
  description = "Full DNS-namn för dev-miljöns Api (ALIAS → ALB)."
  value       = "${var.dev_subdomain}.${var.apex_domain_name}"
}

output "acm_dev_certificate_arn" {
  description = "Validerad ACM-cert-ARN för dev.<apex>. Kopiera till terraform.tfvars som alb_acm_certificate_arn samtidigt som alb_https_enabled flippas till true."
  value       = module.acm_dev.certificate_arn
}

output "dev_url_https" {
  description = "HTTPS-URL till Api-via-ALB. Fungerar först efter alb_https_enabled flippats."
  value       = "https://${var.dev_subdomain}.${var.apex_domain_name}"
}

# ---------------------------------------------------------------------------
# STEG 14b — Migrate one-shot DDL-init
# ---------------------------------------------------------------------------

output "ecr_migrate_repository_url" {
  description = "ECR-repo-URL för Migrate. Används vid `docker push` av migrate-image."
  value       = module.ecr.repository_urls["migrate"]
}

output "ecs_task_migrate_role_arn" {
  description = "Task-role för Migrate (PutSecretValue på app+hangfire-secret + GetSecretValue på master). Tom om migrate-rollen ej skapats."
  value       = module.iam_ecs.task_migrate_role_arn
}

output "migrate_run_task_command" {
  description = "Färdigt aws ecs run-task-kommando för Migrate-task. Operatör copy-paste:ar utan att slå upp subnets/SG manuellt. Kräver att migrate_image_tag är satt + Phase 2-apply kört."
  value = module.ecs.migrate_task_definition_family != "" ? format(
    "aws ecs run-task --cluster %s --task-definition %s --launch-type FARGATE --network-configuration 'awsvpcConfiguration={subnets=[%s],securityGroups=[%s],assignPublicIp=DISABLED}' --region %s --profile jobbpilot",
    module.ecs.cluster_name,
    module.ecs.migrate_task_definition_family,
    join(",", module.network.private_subnet_ids),
    module.network.ecs_security_group_id,
    var.aws_region
  ) : "Migrate-task-def ej skapad — sätt migrate_image_tag i tfvars och apply."
}
