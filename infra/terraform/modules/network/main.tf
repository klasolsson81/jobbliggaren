data "aws_availability_zones" "available" {
  state = "available"
}

data "aws_region" "current" {}

locals {
  azs       = slice(data.aws_availability_zones.available.names, 0, var.az_count)
  nat_count = var.single_nat_gateway ? 1 : var.az_count
}

# ---------------------------------------------------------------------------
# VPC
# ---------------------------------------------------------------------------

resource "aws_vpc" "this" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-vpc"
  })
}

# ---------------------------------------------------------------------------
# Internet Gateway (för publika subnets)
# ---------------------------------------------------------------------------

resource "aws_internet_gateway" "this" {
  vpc_id = aws_vpc.this.id

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-igw"
  })
}

# ---------------------------------------------------------------------------
# Subnets
# ---------------------------------------------------------------------------

resource "aws_subnet" "public" {
  count = var.az_count

  vpc_id                  = aws_vpc.this.id
  cidr_block              = var.public_subnet_cidrs[count.index]
  availability_zone       = local.azs[count.index]
  map_public_ip_on_launch = true

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-public-${local.azs[count.index]}"
    Tier = "public"
  })
}

resource "aws_subnet" "private" {
  count = var.az_count

  vpc_id                  = aws_vpc.this.id
  cidr_block              = var.private_subnet_cidrs[count.index]
  availability_zone       = local.azs[count.index]
  map_public_ip_on_launch = false

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-private-${local.azs[count.index]}"
    Tier = "private"
  })
}

# ---------------------------------------------------------------------------
# NAT Gateway — single (cost-optimized) eller per-AZ
# ---------------------------------------------------------------------------

resource "aws_eip" "nat" {
  count  = local.nat_count
  domain = "vpc"

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-nat-eip-${count.index}"
  })

  depends_on = [aws_internet_gateway.this]
}

resource "aws_nat_gateway" "this" {
  count = local.nat_count

  allocation_id = aws_eip.nat[count.index].id
  subnet_id     = aws_subnet.public[count.index].id

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-nat-${count.index}"
  })

  depends_on = [aws_internet_gateway.this]
}

# ---------------------------------------------------------------------------
# Route tables
# ---------------------------------------------------------------------------

# Publik route-tabell — alla publika subnets pekar på IGW.
resource "aws_route_table" "public" {
  vpc_id = aws_vpc.this.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.this.id
  }

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-rt-public"
  })
}

resource "aws_route_table_association" "public" {
  count = var.az_count

  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

# Privata route-tabeller — en per AZ när Multi-NAT (HA), annars en delad.
resource "aws_route_table" "private" {
  count  = var.az_count
  vpc_id = aws_vpc.this.id

  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.this[var.single_nat_gateway ? 0 : count.index].id
  }

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-rt-private-${local.azs[count.index]}"
  })
}

resource "aws_route_table_association" "private" {
  count = var.az_count

  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private[count.index].id
}

# ---------------------------------------------------------------------------
# Security groups
# ---------------------------------------------------------------------------

# ALB — internet-facing, accepterar HTTPS från 0.0.0.0/0 + HTTP för redirect.
resource "aws_security_group" "alb" {
  name        = "${var.name_prefix}-alb"
  description = "ALB ingress (80/443 från internet)."
  vpc_id      = aws_vpc.this.id

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-alb-sg"
  })
}

resource "aws_vpc_security_group_ingress_rule" "alb_https" {
  security_group_id = aws_security_group.alb.id
  description       = "HTTPS från internet"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
  cidr_ipv4         = "0.0.0.0/0"
}

resource "aws_vpc_security_group_ingress_rule" "alb_http" {
  security_group_id = aws_security_group.alb.id
  description       = "HTTP från internet (redirect till HTTPS i ALB-listener)"
  ip_protocol       = "tcp"
  from_port         = 80
  to_port           = 80
  cidr_ipv4         = "0.0.0.0/0"
}

resource "aws_vpc_security_group_egress_rule" "alb_egress" {
  security_group_id = aws_security_group.alb.id
  description       = "Egress till ECS-tasks i VPC"
  ip_protocol       = "-1"
  cidr_ipv4         = var.vpc_cidr
}

# ECS — accepterar bara från ALB-SG.
resource "aws_security_group" "ecs" {
  name        = "${var.name_prefix}-ecs"
  description = "ECS Fargate tasks (Api + Worker). Ingress från ALB-SG."
  vpc_id      = aws_vpc.this.id

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-ecs-sg"
  })
}

resource "aws_vpc_security_group_ingress_rule" "ecs_from_alb" {
  security_group_id            = aws_security_group.ecs.id
  description                  = "Trafik från ALB"
  ip_protocol                  = "tcp"
  from_port                    = 8080
  to_port                      = 8080
  referenced_security_group_id = aws_security_group.alb.id
}

