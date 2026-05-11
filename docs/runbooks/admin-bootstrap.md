# Admin-bootstrap — prod-konfig-källa

**Källa:** TD-50 (security-auditor, Fas 1-stängning admin-audit 2026-05-11)
**Relaterad:** [ADR 0028 — Admin authorization defense-in-depth](../decisions/0028-admin-authorization-marker-interface-defense-in-depth.md)

`IdempotentAdminRoleSeeder` (i `JobbPilot.Infrastructure.Identity`) skapar
Admin-rollen vid container-uppstart om den saknas och tilldelar den till
användaren vars email matchar `AdminBootstrap:InitialAdminEmail`. Värdet är
**säkerhetskänsligt** — emailen identifierar vem som får admin-yta i prod.

## Konfig-källa per miljö

| Miljö | Källa | Värde-exempel | Var sätts det |
|-------|-------|---------------|---------------|
| Local dev | `appsettings.Local.json` (gitignored) | `klas@jobbpilot.test` | Manuell fil på din maskin |
| Dev (AWS) | `appsettings.Development.json` eller AWS Secrets Manager | `klas@jobbpilot.se` | committat eller Secrets |
| Staging | AWS Secrets Manager | `klas@jobbpilot.se` | ECS task-def env-var |
| **Prod** | **AWS Secrets Manager + ECS task-def env-var** | `klas@jobbpilot.se` | **ENDAST via Secrets — aldrig i fil** |

## Förbudet: aldrig i `appsettings.json`

**Aldrig** committa `AdminBootstrap:InitialAdminEmail` med ett riktigt värde
till git, vare sig i `appsettings.json`, `appsettings.Production.json`, eller
någon overlay. Skäl:

1. **PII i git-historiken** — emailen är personlig och kan inte raderas i
   efterhand utan full history-rewrite.
2. **Privilege-eskalering vid läckage** — om någon får commit-rättigheter på
   en feature-branch kan de byta emailen till sin egen och få admin-yta vid
   nästa deploy. Defense-in-depth (ADR 0028) hjälper inte mot opt-in-värde
   i config.
3. **Cross-environment-läckage** — samma `appsettings.json` byggs in i alla
   miljöer. Att hardkoda är att exportera prod-admin till dev-image.

## Prod-konfig-flöde (steg-för-steg)

### 1. Skapa secret i AWS Secrets Manager

```bash
aws secretsmanager create-secret \
  --name jobbpilot/prod/admin/initial-admin-email \
  --description "InitialAdminEmail för IdempotentAdminRoleSeeder" \
  --secret-string '{"email":"klas@jobbpilot.se"}' \
  --kms-key-id alias/jobbpilot-prod-master \
  --profile jobbpilot
```

**KMS-nyckel:** använd `jobbpilot-prod-master`-alias (samma som RDS/Redis-
secrets). Bevarar konsistens i nyckelhantering per ADR 0024.

### 2. Mappa secret → ECS task-def env-var

I `infra/modules/ecs/main.tf` på `aws_ecs_task_definition.api` container-
definition lägg till under `secrets`-blocket:

```hcl
secrets = [
  # ... befintliga secrets (ConnectionStrings__Postgres etc) ...
  {
    name      = "AdminBootstrap__InitialAdminEmail"
    valueFrom = "${aws_secretsmanager_secret.admin_email.arn}:email::"
  }
]
```

ECS hämtar värdet vid container-start och injicerar som env-var.
.NET:s configuration-binder mappar `AdminBootstrap__InitialAdminEmail` till
`AdminBootstrapOptions.InitialAdminEmail` via det dubbla underscoret
(standard-konvention).

### 3. Grant task-execution-role access till secret

Lägg till statement i `aws_iam_role_policy.ecs_task_execution_secrets`:

```hcl
{
  Effect = "Allow"
  Action = ["secretsmanager:GetSecretValue"]
  Resource = [
    # ... befintliga ARN:s ...
    aws_secretsmanager_secret.admin_email.arn,
  ]
}
```

Plus `kms:Decrypt` på master-keyen om secret är KMS-encrypted (det är den).

### 4. Verifiera vid första prod-deploy

Efter deploy:

```bash
# Vänta in service stable
aws ecs describe-services \
  --cluster jobbpilot-prod-cluster \
  --services jobbpilot-prod-api \
  --query 'services[0].deployments[0].rolloutState' \
  --output text --profile jobbpilot

# Tail CloudWatch logs för seeder
aws logs tail /ecs/jobbpilot-prod-api --since 5m --profile jobbpilot \
  | grep -iE "IdempotentAdminRoleSeeder|admin.*role"
```

**Förväntat logginnehåll:**

- `IdempotentAdminRoleSeeder: ensuring Admin role exists`
- `IdempotentAdminRoleSeeder: Admin role assigned to user <user-id>`
  (UserId, inte email — per PII-disciplin från Fas 1 in-block-fix M2)

**Block-kriterium:** seeder loggar warning `InitialAdminEmail is empty,
skipping admin assignment` → secret är inte rätt mappad. Felsök ECS
task-def + IAM-grants.

## Rotation (byte av admin)

Om Klas ska överlåta admin-yta till annan person:

1. Lägg till nya admin manuellt via DB (`AspNetUserRoles` insert) ELLER via
   admin-suspendering/grant-flow när Fas 6 admin-yta är klar.
2. Uppdatera `AdminBootstrap__InitialAdminEmail`-secret till nya emailen:
   ```bash
   aws secretsmanager update-secret \
     --secret-id jobbpilot/prod/admin/initial-admin-email \
     --secret-string '{"email":"ny.admin@jobbpilot.se"}' \
     --profile jobbpilot
   ```
3. Force-new-deployment för Api så seeder körs med nya värdet:
   ```bash
   aws ecs update-service \
     --cluster jobbpilot-prod-cluster \
     --service jobbpilot-prod-api \
     --force-new-deployment --profile jobbpilot
   ```
4. Revoke gamla admin via `AspNetUserRoles` delete (eller Fas 6 admin-flow).

**Seeder bibehåller idempotens** — den lägger till rollen om den saknas, men
revoke:ar inte tidigare admins. Det är medvetet val (defense mot deploy-
katastrof där en feltänkt config-ändring låser ut alla admins).

## Lokal dev — bypass via appsettings.Local.json

För dev-uppstart räcker:

```json
// appsettings.Local.json (gitignored)
{
  "AdminBootstrap": {
    "InitialAdminEmail": "klas@jobbpilot.test"
  }
}
```

Registrera sedan en user med samma email i auth-flödet och du har admin
nästa request (per ADR 0028 per-request roll-fetch).

## Cross-references

- **ADR 0028 §4** — IdempotentAdminRoleSeeder + IHostedService-mönstret
- **CLAUDE.md §11.3** — `appsettings.Local.json` är gitignored
- **`docs/runbooks/aws-setup.md`** — översikt över Secrets Manager-strukturen
- **TD-51** — admin-läs-aktioner audit-logging (Fas 6, GDPR Art. 30)
- **TD-52** — dedikerad rate-limit-policy för admin-endpoints (Fas 6)
