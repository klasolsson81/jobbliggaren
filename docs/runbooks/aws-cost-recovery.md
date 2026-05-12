# Runbook — AWS Cost Recovery (F2-P4)

**Skapad:** 2026-05-12 (F2-P3 stub) → utbyggd 2026-05-12 (F2-P4)
**Owner:** Klas (Fas 1-2) — överlämnas till FinOps-roll vid Fas 7 internal beta
**Budget:** `jobbpilot-monthly` ($50/mån, ägs av prod/baseline-stack)
**SNS-topic:** `${name_prefix}-cost-anomaly` (KMS-encrypted)
**Automatik:** APPLY_IAM_POLICY Budget Action vid 100% ACTUAL → JobbPilotBedrockDeny på api-task-role
**Källor:** ADR 0005 amendment + second amendment 2026-05-12

---

## Bakgrund

Per ADR 0005 second amendment 2026-05-12 (Alt C — invite-only public beta
med hård cap):

- **Primärskydd (automatisk):** AWS Budget Action APPLY_IAM_POLICY bifogar
  `JobbPilotBedrockDeny`-overlay på api-task-role vid 100% ACTUAL av
  $50/mån-budgeten. Explicit Deny vinner över Allow per IAM-evaluation-logik
  — blockerar all `bedrock:Invoke*` / `Converse*`. Reversibel via
  auto-detach vid cycle-reset.
- **Sekundärskydd (manuell):** ECS scale-down via denna runbook. AWS Budget
  Actions API stödjer inte custom SSM-documents eller Lambda-trigger för
  Fargate-services — ECS-stop kan inte automatiseras via Budget Actions.

ECS Fargate-baseline ~$30/mån är fast kostnad (inte skenrisk). Bedrock-
invocation är enda blowout-vektorn — täckt av primärskyddet.

---

## När triggar primärskyddet

Budget Action publicerar SNS-event till `${name_prefix}-cost-anomaly`-topic
vid 100% ACTUAL. Om SNS-email-subscription är opt-in:ad: AWS-mail med rubrik
"AWS Budgets Action - Policy Applied".

**Verifiering att deny är attachad:**

```powershell
aws iam list-attached-role-policies `
  --role-name jobbpilot-dev-ecs-task-api `
  --profile jobbpilot `
  --query 'AttachedPolicies[?PolicyName==`JobbPilotBedrockDeny`]'
```

Förväntat efter trigger: en träff med ARN `arn:aws:iam::710427215829:policy/JobbPilotBedrockDeny`.

---

## Decision-tree vid kostnadslarm

```
┌──────────────────────────────────────┐
│ Larm: Budget 100% ACTUAL / SNS-event │
└─────────────────┬────────────────────┘
                  │
                  v
        ┌─────────────────────┐
        │ Steg 1: Klassificera│
        │ (CloudWatch + CE)   │
        └──────┬──────────────┘
               │
       ┌───────┴────────┬────────────────┐
       v                v                v
 ┌──────────┐    ┌──────────────┐  ┌──────────────┐
 │ Bedrock  │    │ ECS/RDS/Redis│  │ Okänd        │
 │ blowout  │    │ fast kostnad │  │ källa        │
 │          │    │ (ej skenande)│  │              │
 └────┬─────┘    └──────┬───────┘  └──────┬───────┘
      │                 │                 │
      v                 v                 v
 ┌──────────┐    ┌──────────────┐  ┌──────────────────┐
 │ Steg 2:  │    │ Steg 3:      │  │ Steg 4: Forensik │
 │ Validera │    │ Verifiera    │  │ + CloudTrail-    │
 │ deny     │    │ baseline     │  │ granskning       │
 │ aktiv +  │    │ ($30/mån     │  │                  │
 │ ECS-stop │    │ ECS-fargate) │  │                  │
 │ om kvar  │    │              │  │                  │
 └────┬─────┘    └──────┬───────┘  └──────┬───────────┘
      │                 │                 │
      └────────┬────────┴─────────────────┘
               v
        ┌─────────────────────┐
        │ Steg 5: Incident-   │
        │ rapport + post-     │
        │ mortem              │
        └─────────────────────┘
```

---

## Steg 1 — Klassificera (5-10 min)

Hitta kostnads-drivaren via Cost Explorer-CLI:

```powershell
# Senaste 7 dagars kostnad per service
$end = (Get-Date).ToString('yyyy-MM-dd')
$start = (Get-Date).AddDays(-7).ToString('yyyy-MM-dd')

