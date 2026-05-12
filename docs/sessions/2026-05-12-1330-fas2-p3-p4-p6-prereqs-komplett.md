---
session: F2-P3 + F2-P4 + F2-P6 — Alla Fas 2-prereqs komplett
datum: 2026-05-12
slug: fas2-p3-p4-p6-prereqs-komplett
status: Klar
commits:
  - 0d66fe5  # F2-P3 feat(infra): Budget Actions terraform-modul
  - ce3e013  # F2-P3 docs(adr): ADR 0005 second amendment + runbook-stub
  - f7bd9fc  # F2-P3 fix(infra): em-dash disciplinretur
  - 0ea23eb  # F2-P3 docs: current-work-stängning
  - 09cd1b9  # F2-P4 docs(runbook): cost-recovery full utbyggnad + scripts
  - a25cbbb  # F2-P6 feat(api): readiness-probe-split (TD-29 stängd)
  - bb0a2de  # F2-P4 + F2-P6 docs(current-work): Fas 2-prereqs avklarade
---

# Session 2026-05-12 (eftermiddag) — Alla Fas 2-prereqs komplett

## Mål

Stänga F2-P3 (Budget Actions terraform) → F2-P4 (cost-recovery-runbook full
utbyggnad) → F2-P6 (TD-29 readiness-probe-split). Klas-prompt: non-stop-flöde
post-lunch, AFK-tolerant per memory `feedback_nonstop_with_pr_reports`.

## Vad som blev klart

7 commits pushade till `main`. Hela Fas 2-prereqs-tabellen avklarad:

| Batch | Status |
|---|---|
| F2-P0 invitations + waitlist | ✓ (förmiddag, separat session) |
| F2-P1 feature-flag | ✓ (F2-P0e) |
| F2-P2 rate-limits | ✓ (F2-P0e) |
| **F2-P3 Budget Actions** | ✓ idag (3 CTO-ronder) |
| **F2-P4 cost-recovery-runbook** | ✓ idag |
| **F2-P6 readiness-probe-split (TD-29)** | ✓ idag |

JobTech-features (P7 paginering + P8 JobTech-integration) får nu startas.

## F2-P3 — AWS Budget Actions (4 commits)

### CTO-konvergens — tre ronder pga AWS-API-begränsningar

Föreslagen design krävde tre senior-cto-advisor-ronder:

**Rond 1 (`a314494fb60370436`):** A4/B1/**C1**/D2/E1/F1 — Hybrid placement
(Budget i baseline, Actions i dev-stack) + APPLY_IAM_POLICY för Bedrock-deny
+ SNS→Lambda för ECS-stop + dedikerad cost-anomaly-topic + 80/100% trösklar
+ AUTOMATIC approval.

**Rond 2 (`ad162f50dacbd0a0a`):** C1 → C2' — Lambda-vägen omöjlig. AWS Budget
Actions API har ingen `INVOKE_LAMBDA`-action_type. CTO bytte till custom
SSM Automation Document (Path γ) som direkt anropar `ecs:UpdateService`.

**Rond 3 (`a37fedf646b292a84`):** C2' → **Väg III** — också omöjlig. Web-
search 2026-05-12 mot AWS CLI v2.29.1 + Terraform AWS Provider v5.80 +
CloudFormation BudgetsAction-spec bekräftade att `RUN_SSM_DOCUMENT` Budget
Action endast stödjer `STOP_EC2_INSTANCES`/`STOP_RDS_INSTANCES` — inga
custom SSM-documents för Fargate. CTO valde att skippa ECS-stop som
automatisk Budget Action helt; sekundärskydd via manuell runbook F2-P4.

### Beslut-rationalet (rond 3)

- ECS Fargate ~$30/mån är **fast kostnad**, inte skenrisk
- Bedrock-invocation = enda blowout-vektorn (täckt av primärskydd via
  APPLY_IAM_POLICY)
- Indirekta workarounds (SNS→Lambda via duplicerade budgetar, eller indirekt
  IAM-deny på ECS-execution-roll) bryter proportionalitets-principen
  (Ford/Parsons/Kua 2017 — Fitness Functions: defense-in-depth ska kostnads-
  mässigt vara billigare än det skyddar mot)
- Manuell ECS-stop via runbook är industri-default för dev-miljöer
  (12-Factor App §IX Disposability)

### Levererade resurser i AWS dev

```
JobbPilotBedrockDeny v1                 (deny-overlay IAM-policy)
jobbpilot-dev-budget-action-role        (least-priv execution-role,
                                         Attach/DetachRolePolicy lockad
                                         till JobbPilotBedrockDeny via
                                         PolicyARN-condition)
jobbpilot-dev-cost-anomaly SNS-topic    (KMS-encrypted, topic-policy
                                         lockad till budgets.amazonaws.com)
Budget Action 0115c684-...-fc69529da7bf APPLY_IAM_POLICY, 100% ACTUAL,
                                         AUTOMATIC, STANDBY
```

