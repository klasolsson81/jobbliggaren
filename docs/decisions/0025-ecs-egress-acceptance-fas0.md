# ADR 0025 — ECS task egress accepterad som `0.0.0.0/0` under Fas 0

**Datum:** 2026-05-09
**Status:** Accepted
**Kontext:** STEG 13a — security-auditor Sec-Major-2 över `infra/terraform/modules/network/main.tf` ECS-egress-rule
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0023 (Worker-pipeline + Hangfire — HTTP-fri Worker), ADR 0024 D7 (logg-retention 30d), BUILD.md §8 (Bedrock cross-region inference), `docs/runbooks/aws-setup.md §3.1`

## Kontext

STEG 13a:s `network`-modul exponerar ett security-group (`${name_prefix}-ecs-sg`) som ECS Fargate-tasks (Api + Worker) kommer att tillhöra från och med STEG 13b. Egress-regeln på SG:n är initialt:

```hcl
resource "aws_vpc_security_group_egress_rule" "ecs_egress_all" {
  security_group_id = aws_security_group.ecs.id
  ip_protocol       = "-1"          # alla protokoll
  cidr_ipv4         = "0.0.0.0/0"   # internet via NAT
}
```

security-auditor STEG 13a (Sec-Major-2) flaggade att detta strider mot least-privilege:
- En komprometterad ECS-task kan exfiltrera data till valfri internet-host via NAT (DNS-tunnel, paste.ee, Discord-webhooks, raw TCP-sockets på godtyckliga portar).
- AWS Fargate-default är `0.0.0.0/0` egress, men "default är inte spec".

**Krafter som spelar in:**

1. **Bedrock cross-region inference går via NAT.** AWS Bedrock finns inte i `eu-north-1` — Api anropar EU inference profile-ARNs i `eu-central-1`/`eu-west-1` via cross-region. Bedrock VPC Endpoint är region-lokal och kan inte instansieras för en frånvarande tjänst. AWS-managed prefix-list för Bedrock egress finns inte heller. Egress-trafiken går via NAT mot AWS-managed cross-region IPs som **inte är stabila/dokumenterade som tillåtna mål**.

2. **GitHub Actions image pulls via NAT.** ECR finns i `eu-north-1` och har Gateway Endpoint via S3-VPCE i denna stack — men ECR Auth-token-anrop går via Interface Endpoint (krävs konfig i STEG 13b). Build-time GitHub Actions kan komma att hämta NuGet/npm-deps via NAT om image-byggen körs cloud-side i framtiden.

3. **Soloutvecklare + dev-miljö.** STEG 13a är dev-VPC:n. Ingen multi-tenant-yta, ingen externt accessibel admin-yta, ingen tredjeparts-integration än. Compromised-task-scenariot förutsätter att en attacker redan har kod-execution i Fargate — vilket i sig kräver en oberoende sårbarhet som denna ADR inte adresserar.

4. **ADR 0024 + STEG 12 redan minskar PII-exfil-värdet.** App-loggens IP/UA är /24-/48-anonymiserade (TD-22), audit-log är 90d retention med Art. 17-anonymisering, EmailHash är HMAC-defererad till Fas 2. Stulen prod-trafik från en komprometterad task innehåller mindre PII än ren rå databas-data — som ändå skulle gå via RDS-SG (5432-only), inte ECS-SG.

5. **Hardened egress = brittlare service-tillägg.** Varje nytt utgående protokoll kräver SG-rule — Sentry-telemetri, OpenTelemetry-collector, framtida tredjeparts-integration. Risk för dolda failures vid deploy ("varför fungerar inte X?" → glömde egress-rule).

## Beslut

ECS-SG egress accepteras som `0.0.0.0/0` `-1` (alla protokoll) **under Fas 0** (fram till och med Fas 1 prod-deploy / klass-launch via Fas 8).

Mitigation består av:

