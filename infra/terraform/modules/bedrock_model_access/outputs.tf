output "bedrock_invoke_policy_arn" {
  description = "ARN till IAM-policyn som tillåter `bedrock:Converse*` + `bedrock:InvokeModel*` mot EU-profilen."
  value       = aws_iam_policy.bedrock_invoke.arn
}

output "bedrock_invoke_policy_name" {
  value = aws_iam_policy.bedrock_invoke.name
}

output "eu_inference_profile_ids" {
  description = "EU inference profile-IDs som policyn täcker."
  value       = var.eu_inference_profile_ids
}