aws ce get-cost-and-usage `
  --time-period "Start=$start,End=$end" `
  --granularity DAILY `
  --metrics UnblendedCost `
  --group-by Type=DIMENSION,Key=SERVICE `
  --profile jobbpilot `
  --output table
```

```powershell
# Bedrock-specifik daglig kostnad (om Bedrock-blowout misstänks)
aws ce get-cost-and-usage `
  --time-period "Start=$start,End=$end" `
  --granularity DAILY `
  --metrics UnblendedCost `
  --filter '{\"Dimensions\":{\"Key\":\"SERVICE\",\"Values\":[\"Amazon Bedrock\"]}}' `
  --profile jobbpilot `
  --output table
```

**Tolkning:**

| Pattern | Klassificering | Nästa steg |
|---|---|---|
| Bedrock > $20 + ECS ≈ $30 + RDS ≈ $13 | Bedrock-blowout | Steg 2 |
| ECS > $50 + Bedrock ≈ $0 | ECS-skenande (osannolikt) | Steg 3 |
| RDS/Redis > $20 | Datalagrings-skenande | Steg 3 |
| Spread över flera services | Forensik krävs | Steg 4 |
| Okänd EC2/NAT-kostnad | Möjlig kompromiss | Steg 4 + säkerhet |

---

## Steg 2 — Bedrock-blowout (primärskydd-validering)

Verifiera att Bedrock-deny är aktivt (primärskyddet ska redan ha triggat):

```powershell
# Lista alla policies på api-task-rollen
aws iam list-attached-role-policies `
  --role-name jobbpilot-dev-ecs-task-api `
  --profile jobbpilot `
  --output table
```

**Förväntat:** två policies — `JobbPilotBedrockInvoke` (normal-state) +
`JobbPilotBedrockDeny` (Budget Action attachad).

**Om JobbPilotBedrockDeny saknas:** primärskyddet failade. Attach manuellt:

```powershell
aws iam attach-role-policy `
  --role-name jobbpilot-dev-ecs-task-api `
  --policy-arn arn:aws:iam::710427215829:policy/JobbPilotBedrockDeny `
  --profile jobbpilot
```

**Om Bedrock-kostnaden fortsätter öka trots deny:** möjlig race-condition
eller compromised credentials. Eskalera till Steg 4 (forensik).

**Eskalering till manuell ECS-stop** om Bedrock-blowout indikerar att
api-task-rollen är kompromissad och även andra resurser kan vara på spel:
fortsätt till §"Manuell ECS scale-down" nedan.

---

## Steg 3 — ECS/RDS/Redis baseline-verifiering

Förväntad dev-baseline (BUILD.md §15 + STEG 13a/13b):

| Resurs | Förväntad $/mån | Skenande-tröskel |
|---|---|---|
| ECS Fargate (api + worker) | $25-30 | > $50 |
| RDS Postgres t4g.micro | $13 | > $25 |
| ElastiCache Valkey t4g.micro | $8 | > $15 |
| NAT Gateway | $32 | > $40 (öknad utgående trafik) |
| ALB | $16 | > $20 |
| Övrigt (CW Logs, ECR, KMS) | $3-5 | > $10 |

**Om baseline överskriden:**

1. ECS — kolla `desired_count` på services. Misskonfigurerad scaling?
   ```powershell
   aws ecs describe-services `
     --cluster jobbpilot-dev `
     --services jobbpilot-dev-api jobbpilot-dev-worker `
     --profile jobbpilot `
     --query 'services[*].[serviceName,desiredCount,runningCount,pendingCount]' `
     --output table
   ```

2. RDS — kolla om engineversion eller instance-class byttes utan terraform.
   ```powershell
   aws rds describe-db-instances `
     --db-instance-identifier jobbpilot-dev-rds `
     --profile jobbpilot `
     --query 'DBInstances[*].[DBInstanceClass,MultiAZ,StorageType,AllocatedStorage]'
   ```

3. NAT — onormalt högt = ovanligt utgående trafik. Kolla VPC Flow Logs.

---

## Steg 4 — Forensik vid okänd kostnadskälla

Om Steg 1 inte tydligt isolerar källan eller om misstanke om kompromiss:

```powershell
# CloudTrail: senaste 24h ovanliga API-anrop på api-task-rollen
$start = (Get-Date).AddHours(-24).ToString('yyyy-MM-ddTHH:mm:ssZ')

aws cloudtrail lookup-events `
  --lookup-attributes AttributeKey=Username,AttributeValue=jobbpilot-dev-ecs-task-api `
  --start-time $start `
  --profile jobbpilot `
  --max-items 100 `
  --query 'Events[*].[EventTime,EventName,EventSource,SourceIPAddress]' `
  --output table
