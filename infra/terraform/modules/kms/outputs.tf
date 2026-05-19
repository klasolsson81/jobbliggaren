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

output "td13_field_key_id" {
  value = aws_kms_key.td13_field.key_id
}

output "td13_field_key_arn" {
  value = aws_kms_key.td13_field.arn
}

output "td13_field_key_alias" {
  value = aws_kms_alias.td13_field.name
}
