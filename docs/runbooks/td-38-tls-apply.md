# TD-38 TLS-hardening Apply-fas Runbook

**Skapad:** 2026-05-11 (Fas 1 Block A4)
**Källa:** security-auditor Apply-fas-checklist (8 pkt)
**Ändrar:** Api + Worker connection-strings i Secrets Manager från
`Trust Server Certificate=true` → `SSL Mode=VerifyFull;Root Certificate=…`

---

## Förkrav

1. AWS SSO-session aktiv: `aws sso login --profile jobbpilot`
2. Backend CI grön på HEAD (`gh run list --limit 1`)
3. Block A code-commits pushed till main (ebb7550 + 48ebe0e eller senare)
4. Klas-GO för apply (terraform apply + Migrate-task re-run + force-redeploy
   är AWS-touch som kräver explicit godkännande)

---

## Apply-fas-checklist (security-auditor Apply-fas, 8 pkt)

### 1. Verifiera bundle-integritet mot AWS upstream

```bash
# Diff lokal bundle mot AWS publika bundle
curl -fsSL https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem \
  -o /tmp/aws-bundle-current.pem
diff infra/certs/rds-global-bundle.pem /tmp/aws-bundle-current.pem
# Förväntat: ingen diff. Annars: AWS har roterat bundle → uppdatera lokal + re-commit
```

**Block-kriterium:** diff → avbryt apply, uppdatera bundle, re-build images.

### 2. Build Api + Worker + Migrate images (deploy-dev.yml via tag)

```bash
# Skapa tag för apply-trigger
git tag v0.1.1-dev -a -m "TD-38 TLS-hardening apply"
git push origin v0.1.1-dev
# Vänta på deploy-dev.yml run (3-5 min för ECR push + ECS deploy)
gh run watch
```

**Verifiera:** ECR-image `jobbpilot-dev-api:v0.1.1-dev` + `jobbpilot-dev-worker:v0.1.1-dev`
pushed.

### 3. Bundle-verifiering inuti container (smoke-test)

```bash
# Hämta image från ECR + verifiera bundle finns och har rätt storlek
aws ecr get-login-password --region eu-north-1 --profile jobbpilot \
  | docker login --username AWS --password-stdin 710427215829.dkr.ecr.eu-north-1.amazonaws.com
docker pull 710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-api:v0.1.1-dev
docker run --rm 710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-api:v0.1.1-dev \
  ls -la /etc/ssl/certs/rds-global-bundle.pem
# Förväntat: -rw-r--r-- … 169 KB rds-global-bundle.pem
```

**Block-kriterium:** filen saknas eller har annan storlek → debug Dockerfile-COPY.

### 4. Migrate-task re-run (uppdatera Secrets Manager med VerifyFull-CS)

```bash
# Hämta migrate-task-def family
aws ecs run-task \
  --cluster jobbpilot-dev-cluster \
  --task-definition jobbpilot-dev-migrate \
  --launch-type FARGATE \
  --network-configuration 'awsvpcConfiguration={subnets=[<private-subnet>],securityGroups=[<ecs-sg>],assignPublicIp=DISABLED}' \
  --profile jobbpilot

# Vänta på exit code 0 via CloudWatch logs:
aws logs tail /ecs/jobbpilot-dev-migrate --follow --profile jobbpilot
# Förväntat: "Migrate complete" + exit 0
```

**Block-kriterium:** task exit != 0 → läs CloudWatch-logs för error, rollback
till tidigare task-def-revision.

### 5. Verifiera Secrets Manager innehåller VerifyFull-CS (INTE Trust=true)

```bash
# Hämta båda secrets och verifiera innehåll
aws secretsmanager get-secret-value \
  --secret-id jobbpilot/dev/db/app-connection-string \
  --query SecretString --output text --profile jobbpilot | \
  grep -q "SSL Mode=VerifyFull" && echo "✓ app-CS OK" || echo "✗ app-CS WRONG"

aws secretsmanager get-secret-value \
  --secret-id jobbpilot/dev/db/hangfire-storage-connection-string \
  --query SecretString --output text --profile jobbpilot | \
  grep -q "SSL Mode=VerifyFull" && echo "✓ worker-CS OK" || echo "✗ worker-CS WRONG"

# Anti-regression: verifiera att Trust=true INTE finns kvar
aws secretsmanager get-secret-value \
  --secret-id jobbpilot/dev/db/app-connection-string \
  --query SecretString --output text --profile jobbpilot | \
  grep -q "Trust Server Certificate=true" && echo "✗ FAIL: Trust=true kvar" || echo "✓ Trust=true borta"
```