```

```powershell
# CloudTrail: nya IAM-aktiviteter (potential privilege-escalation)
aws cloudtrail lookup-events `
  --lookup-attributes AttributeKey=ReadOnly,AttributeValue=false `
  --start-time $start `
  --profile jobbpilot `
  --max-items 50 `
  --query 'Events[?contains([`CreateRole`,`AttachRolePolicy`,`PutRolePolicy`,`CreateUser`,`CreateAccessKey`],EventName)]'
```

**Vid bekräftad kompromiss:**

1. Rotera api-task-role: `terraform destroy -target=module.iam_ecs.aws_iam_role.task_api`
   sedan re-applya — ny role-ARN, ECS-services kör vidare med gamla rollen
   tills task-def-update (force-new-deployment).
2. Granska Secrets Manager för rotation: `aws secretsmanager rotate-secret ...`
3. Skapa security-incident i `docs/security-incidents/YYYY-MM-DD-cost-blowout.md`
4. Vid PII-exposure: GDPR Art. 33 notification till IMY inom 72h

---

## Manuell ECS scale-down (när primärskyddet inte räcker)

Triggas explicit av incident-responder. Inte automatiskt.

**När använda:**

- Bedrock-blowout fortsätter trots deny (Steg 2 misslyckas)
- Misstänkt kompromiss av api/worker-task-role
- Klas explicit önskar full compute-pause

**Procedur:**

```powershell
# Stoppa Api-service
aws ecs update-service `
  --cluster jobbpilot-dev `
  --service jobbpilot-dev-api `
  --desired-count 0 `
  --profile jobbpilot

# Stoppa Worker-service
aws ecs update-service `
  --cluster jobbpilot-dev `
  --service jobbpilot-dev-worker `
  --desired-count 0 `
  --profile jobbpilot
```

**Verifiering inom 60s:**

```powershell
aws ecs describe-services `
  --cluster jobbpilot-dev `
  --services jobbpilot-dev-api jobbpilot-dev-worker `
  --profile jobbpilot `
  --query 'services[*].[serviceName,runningCount,desiredCount]' `
  --output table
```

Förväntat: `desiredCount=0`, `runningCount` sjunker mot 0 inom ~60s.

**Konsekvens:** `dev.jobbpilot.se` returnerar 503 (ALB target-group tom).
Hangfire-jobs körs inte. Klassisk "maintenance mode".

**Genvägs-script:** `infra/scripts/cost-recovery/stop-ecs-services.ps1`
körs en-knapps.

---

## Återställning efter incident

### Steg R1 — Identifiera och fixa kostnads-drivaren

Innan återställning: säkerställ att grundorsaken är fixad. Att starta upp
ECS igen utan att stoppa kostnads-driver = ny blowout inom timmar.

| Drivare | Fix-checklista |
|---|---|
| Bedrock-loop i kod | Granska senaste deploy (commit + tag). Rolla tillbaka om nödvändigt. Kolla AI-prompts som kanske loop:ar. |
| Bot-trafik mot API | Kolla rate-limit-effektivitet i CloudWatch. Höj policies tillfälligt. WAF om legitim attack. |
| Kompromiss | Rotera credentials (Steg 4). Granska CloudTrail. Återställ EFTER ny role + Secrets-rotation. |
| Konfigurationsfel | Granska terraform plan. Felaktig autoscaling? Felaktig logg-retention? |

### Steg R2 — Detacha JobbPilotBedrockDeny

Om budget-cycle inte resettat ännu (mitt i månaden):

```powershell
aws iam detach-role-policy `
  --role-name jobbpilot-dev-ecs-task-api `
  --policy-arn arn:aws:iam::710427215829:policy/JobbPilotBedrockDeny `
  --profile jobbpilot
```

**Om cycle redan resettat:** Budget Action har auto-detachat. Verifiera:

```powershell
aws iam list-attached-role-policies `
  --role-name jobbpilot-dev-ecs-task-api `
  --profile jobbpilot `
  --query 'AttachedPolicies[?PolicyName==`JobbPilotBedrockDeny`]'
```

Tom array = redan detachad.

### Steg R3 — Skala upp ECS

```powershell
aws ecs update-service `
  --cluster jobbpilot-dev `
  --service jobbpilot-dev-api `
  --desired-count 1 `
  --profile jobbpilot

aws ecs update-service `
  --cluster jobbpilot-dev `
  --service jobbpilot-dev-worker `
  --desired-count 1 `
  --profile jobbpilot
