# ---------------------------------------------------------------------------
# ECR repositories — en per service (api, worker).
# Image-encryption via KMS om kms_key_id sätts; annars AES256-default.
# ---------------------------------------------------------------------------

resource "aws_ecr_repository" "this" {
  for_each = toset(var.repository_names)

  name                 = "${var.name_prefix}-${each.key}"
  image_tag_mutability = var.image_tag_mutability

  image_scanning_configuration {
    scan_on_push = var.scan_on_push
  }

  encryption_configuration {
    encryption_type = var.kms_key_id == null ? "AES256" : "KMS"
    kms_key         = var.kms_key_id
  }

  tags = merge(var.tags, {
    Name    = "${var.name_prefix}-${each.key}"
    Service = each.key
  })
}

# ---------------------------------------------------------------------------
# Lifecycle policy — behåll bara senaste N images. Förhindrar storage-spillover.
# ---------------------------------------------------------------------------

resource "aws_ecr_lifecycle_policy" "this" {
  for_each = aws_ecr_repository.this

  repository = each.value.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Keep last ${var.keep_last_n_images} images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = var.keep_last_n_images
        }
        action = {
          type = "expire"
        }
      }
    ]
  })
}