# Worker har ingen ingress (HTTP-fri per ADR 0023). All ingress sker via ECS-SG
# men Worker-task lyssnar inte på portar; ALB target group registrerar bara Api-tasks.

resource "aws_vpc_security_group_egress_rule" "ecs_egress_all" {
  security_group_id = aws_security_group.ecs.id
  description       = "Egress till RDS, Redis, VPCE, internet via NAT"
  ip_protocol       = "-1"
  cidr_ipv4         = "0.0.0.0/0"
}

# RDS — accepterar 5432 bara från ECS-SG.
resource "aws_security_group" "rds" {
  name        = "${var.name_prefix}-rds"
  description = "RDS Postgres. Ingress 5432 från ECS-SG."
  vpc_id      = aws_vpc.this.id

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-rds-sg"
  })
}

resource "aws_vpc_security_group_ingress_rule" "rds_from_ecs" {
  security_group_id            = aws_security_group.rds.id
  description                  = "Postgres från ECS-tasks"
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  referenced_security_group_id = aws_security_group.ecs.id
}

# Redis — accepterar 6379 bara från ECS-SG.
resource "aws_security_group" "redis" {
  name        = "${var.name_prefix}-redis"
  description = "ElastiCache. Ingress 6379 från ECS-SG."
  vpc_id      = aws_vpc.this.id

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-redis-sg"
  })
}

resource "aws_vpc_security_group_ingress_rule" "redis_from_ecs" {
  security_group_id            = aws_security_group.redis.id
  description                  = "Redis/Valkey från ECS-tasks"
  ip_protocol                  = "tcp"
  from_port                    = 6379
  to_port                      = 6379
  referenced_security_group_id = aws_security_group.ecs.id
}

# VPC Endpoints — accepterar 443 bara från ECS-SG.
resource "aws_security_group" "vpc_endpoints" {
  count = var.enable_interface_endpoints ? 1 : 0

  name        = "${var.name_prefix}-vpce"
  description = "Interface VPC Endpoints. Ingress 443 från ECS-SG."
  vpc_id      = aws_vpc.this.id

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-vpce-sg"
  })
}

resource "aws_vpc_security_group_ingress_rule" "vpce_from_ecs" {
  count = var.enable_interface_endpoints ? 1 : 0

  security_group_id            = aws_security_group.vpc_endpoints[0].id
  description                  = "HTTPS från ECS till VPC endpoints"
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
  referenced_security_group_id = aws_security_group.ecs.id
}

# ---------------------------------------------------------------------------
# VPC Endpoints
# ---------------------------------------------------------------------------

# S3 Gateway endpoint — gratis, used för ECR-image-layers + S3-data-access.
resource "aws_vpc_endpoint" "s3" {
  count = var.enable_s3_endpoint ? 1 : 0

  vpc_id            = aws_vpc.this.id
  service_name      = "com.amazonaws.${data.aws_region.current.name}.s3"
  vpc_endpoint_type = "Gateway"
  route_table_ids   = aws_route_table.private[*].id

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-vpce-s3"
  })
}

# Secrets Manager Interface endpoint — Api/Worker hämtar ConnectionStrings.
resource "aws_vpc_endpoint" "secretsmanager" {
  count = var.enable_interface_endpoints ? 1 : 0

  vpc_id              = aws_vpc.this.id
  service_name        = "com.amazonaws.${data.aws_region.current.name}.secretsmanager"
  vpc_endpoint_type   = "Interface"
  subnet_ids          = aws_subnet.private[*].id
  security_group_ids  = [aws_security_group.vpc_endpoints[0].id]
  private_dns_enabled = true

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-vpce-secretsmanager"
  })
}

# KMS Interface endpoint — Decrypt vid Secrets Manager-getValue + RDS/Redis-access.
resource "aws_vpc_endpoint" "kms" {
  count = var.enable_interface_endpoints ? 1 : 0

  vpc_id              = aws_vpc.this.id
  service_name        = "com.amazonaws.${data.aws_region.current.name}.kms"
  vpc_endpoint_type   = "Interface"
  subnet_ids          = aws_subnet.private[*].id
  security_group_ids  = [aws_security_group.vpc_endpoints[0].id]
  private_dns_enabled = true

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-vpce-kms"
  })
}

# OBS: Bedrock VPC endpoint utelämnas. Bedrock-tjänsten finns ej i eu-north-1
# (cross-region inference går till eu-central-1/eu-west-1) — ingen lokal endpoint
# möjlig. Trafiken går via NAT. Vid framtida region-byte: lägg till
# com.amazonaws.<region>.bedrock-runtime här.