1. **Inga publika ingress-vägar förutom ALB:s 80/443** — ingress till ECS-tasks går bara från ALB-SG (referenced security group). Compromise-vektor måste alltså komma via Api:s applikationskod (Bedrock-prompt-injection, deserialization, etc.).

2. **PII-defense-in-depth via STEG 11–12 + ADR 0024** — IP-anonymisering, audit-anonymisering, EmailHash-defererad-HMAC, Art. 17-cascade. En komprometterad task som exfiltrerar app-logg-snippets bär mindre PII än rå database-snapshot.

3. **CloudTrail-monitoring** av ECS task-execution-roll (existerande baseline-stack). Avvikande Bedrock/Secrets Manager-API-kall i CloudTrail flaggas via budget/anomali-alerts.

4. **Encrypted transit hela vägen** — TLS för Bedrock, TLS för Secrets Manager VPCE, TLS för KMS VPCE, TLS för RDS (`rds.force_ssl=1`), TLS för Redis (transit_encryption_enabled). Rå TCP/UDP-exfil till tredjepart är möjlig via egress-rule men *innehåller* inte plaintext-data eftersom appen aldrig har den.

## Omvärderingstrigger

ADR 0025 omvärderas och kan supersedas vid någon av:

- **Fas 1 → Fas 2-övergång** (BUILD.md §18): Fas 2 introducerar JobTech-integration, första externa data-källan. Bredare attack-yta; egress bör hardeneras till explicit allow-list innan Platsbankens API-trafik börjar flöda. Hardening-spec finns i mitigation-väg nedan.
- **Multi-tenant-yta införs** (Fas 7+ extern beta): när andra användare än Klas själv har konton ökar incentivet för exfil-attack.
- **Compliance-krav** (DPA-villkor mot B2B-kunder, ISO27001-spår): least-privilege egress kan bli kontraktuellt krävt.
- **Konkret incident** (CloudTrail-anomaly, security-auditor-fynd, supply-chain-incident i NuGet/npm): trigger för omedelbar hardening utanför schema.

## Hardening-väg (förberedd för supersession)

När ADR 0025 supersedas, ersätt `aws_vpc_security_group_egress_rule.ecs_egress_all` med:

```hcl
# HTTPS only — blockera plaintext-exfil
resource "aws_vpc_security_group_egress_rule" "ecs_egress_https" {
  security_group_id = aws_security_group.ecs.id
  description       = "HTTPS till Bedrock, AWS APIs, externa integrationer"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
  cidr_ipv4         = "0.0.0.0/0"
}

# DNS via NAT
resource "aws_vpc_security_group_egress_rule" "ecs_egress_dns" {
  security_group_id = aws_security_group.ecs.id
  description       = "DNS-uppslag"
  ip_protocol       = "udp"
  from_port         = 53
  to_port           = 53
  cidr_ipv4         = "0.0.0.0/0"
}

# Postgres till RDS-SG
resource "aws_vpc_security_group_egress_rule" "ecs_egress_rds" {
  security_group_id            = aws_security_group.ecs.id
  description                  = "Postgres till RDS"
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  referenced_security_group_id = aws_security_group.rds.id
}

# Redis till Redis-SG
resource "aws_vpc_security_group_egress_rule" "ecs_egress_redis" {
  security_group_id            = aws_security_group.ecs.id
  description                  = "Redis till ElastiCache"
  ip_protocol                  = "tcp"
  from_port                    = 6379
  to_port                      = 6379
  referenced_security_group_id = aws_security_group.redis.id
}
```

Detta blockerar raw TCP/UDP-exfil på godtyckliga portar utan att kräva en lista över Bedrock-IPs (HTTPS 443 till `0.0.0.0/0` täcker dem). DNS via NAT behåller name-resolution. RDS/Redis-kommunikation går via referenced-SG (= principal-based, inte IP-based).

## Konsekvenser

### Positiva