api-task-role normal-state: bara `JobbPilotBedrockInvoke` attachad (deny
aktiveras automatiskt vid threshold-breach).

### Disciplinmiss — em-dash (U+2014)

Första apply-försök failade på `aws_iam_role.description` regex-validation.
AWS IAM CreateRole tillåter inte U+2014 EM DASH i description-fältet
(regex `[	
 -~¡-ÿ]*`). 3 av 6 resurser
hann skapas innan fel — SNS-topic + topic-policy + JobbPilotBedrockDeny
(aws_iam_policy.description tillåter unicode, aws_iam_role.description är
restriktiv).

**Fix:** ersatt em-dash med ASCII-bindestreck + notering i description om
regex-begränsningen. Re-apply efter fix kompletterade resterande 3 utan
ytterligare problem. Disciplinmiss-återhämtning per memory
`feedback_nonstop_with_pr_reports` — fix + recommit + push utan stopp.

### ADR 0005 second amendment

Behövs eftersom amendment 2026-05-12 sade explicit "stoppa ECS-services"
men AWS-API-realiteten avvisar custom SSM-documents för Fargate. Second
amendment klargör tolkningen utan att supersedera huvudbeslutet.

## F2-P4 — cost-recovery-runbook full utbyggnad (1 commit)

Stub från F2-P3 utbyggd till komplett incident-response-runbook. Följer
failed-access-anomaly-mönstret (Steg 1-N + Severity-tabell + Pre-apply-
verifiering + Drift-tester).

### docs/runbooks/aws-cost-recovery.md

- Decision-tree (ASCII) för incident-klassificering
- 5 steg: Klassificera → Bedrock-validering → Baseline-verifiering →
  Forensik → Incident-rapport
- Manuell ECS scale-down + Återställning R1-R5
- Post-mortem-template med GDPR Art. 33-bedömning
- Test-procedur (säkert utan att brännas $50)
- Severity-tabell + SNS-subscription-flöde

### infra/scripts/cost-recovery/ (nytt)

PowerShell-scripts checked-in för en-knapps-respons:

- `stop-ecs-services.ps1` — desired_count=0 + 60s verify
- `restore-ecs-services.ps1` — detach deny (idempotent) + scale-up + 90s
  verify
- `README.md` — användning + säkerhet + test-procedur

Båda scripts: `$ErrorActionPreference = "Stop"`, exit-code-verifiering efter
varje `aws`-anrop, inga credentials hårdkodade.

Ingen CTO-konsult — ren docs-utbyggnad enligt etablerade mönster.

## F2-P6 — strict readiness-probe-split / TD-29 stängd (1 commit)

### Kod