**Block-kriterium:** någon assert failar → rollback Migrate-task till tidigare
revision, debug `ConnectionStringFactory.ForPersisted`-anrop.

### 6. Force new deployment av Api + Worker (plocka upp nya secrets)

```bash
aws ecs update-service \
  --cluster jobbpilot-dev-cluster \
  --service jobbpilot-dev-api \
  --force-new-deployment \
  --profile jobbpilot

aws ecs update-service \
  --cluster jobbpilot-dev-cluster \
  --service jobbpilot-dev-worker \
  --force-new-deployment \
  --profile jobbpilot

# Vänta på båda tjänster 1/1 stable (~2-3 min):
aws ecs describe-services \
  --cluster jobbpilot-dev-cluster \
  --services jobbpilot-dev-api jobbpilot-dev-worker \
  --query 'services[*].[serviceName,runningCount,desiredCount]' \
  --output table --profile jobbpilot
```

### 7. Bevaka CloudWatch för Npgsql-handshake-errors

```bash
# Api-loggar
aws logs tail /ecs/jobbpilot-dev-api --follow --since 5m --profile jobbpilot | \
  grep -iE "npgsql|tls|ssl|certificate|handshake"

# Worker-loggar
aws logs tail /ecs/jobbpilot-dev-worker --follow --since 5m --profile jobbpilot | \
  grep -iE "npgsql|tls|ssl|certificate|handshake"
```

**Block-kriterium:** errors som "remote certificate is invalid",
"hostname mismatch", "PartialChain" → rollback till tidigare task-def-revision,
analysera offline (Trust=true-CS i tidigare image-tag fungerar fortfarande mot
RDS, så rollback är säker).

### 8. Smoke-test + Block A-stängning

```bash
# Public smoke-test
curl -I https://dev.jobbpilot.se/api/ready
# Förväntat: HTTP/2 200 + Strict-Transport-Security: max-age=31536000

# TD-38 stäng i tech-debt.md (header + status-sektion uppdaterade)
# git commit -m "docs: TD-38 STÄNGD — TLS-hardening apply:ad"
```

---

## Rollback-procedur (om något går fel mellan steg 4-7)

```bash
# Hämta tidigare task-def-revision (innan TD-38-deploy)
aws ecs list-task-definitions \
  --family-prefix jobbpilot-dev-api \
  --sort DESC --max-items 5 \
  --profile jobbpilot

# Update service till tidigare revision
aws ecs update-service \
  --cluster jobbpilot-dev-cluster \
  --service jobbpilot-dev-api \
  --task-definition jobbpilot-dev-api:<prev-revision> \
  --profile jobbpilot

# Samma för Worker.
# Migrate-task-re-run behöver inte rollbackas; Secrets Manager-värden
# bevaras tills nästa Migrate-körning.
```

**OBS:** Rollback till tidigare task-def gör att Api/Worker plockar upp
TIDIGARE secret-version i Secrets Manager. Eftersom Migrate redan har
uppdaterat secrets till VerifyFull-format, kan tidigare task-def-revision
(som har container UTAN bundle) failera på SSL-handshake. För säker rollback:

1. Rollback Secrets Manager till tidigare version också:
   ```bash
   aws secretsmanager update-secret-version-stage \
     --secret-id jobbpilot/dev/db/app-connection-string \
     --version-stage AWSCURRENT \
     --move-to-version-id <prev-version-id> \
     --remove-from-version-id <new-version-id> \
     --profile jobbpilot
   ```
2. Force-new-deployment på rollback:ad task-def-revision.

Eller — enklare — re-run Migrate-task med tidigare image-tag som har
`BuildConnectionString` (single function, Trust=true).

---

## RDS CA-bundle-rotation (om AWS roterar bundle)

Per TD-47:

1. Download ny bundle:
   ```bash
   curl -fsSL https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem \
     -o infra/certs/rds-global-bundle.pem
   ```
2. Verifiera diff: `git diff infra/certs/rds-global-bundle.pem` — granska att
   förändringar är expected (nya CAs tillagda, inte borttagna).
3. Re-commit + push.
4. Skapa ny tag `v0.1.X-dev` → deploy-dev.yml triggar nya images.
5. Re-run Migrate-task (steg 4-5 ovan).
6. Force-new-deployment Api + Worker (steg 6).
7. Bevaka CloudWatch (steg 7).

Inga code-changes behövs — bara bundle-rotation + re-deploy.
