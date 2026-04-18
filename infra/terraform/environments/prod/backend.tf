terraform {
  backend "s3" {
    bucket         = "jobbpilot-terraform-state-710427215829"
    key            = "prod/baseline.tfstate"
    region         = "eu-north-1"
    dynamodb_table = "jobbpilot-terraform-locks"
    encrypt        = true
  }
}