```

Vänta 3-5 min för ECS deployment.

**Genvägs-script:** `infra/scripts/cost-recovery/restore-ecs-services.ps1`.

### Steg R4 — Smoke-test

```powershell
# API live (process up)
curl -s https://dev.jobbpilot.se/api/ready -o /dev/null -w "%{http_code}`n"
# Förväntat: 200

# API ready (DB + Redis check, post-F2-P6)
# (Efter F2-P6-leverans — kontrollera DB + Redis-anslutning)
```

### Steg R5 — Bekräfta Bedrock fungerar (om relevant)

Om Bedrock används i normal-state och Steg 1 isolerade källan:

```powershell
# Kolla att api-task-role kan invokera Bedrock (efter detach)
# Klas kör test-flow i app-UI istället för att direkt-testa IAM
```

---

## Post-mortem-template

Skapa fil `docs/security-incidents/YYYY-MM-DD-cost-blowout-<slug>.md` efter
varje budget-trigger (även "false-positive" Budget Action). Mall:

```markdown
# Cost Blowout — YYYY-MM-DD

**Trigger:** AWS Budget Action APPLY_IAM_POLICY ($50 ACTUAL)
**Upptäckt:** HH:MM UTC via [AWS-mail / SNS / manuell granskning]
**Påverkan:** [primärskydd aktiverat / ingen / annan]
**Total kostnad denna cycle:** $X
**Resolution:** HH:MM UTC

## Klassifikation

[Bedrock-blowout | ECS/RDS-skenande | Forensik krävs | False-positive]

## Tidslinje

| Tid (UTC) | Händelse |
|---|---|
| HH:MM | Budget threshold-breach |
| HH:MM | SNS-event publicerat |
| HH:MM | JobbPilotBedrockDeny attachad |
| HH:MM | Klas notifierad |
| HH:MM | Steg 1 klar — kostnadskälla isolerad |
| HH:MM | Resolution: [...] |

## Rotorsak

[Vad orsakade kostnaden? Kod-bug, bot-trafik, kompromiss, konfig-drift?]

## Åtgärder

- [ ] Grundorsak fixad
- [ ] Återställnings-procedur körd (Steg R1-R5)
- [ ] Smoke-test PASS
- [ ] Bedrock-funktion verifierad

## Lärdomar

- Vad fångade larmet snabbt / långsamt?
- Saknades larm för annan dimension?
- Behöver primärskyddet utvidgas?

## Uppföljning

- [ ] TD/ADR-uppdatering om processen ändras
- [ ] Runbook-uppdatering om steg saknades

## GDPR Art. 33-bedömning (om kompromiss-misstanke)

- PII exponerad: [nej / ja, vilken / okänt]
- Notification till IMY: [ej krävd / krävd inom 72h / skickad HH:MM]
- Källa: [CloudTrail-events, audit-log-granskning, etc.]
```

---

## Test-procedur (utan att brännas $50)

Hela mekaniken kan inte fullt end-to-end-testas utan att triggera real
kostnad. Däremot kan delkomponenter verifieras säkert.

### Test 1 — Verifiera Budget Action existens + konfig

```powershell
aws budgets describe-budget-actions-for-budget `
  --account-id 710427215829 `
  --budget-name jobbpilot-monthly `
  --region us-east-1 `
  --profile jobbpilot `
  --query 'Actions[*].[ActionId,ActionType,Status,ApprovalModel,Definition.IamActionDefinition.PolicyArn]' `
  --output table
```

**Förväntat:**
- ActionType: `APPLY_IAM_POLICY`
- Status: `STANDBY` (inte triggad) eller `EXECUTION_SUCCESS` (har triggat)
- ApprovalModel: `AUTOMATIC`
- PolicyArn: `arn:aws:iam::710427215829:policy/JobbPilotBedrockDeny`

### Test 2 — Verifiera deny-policy-innehåll

```powershell
aws iam get-policy-version `
  --policy-arn arn:aws:iam::710427215829:policy/JobbPilotBedrockDeny `
  --version-id v1 `
  --profile jobbpilot `
  --query 'PolicyVersion.Document'
```

**Förväntat:** Statement med `Effect: Deny` på `bedrock:InvokeModel*` + `bedrock:Converse*`, Resource `*`.

### Test 3 — Verifiera SNS topic-policy

```powershell
aws sns get-topic-attributes `
  --topic-arn arn:aws:sns:eu-north-1:710427215829:jobbpilot-dev-cost-anomaly `
  --profile jobbpilot `
  --query 'Attributes.Policy' `
  --output text | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

