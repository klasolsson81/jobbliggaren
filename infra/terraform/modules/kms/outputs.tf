output "master_key_id" {
  value = aws_kms_key.master.key_id
}

output "master_key_arn" {
  value = aws_kms_key.master.arn
}

output "master_key_alias" {
  value = aws_kms_alias.master.name
}

output "byok_key_id" {
  value = aws_kms_key.byok.key_id
}

output "byok_key_arn" {
  value = aws_kms_key.byok.arn
}

output "byok_key_alias" {
  value = aws_kms_alias.byok.name
}
