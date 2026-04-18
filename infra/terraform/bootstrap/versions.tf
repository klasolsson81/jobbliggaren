terraform {
  required_version = ">= 1.14.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.80"
    }
  }

  # Bootstrap använder LOKAL state. Migrera inte in i den egna bucketen
  # — cirkulärt beroende vid nedplockning. Lokal state är gitignored.
}
