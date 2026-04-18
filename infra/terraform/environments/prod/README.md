# Terraform prod environment — JobbPilot

Baseline-stack för AWS-kontot. Sätter upp:

- Budgets (zero-spend + $50/månads-budget med alerts)
- CloudTrail (multi-region, log file validation)
- KMS — `jobbpilot-master-key` + `jobbpilot-byok-key`
- Secrets Manager — placeholder-secrets för kommande appar
- IAM-policy för Bedrock `Converse*` mot EU inference profile

## Förkrav

- Bootstrap-stacken är applied (`infra/terraform/bootstrap/`).
- `AWS_PROFILE=jobbpilot` (SSO) eller `--profile jobbpilot` per kommando.
- SSO-session aktiv: `aws sso login --profile jobbpilot` vid behov.

## Körning

```bash
cd infra/terraform/environments/prod
export AWS_PROFILE=jobbpilot
terraform init
terraform plan -out=plan.out
terraform apply plan.out
```

## Verifiering efter apply

```bash
aws budgets describe-budgets --account-id 710427215829 --profile jobbpilot
aws cloudtrail describe-trails --profile jobbpilot
aws kms list-aliases --profile jobbpilot | grep jobbpilot
aws secretsmanager list-secrets --profile jobbpilot
aws iam list-policies --scope Local --profile jobbpilot | grep JobbPilot
```

## Efter Bedrock model-access-approval

1. Kör `aws bedrock list-inference-profiles --region eu-central-1 --profile jobbpilot`.
2. Uppdatera `var.eu_inference_profile_ids` i [`terraform.tfvars`](./terraform.tfvars) om ID:n skiljer sig.
3. `terraform apply` igen — bara IAM-policyn ändras.
