output "state_bucket_name" {
  description = "S3 bucket för Terraform remote state."
  value       = aws_s3_bucket.state.bucket
}

output "state_bucket_arn" {
  value = aws_s3_bucket.state.arn
}

output "lock_table_name" {
  description = "DynamoDB-tabell för Terraform state locks."
  value       = aws_dynamodb_table.lock.name
}

output "region" {
  value = var.aws_region
}
