# AWS-setup — JobbPilot

> **Status:** Levande dokument. Uppdateras när AWS-ytan förändras.
> **Konto:** `710427215829` (alias `jobbpilot-prod`)
> **Primär region:** `eu-north-1` (Stockholm)
> **Bedrock-regioner:** `eu-central-1` (Frankfurt), `eu-west-1` (Irland) via EU cross-region inference profile

---

## 1. Vad Klas har gjort manuellt (redan klart före session 3)

Detta är engångs-setup som ska vara på plats innan Terraform körs. Alla steg är gjorda per
SESSION-2-PLAN §12 (Klas-beslut #9). Dokumenteras här som sanningskälla.

1. **AWS-konto skapat** — fristående (ingen Organization), root-email låst i password manager.
2. **MFA på root** — hårdvarunyckel. Root-access används aldrig utom vid nödfall.
3. **IAM Identity Center (SSO) aktiverat** — instance i `eu-north-1`. Permission set
   `AdministratorAccess` (bred under fas 0; tightas i senare fas).
4. **SSO-user `Klas`** skapad och tilldelad `AdministratorAccess` på konto `710427215829`.
5. **IAM-user `jobbpilot-bootstrap-admin`** skapad med långlivade access keys.
   Används enbart för Terraform-bootstrap (se §3). **Raderas som sista steg av Fas 0**
   när SSO-profilen är verifierad mot Terraform backend.
6. **AWS CLI-profiler lokalt:**
   - `jobbpilot-bootstrap` — bootstrap-IAM-user access keys (långlivade).
   - `jobbpilot` — SSO via `klas` med `AdministratorAccess` (12h-sessions).
     Förnyas med `aws sso login --profile jobbpilot` när session löper ut.

**Verifiering:**

```bash
aws sts get-caller-identity --profile jobbpilot-bootstrap
# → arn:aws:iam::710427215829:user/jobbpilot-bootstrap-admin

aws sts get-caller-identity --profile jobbpilot
# → arn:aws:sts::710427215829:assumed-role/AWSReservedSSO_AdministratorAccess_*/Klas
```

---

## 2. Vad Terraform hanterar (session 3 steg 2)

Allt infrastruktur-as-code som inte är engångs-setup. Ligger under [`infra/terraform/`](../../infra/terraform/).

### 2.1 Bootstrap (`infra/terraform/bootstrap/`)

Engångs-stack som skapar resurserna som resten av Terraform-staten lever i:

| Resurs | Syfte |
|--------|-------|
| S3 bucket `jobbpilot-terraform-state-710427215829` | Fjärr-state för övriga stackar (versioning + encryption + public access block) |
| DynamoDB table `jobbpilot-terraform-locks` | State locks (LockID hash key, PAY_PER_REQUEST) |

**Körs med `--profile jobbpilot-bootstrap`** eftersom state-bucketen inte finns än och
SSO-profilen har subtila problem med att referera till ej-existerande state.

**State för bootstrap:** lokal (`terraform.tfstate` i bootstrap-mappen, gitignored).
Vi migrerar inte bootstrap-staten in i sin egen bucket — det ger cirkulärt beroende
vid nedplockning. Lokal state är tillräcklig för en engångs-stack.

### 2.2 Prod (`infra/terraform/environments/prod/`)

Baseline-stack för produktionskontot. Lägger grunden som AWS-kontot alltid ska ha:

| Modul | Vad den gör |
|-------|-------------|
| `modules/budgets` | Zero-spend budget + $50/månads-budget med mail-alerts vid 50/80/100% |
| `modules/cloudtrail` | Multi-region management-event trail till dedikerad S3-bucket + log file validation |
| `modules/kms` | `jobbpilot-master-key` (app-secrets, CVs) + `jobbpilot-byok-key` (BYOK envelope) med nyckelrotation |
| `modules/secrets_manager` | Placeholder-secrets för kommande app-hemligheter (inga riktiga värden än) |
| `modules/bedrock_model_access` | IAM-policy för `bedrock:InvokeModel` + Converse mot EU-profiler; dokumenterar manuell approval-process |

**Körs med `--profile jobbpilot`** (SSO). Om sessionen är utgången:

```bash
aws sso login --profile jobbpilot
```

---

## 3. Vad som INTE är automatiserat (och varför)

### 3.1 Bedrock model access

**Manuell approval krävs.** AWS har ingen first-party Terraform-resource för
`bedrock:RequestModelAccess`. Access-requesten går genom AWS Console och kan ta
minuter till timmar att få approval.

**Procedur:**

1. Logga in i AWS Console som SSO-user (`jobbpilot`-profil).
2. Region: välj `eu-central-1` (Frankfurt).
3. Bedrock → Model access → "Request access" eller "Manage model access".
4. Välj minst:
   - Anthropic / Claude Haiku 4.5
   - Anthropic / Claude Sonnet 4.6
   - Anthropic / Claude Opus 4.7 (om tillgänglig i region)
5. Submit. Approval-mail kommer när klart.
6. Upprepa i `eu-west-1` (Irland) som fallback.
7. Verifiera:

   ```bash
   aws bedrock list-foundation-models \
     --region eu-central-1 \
     --by-provider anthropic \
     --profile jobbpilot
   ```

8. Lista inference profiles i bägge regioner:

   ```bash
   aws bedrock list-inference-profiles --region eu-central-1 --profile jobbpilot
   aws bedrock list-inference-profiles --region eu-west-1   --profile jobbpilot
   ```

   Spara output till `docs/research/bedrock-inference-profiles.md` enligt
   SESSION-2-PLAN §14. Om access inte approved än — dokumentera status, och
   uppdatera BUILD.md §8.2 när godkännandet kommer.

### 3.2 CloudWatch LogGroup retention — pre-launch-gate

Per ADR 0024 D7 (TD-22) ska alla JobbPilot-LogGroups i prod ha
`retention_in_days = 30` så app-loggens IP/UA/EmailHash följer samma
30d-fönster som DELETE /me-restore + audit-anonymisering. Utan explicit
retention defaultar AWS till "never expire" — vilket är **GDPR-blocker**
för Fas 1 prod-launch.

**Pre-launch-gate (måste vara grönt innan första prod-trafiken):**

```bash
# Lista alla JobbPilot-LogGroups + deras retention
aws logs describe-log-groups \
    --log-group-name-prefix "/aws/jobbpilot" \
    --query 'logGroups[].[logGroupName,retentionInDays]' \
    --output table \
    --profile jobbpilot

# Förväntat: alla rader visar retentionInDays = 30. None/null = NOT OK.
```

**Sätt retention på en enskild grupp:**

```bash
aws logs put-retention-policy \
    --log-group-name <namn> \
    --retention-in-days 30 \
    --profile jobbpilot
```

När IaC införs (Terraform/CDK): hantera via `aws_cloudwatch_log_group`-resurs
med explicit `retention_in_days = 30`. Drift-detection upptäcker manuella
ändringar.

### 3.3 ForwardedHeaders KnownProxies — pre-launch-gate

Per TD-21 (rate-limiting) använder Api `app.UseForwardedHeaders(...)` så
`Connection.RemoteIpAddress` reflekterar klient-IP, inte ALB:s/CloudFront:s
proxy-IP. Utan korrekt KnownProxies/KnownNetworks-konfig:
- accepterar dotnet-default `X-Forwarded-For` bara från loopback → klient-IP
  förblir proxy-IP → IP-baserad rate-limiting blir effektivt no-op i prod
- alternativt (om KnownProxies sätts till `0.0.0.0/0`) accepteras spoofade
  headers från klient → rate-limit kan kringgås trivialt

**Pre-launch-gate (måste vara grönt innan första prod-trafiken):**

1. Identifiera ALB:s VPC-CIDR-block. För standard-VPC: `aws ec2 describe-vpcs
   --query 'Vpcs[].CidrBlock' --profile jobbpilot`.
2. Sätt `ForwardedHeaders__KnownNetworks__0` env-var (eller motsvarande IaC-
   resurs på Fargate task-definition) till VPC-CIDR.
3. Verifiera via curl + manuell `X-Forwarded-For: 198.51.100.42`-header att
   `Connection.RemoteIpAddress` i app-loggen reflekterar den header:n
   (om ALB skickar requesten — annars ignoreras header:n).

**Kod-skiss för Fas 0-stängning** (uppdatering av Api/Program.cs när
ALB-CIDR är känd):

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { new IPNetwork(IPAddress.Parse("10.0.0.0"), 16) },  // ALB:s VPC-CIDR
    ForwardLimit = 2,  // 2 hops: ALB → CloudFront (om används)
});
```

Idag (dev) körs Api direkt utan proxy → headers saknas → no-op. Säkerhets-
impact noll i dev, kritiskt i prod.

### 3.4 Bootstrap-IAM-user cleanup

Raderas **som sista steg av Fas 0** — efter att Terraform verifierat fungerar
fullt ut mot SSO-profilen. Detta är inte en del av session 3.

**Procedur (när Klas är redo):**

```bash
# Lista eventuella access keys kvar
aws iam list-access-keys --user-name jobbpilot-bootstrap-admin --profile jobbpilot