- `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.7
  tillagt i `Directory.Packages.props` (Microsoft-paket, inte Xabaril)
- `src/JobbPilot.Api/HealthChecks/RedisHealthCheck.cs` — custom IHealthCheck
  (10 rader via `IConnectionMultiplexer.IsConnected` + `PingAsync`).
  Undviker Xabaril third-party-dep konsistent med STEG 13b-mönster "use
  platform features, not familiar tools".
- `Program.cs`: `AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres",
  tags: ["ready"]).AddCheck<RedisHealthCheck>("redis", tags: ["ready"])`
- `/api/live` (Predicate `_ => false`): liveness, alltid 200
- `/api/ready` (Predicate `Tags.Contains("ready")`): strict readiness, 503
  tills BÅDE Postgres + Redis svarar
- Legacy `/health` borttagen (ingen konsument refererade utöver Program.cs)

### Tester — 6 nya integration-tester

`tests/JobbPilot.Api.IntegrationTests/HealthChecks/HealthCheckEndpointsTests.cs`:

1. `ApiLive_ReturnsHealthy_WhenProcessIsUp` — 200 OK på `/api/live`
2. `ApiReady_ReturnsHealthy_WhenDatabaseAndRedisAreReachable` — 200 OK med
   Testcontainers Postgres + Redis aktiva
3. `ApiLive_DoesNotEvaluateRegisteredChecks` — anti-regression: predicate
   `_ => false` så svaret kommer under 500ms (ingen DB-roundtrip)
4. `ApiReady_IsAnonymouslyAccessible` — anti-regression mot framtida
   `RequireAuthorization`-glömska
5. `ApiLive_IsAnonymouslyAccessible` — samma disciplin
6. `LegacyHealthEndpoint_IsRemoved` — anti-regression mot oavsiktlig
   återinförande av `/health`

**Test-suite:** Api.IntegrationTests 217 → **223 PASS** (+6).

### ALB-konsekvens

`modules/alb/variables.tf` `health_check_path` default är redan `/api/ready`
(BUILD.md §15.4). Ingen Terraform-ändring krävs. Under rolling-deploys får
nya tasks INGEN trafik förrän DbContext-pool + Redis-multiplexer initierats
(typiskt 10-30s) — exakt det önskade beteende TD-29 motiverade.

### Disciplinmissar — xUnit v3 mtp-v2

xUnit v3 mtp-v2 har annan filter-syntax än xunit2:
- INTE `--filter` (det är ett `dotnet test`-arg som genererar Zero tests run)
- ÄR `-- --filter-class <FQN>` (mtp-CLI-arg)

xUnit1051 är treated as error i projektet — alla async-anrop måste ta
`TestContext.Current.CancellationToken`. 8 occurrences fixade innan tester
körde.

### TD-29 stängd

Flyttad från `docs/tech-debt.md` (Översiktstabell + full kropp) till
`docs/tech-debt-archive.md` med leverans-detaljer + ALB-konsekvens +
beslut-rationalet för custom RedisHealthCheck över Xabaril.

## CTO-konvergensprocessen — lärdom

Tre CTO-ronder för F2-P3 illustrerar värdet av iterativ konsultation när
AWS-API-realiteten avviker från CTO:s mental model. Lärdom dokumenterad i
ADR 0005 second amendment:

1. **Rond 1** — CTO valde det "rätta" arkitektoniskt (Lambda för
   imperativ-eller-deklarativ flexibilitet)
2. **Rond 2** — CC fångade att Lambda inte är native trigger för Budget
   Actions; CTO bytte till SSM Document
3. **Rond 3** — CC fångade via web-search att SSM Document också är
   begränsat till EC2/RDS; CTO valde att skippa hela ECS-stop-automatiseringen

**Memory `feedback_cto_decides_multi_approach`** validerad i praktiken: CC
rekommenderade INTE själv mellan ronderna utan presenterade trade-off-tabell
och frågade. CTO är decision-maker.

## Tekniska beslut värda att minnas

- **AWS Budget Actions API**-begränsningar (verifierade 2026-05-12):
  - `action_type` enum: `APPLY_IAM_POLICY` | `ATTACH_POLICY` | `RUN_SSM_DOCUMENT`
  - INGEN `INVOKE_LAMBDA`-action_type
  - `RUN_SSM_DOCUMENT` `action_sub_type` enum: bara `STOP_EC2_INSTANCES` |
    `STOP_RDS_INSTANCES`. INGA custom SSM-documents.
  - För Fargate-baserade ECS-services finns det därför INGEN native
    Budget Action för stop. Manuell via runbook är industri-default.

- **`aws_iam_role.description` regex-restriktion** (men inte
  `aws_iam_policy.description`): U+2014 EM DASH avvisas. Använd ASCII-
  bindestreck i IAM-roll-beskrivningar för säkerhets skull (även om policy-
  beskrivningar tillåter unicode).

- **Custom IHealthCheck > Xabaril** för enkla Redis-checks: 10 rader kod
  via `IConnectionMultiplexer.IsConnected` + `PingAsync()` undviker third-
  party-dep utan att förlora funktionalitet.

- **xUnit v3 mtp-v2 filter-syntax** är `dotnet test -- --filter-class <FQN>`,
  inte `dotnet test --filter <pattern>`. `--filter` parsas som okänt
  `dotnet test`-arg och genererar Zero tests ran.

- **Microsoft AddDbContextCheck** är Microsoft-paket (inte Xabaril) och
  pingar via `Database.CanConnectAsync()`. Inkluderar inte explicit SELECT 1.

## TD-status

- **TD-29 stängd.** Flyttad till archive med full leverans-detalj.
- Aktiva: **17** (var 18 vid session-start, -1 idag).
- Inga nya TDs lyfta. Allt fixat in-block per CLAUDE.md §9.6.

## Nästa session

P7 paginering + P8 JobTech-integration. Sannolika CTO-design-frågor:

- **P7 paginering:** cursor-based vs offset-based, ADR 0030-symmetri med
  PagedResult-pattern (TD-56), JobTech-API-paginerings-format alignment.
- **P8 JobTech-integration:** API-client (Polly resilience-policies?),
  caching-strategi (Redis vs in-memory), rate-limiting mot Platsbanken,
  data-staleness-policy, GDPR vid extern API-trafik.

Klas-startprompt genereras separat i `STARTPROMPT-FAS2-P7-P8.md` (gitignored
per `.gitignore` STARTPROMPT-mönster).

## Tidsuppskattning

~3h CC-tid effektivt (10:30–13:30 med lunch-paus i mitten). Klas AFK i ~1.5h
utan friktion — non-stop-flödet fungerar för F2-P3 + F2-P4 + F2-P6-batch.
