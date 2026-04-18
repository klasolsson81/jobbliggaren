# Terraform bootstrap — JobbPilot

Engångs-stack som skapar S3-bucket + DynamoDB-tabell som resten av Terraform
använder för remote state.

## Körning

```bash
cd infra/terraform/bootstrap
export AWS_PROFILE=jobbpilot-bootstrap   # eller --profile-flaggan per kommando
terraform init
terraform plan
terraform apply
```

## Varför lokal state?

Bootstrap skapar bucket och lock-tabell som är *fjärr-state för andra stackar*.
Om bootstrap själv skulle använda sin egen bucket → cirkulärt beroende vid
nedplockning. Lokal `terraform.tfstate` är gitignored.

## Vad som skapas

| Resurs | Detalj |
|--------|--------|
| `aws_s3_bucket.state` | `jobbpilot-terraform-state-710427215829` |
| `aws_s3_bucket_versioning.state` | Enabled |
| `aws_s3_bucket_server_side_encryption_configuration.state` | AES256 |
| `aws_s3_bucket_public_access_block.state` | All 4 block-flags true |
| `aws_s3_bucket_lifecycle_configuration.state` | Noncurrent 90d, abort multipart 7d |
| `aws_s3_bucket_policy.state` | Neka `aws:SecureTransport=false` |
| `aws_dynamodb_table.lock` | `jobbpilot-terraform-locks`, PAY_PER_REQUEST, PITR på |

## Efter apply

Verifiera outputs, kopiera bucket-namn till
[`../environments/prod/backend.tf`](../environments/prod/backend.tf) om du
ändrar konstanter.