**Förväntat:** Statement som tillåter `budgets.amazonaws.com` publish med
`AWS:SourceAccount`-condition.

### Test 4 — Simulera manuell deny-attach + verifiera Bedrock fail

**Endast om JobbPilotBedrockInvoke är i bruk!** För Fas 2 är Bedrock inte
aktiv än, så detta test är defererat till Fas 4.

```powershell
# Steg 1: attach deny manuellt
aws iam attach-role-policy `
  --role-name jobbpilot-dev-ecs-task-api `
  --policy-arn arn:aws:iam::710427215829:policy/JobbPilotBedrockDeny `
  --profile jobbpilot

# Steg 2: trigga AI-feature i app (när Fas 4 live)
# Förväntat: AI-funktion returnerar 5xx eller specifikt "Bedrock denied"-fel

# Steg 3: detach
aws iam detach-role-policy `
  --role-name jobbpilot-dev-ecs-task-api `
  --policy-arn arn:aws:iam::710427215829:policy/JobbPilotBedrockDeny `
  --profile jobbpilot
```

### Test 5 — Manuell ECS-stop-rollback-cykel

Säker att köra: stoppar + startar tjänsterna utan kostnadsimpact (faktiskt
sparar ~5 min ECS-tid).

```powershell
# Kör scripts (sekvensiellt):
.\infra\scripts\cost-recovery\stop-ecs-services.ps1
# Vänta 60s, smoke-test 503
.\infra\scripts\cost-recovery\restore-ecs-services.ps1
# Vänta 3-5 min, smoke-test 200
```

---

## SNS-subscription för cost-anomaly

För att få email-notis vid Budget Action-trigger:

```powershell
# 1. Sätt cost_anomaly_alert_email i terraform.tfvars
notepad infra\terraform\environments\dev\terraform.tfvars
# Lägg till: cost_anomaly_alert_email = "klasolsson81@gmail.com"

# 2. Re-applya
cd infra\terraform\environments\dev
$env:AWS_PROFILE = "jobbpilot"
terraform apply -target=module.budget_actions

# 3. Opta-in via AWS-mail (subject: "AWS Notification - Subscription Confirmation")
```

Granska subscription-lista kvartalsvis:

```powershell
aws sns list-subscriptions-by-topic `
  --topic-arn arn:aws:sns:eu-north-1:710427215829:jobbpilot-dev-cost-anomaly `
  --profile jobbpilot
```

---

## Severity-klassificering

| Pattern | Severity | Respons-fönster |
|---|---|---|
| Budget Action trigger (100% ACTUAL) | HIGH | 1h |
| Kostnad > $80 (160% av threshold) | CRITICAL | 30 min |
| Bedrock > $20 enstaka dag | HIGH | 4h |
| Misstänkt kompromiss | CRITICAL | Omedelbart |
| Forecasted 100% (FORECASTED notif) | MEDIUM | 24h |
| 80% ACTUAL email-warning | LOW | Granska inom 7d |

---

## Pre-apply-verifiering (innan första `terraform apply`)

Innan `terraform apply` av `modules/budget_actions/` i ny miljö:

1. **Verifiera target-role existerar:**
   ```powershell
   aws iam get-role --role-name <name_prefix>-ecs-task-api --profile <profile>
   ```

2. **Verifiera budget existerar i baseline-stacken:**
   ```powershell
   aws budgets describe-budget `
     --account-id <account-id> `
     --budget-name <budget_name> `
     --region us-east-1
   ```

3. **Verifiera KMS-key access:**
   ```powershell
   aws kms describe-key --key-id <kms-key-id> --profile <profile>
   ```

Om något av ovan failar → fixa innan apply (annars partial-create).

---

## Relaterade dokument

- [ADR 0005](../decisions/0005-go-to-market-strategy.md) — Go-to-market + kostnadsskydd-strategi
- [Modul: budget_actions](../../infra/terraform/modules/budget_actions/) — Terraform-källkod
- [failed-access-anomaly.md](failed-access-anomaly.md) — Security-anomaly-runbook (parallell mekanism)
- [Scripts: cost-recovery](../../infra/scripts/cost-recovery/) — Checked-in återställnings-PowerShell-script

## Källor (verifierade 2026-05-12)

- AWS CLI Reference `create-budget-action` v2.29.1
- Terraform AWS Provider `aws_budgets_budget_action` v5.80
- AWS CloudFormation `BudgetsAction SsmActionDefinition`
- AWS IAM Policy Evaluation Logic (deny precedence)