- **Snabbare iteration i Fas 0** — nya outbound-integrationer (Sentry, OpenTelemetry, Bedrock cross-region) kräver ingen SG-ändring + apply-cykel
- **Inga dolda Fargate-failures** vid deploy där egress-rule skulle saknas
- **Bedrock cross-region-trafik fungerar utan IP-allow-list-skript** — inga AWS-managed prefix-lists för Bedrock-egress finns
- **Symmetri med AWS Fargate-default** — minskar avvikelse mot industri-praxis och dokumentation
- **Mitigerings-stack** (TLS + audit-anonymisering + IP-anonymisering + ALB-only-ingress) bär huvudbördan av dataskyddet

### Negativa

- **Compromised-task kan exfiltrera via raw TCP/UDP på godtycklig port** — DNS-tunnel, raw socket-exfil, command-and-control via standard-ports
- **Avvikelse från CIS AWS Foundations Benchmark** rule 5.x (least-privilege egress) — relevant vid framtida compliance-spår
- **ADR-skuld** — supersession kräver kod-ändring + apply, inte bara IAM-policy-tweak

### Mitigering

- Hardening-väg är förberedd och dokumenterad i denna ADR — supersession är enklare än ny design
- security-auditor-rapport `docs/reviews/2026-05-09-steg13a-security.md` Sec-Major-2 dokumenterar tillståndet och triggers
- CloudTrail-baseline (existerande prod-stack via `modules/cloudtrail/`) loggar all AWS-API-aktivitet — exfil via AWS-tjänst-anrop är synligt
- VPC Flow Logs är **inte** aktiva idag (defererad till STEG 13b eller Fas 1+) — när aktiverade ger de exfil-detektion även mot ej-AWS-mål

## Alternativ övervägda

### Alt B — Hardena egress redan i STEG 13a

Som beskrivet i Hardening-väg ovan. Avvisat eftersom:
- Bedrock cross-region-IPs är inte stabila/listade — `0.0.0.0/0:443` täcker dem ändå, så det enda som hardenas är raw TCP/UDP på portar !=443/53/5432/6379
- Marginal säkerhetsvinst (TLS + ALB-only-ingress + audit-stack) jämfört med iterations-friktion i Fas 0
- Risken för "varför fungerar inte X?"-felsökning vid deploy-tid utan att tjäna säkerhets-värde i en miljö där bara Klas själv har konto

### Alt C — Egress till en explicit lista AWS-prefix + 443/0.0.0.0/0

Hämta AWS-managed prefix-lists för S3, Bedrock, Secrets Manager via `aws ec2 get-managed-prefix-list-entries` + scriptad SG-population. Avvisat eftersom:
- Bedrock har ingen managed prefix-list (verifierat 2026-05-09)
- Prefix-listan ändras dynamiskt — kräver sched script + state-drift mot Terraform
- Komplexitet vida över säkerhetsvinsten

### Alt D — VPC PrivateLink-peering till eu-central-1 för Bedrock

PrivateLink över region-gränser blev möjligt 2024 men kräver DTS-cost + provider-side acceptance. Avvisat eftersom Klas-budget #9 prioriterar minimering av AWS-kostnad i Fas 0 — DTS-cost för PrivateLink-trafik är högre än NAT Gateway data-processing.

## Implementationsstatus

- ✅ ECS-SG egress = `0.0.0.0/0` `-1` i `infra/terraform/modules/network/main.tf:208-213` (STEG 13a)
- ✅ Hardening-väg dokumenterad i denna ADR
- ✅ security-auditor Sec-Major-2 i `docs/reviews/2026-05-09-steg13a-security.md` accepterad
- ⏳ Omvärdering vid Fas 1 → Fas 2-övergång (när BUILD.md §18 Fas 2 påbörjas)
- ⏳ VPC Flow Logs aktivering (separat task, ej ADR 0025-scope)

## Validering

ADR omvärderas vid:
- Sec-major-fynd som direkt utnyttjar denna egress-yta
- Fas 2 prereq-stängning (ADR 0005-relaterad)
- Multi-tenant-yta införande
- B2B-kontraktuellt least-privilege-krav
