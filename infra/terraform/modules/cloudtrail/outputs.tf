output "trail_name" {
  value = aws_cloudtrail.audit.name
}

output "trail_arn" {
  value = aws_cloudtrail.audit.arn
}

output "log_bucket_name" {
  value = aws_s3_bucket.trail_logs.bucket
}

output "log_bucket_arn" {
  value = aws_s3_bucket.trail_logs.arn
}
