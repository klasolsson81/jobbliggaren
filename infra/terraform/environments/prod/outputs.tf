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