# Radera access keys
aws iam delete-access-key --user-name jobbpilot-bootstrap-admin \
    --access-key-id <AKIA...> --profile jobbpilot

# Radera inline / attached policies
aws iam list-attached-user-policies --user-name jobbpilot-bootstrap-admin --profile jobbpilot
# detach per rad

# Radera user
aws iam delete-user --user-name jobbpilot-bootstrap-admin --profile jobbpilot
```

Uppdatera denna runbook med datum när borttagning är gjord.

---

## 4. Kommandon — snabbreferens

```bash
# Verifiera vem jag är
aws sts get-caller-identity --profile jobbpilot

# Kolla budgets
aws budgets describe-budgets --account-id 710427215829 --profile jobbpilot

# Lista CloudTrail-trails
aws cloudtrail describe-trails --profile jobbpilot

# Lista KMS-nycklar
aws kms list-aliases --profile jobbpilot | grep jobbpilot

# Bedrock model access-status
aws bedrock list-foundation-models --region eu-central-1 --by-provider anthropic --profile jobbpilot

# Bedrock inference profiles
aws bedrock list-inference-profiles --region eu-central-1 --profile jobbpilot
aws bedrock list-inference-profiles --region eu-west-1   --profile jobbpilot

# Förnya SSO-session om utgången
aws sso login --profile jobbpilot
```

---

## 5. Revisionshistorik

| Datum | Ändring |
|-------|---------|
| 2026-04-18 | Första versionen — bootstrap + prod baseline via Terraform (session 3 steg 2) |
