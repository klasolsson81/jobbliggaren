terraform {
  backend "s3" {
    bucket         = "jobbpilot-terraform-state-710427215829"
    key            = "dev/main.tfstate"
    region         = "eu-north-1"
    dynamodb_table = "jobbpilot-terraform-locks"
    encrypt        = true
  }
}
