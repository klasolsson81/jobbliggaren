# Current work — JobbPilot

**Status:** **F2-P3 (Budget Actions terraform) komplett 2026-05-12 ~11:30. JobbPilotBedrockDeny + APPLY_IAM_POLICY Budget Action live i AWS dev (STANDBY). ADR 0005 second amendment (ECS-stop → manuell runbook) accepterad. Nästa: F2-P4 (runbook-utbyggnad) → F2-P6 (readiness-probe) → JobTech-features.**
**Senast uppdaterad:** 2026-05-12 (session-end efter F2-P3)
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md` (aktiva) + `docs/tech-debt-archive.md` (stängda)

---

## Aktivt nu — F2-P3 komplett, F2-P4/P6 nästa

**2026-05-12 fortsatt session** (~10:30–11:30) levererade F2-P3 Budget Actions
terraform-modul live mot AWS dev. Per ADR 0005 second amendment 2026-05-12
(ECS-stop som automatisk Budget Action skippad pga AWS-API-begränsning —
sekundärskydd via manuell runbook F2-P4).

### Levererat F2-P3-batch

| Commit | Innehåll |
|---|---|
| `0d66fe5` | feat(infra): terraform-modul `modules/budget_actions/` + iam_ecs-output + dev-wire |
| `ce3e013` | docs(adr): ADR 0005 second amendment + aws-cost-recovery runbook-stub |
| `f7bd9fc` | fix(infra): disciplinretur — em-dash i IAM-role description (AWS IAM regex avvisade U+2014) |

**AWS dev-state efter apply:**
- `aws_iam_policy.bedrock_deny` — `JobbPilotBedrockDeny` v1 (deny-overlay)
- `aws_iam_role.budget_action` — `jobbpilot-dev-budget-action-role` (least-priv Attach/DetachRolePolicy lockad till JobbPilotBedrockDeny + PolicyARN-condition)
- `aws_sns_topic.cost_anomaly` — `jobbpilot-dev-cost-anomaly` (KMS-encrypted via master-key)
- `aws_sns_topic_policy.cost_anomaly` — locked-down till budgets.amazonaws.com
- `aws_budgets_budget_action.attach_bedrock_deny` — `0115c684-4ede-4702-a4a7-fc69529da7bf`, APPLY_IAM_POLICY, 100% ACTUAL, AUTOMATIC, **STANDBY**

### CTO-konvergens (tre ronder)

F2-P3 designval krävde tre senior-cto-advisor-ronder pga successivt verifierade AWS-API-begränsningar:

1. **Rond 1** (`a314494fb60370436`): A4/B1/**C1**/D2/E1/F1 — Hybrid + APPLY_IAM_POLICY + SNS→Lambda + dedikerad topic + 80/100% + AUTOMATIC
2. **Rond 2** (`ad162f50dacbd0a0a`): C1' (Path γ) — SSM Automation Document istället för Lambda, efter discovery att Budget Actions saknar INVOKE_LAMBDA action_type
3. **Rond 3** (`a37fedf646b292a84`): **Väg III** — skippa ECS-stop som automatisk Budget Action, manuell via F2-P4-runbook. Efter web-search-verifiering att `RUN_SSM_DOCUMENT` Budget Action endast stödjer `STOP_EC2_INSTANCES`/`STOP_RDS_INSTANCES` — inga custom SSM-documents för Fargate

### Beslut-rationalet (rond 3)

- ECS Fargate ~$30/mån **fast kostnad** — inte skenrisk
- Bedrock-invocation är enda blowout-vektorn (täckt av primärskydd via APPLY_IAM_POLICY)
- Att bygga indirekta workarounds (SNS→Lambda via duplicerade budgetar) bryter proportionalitets-principen (Ford/Parsons/Kua 2017 — Fitness Functions)
- Manuell ECS-stop via runbook är industri-default för dev-miljöer (12-Factor §IX Disposability)

### Disciplinmissar fångade + fixade

1. **Em-dash (U+2014) i IAM-role description** → AWS IAM CreateRole ValidationError → fixad in-block med ASCII-bindestreck. 3 av 6 resurser skapades innan fel; re-apply efter fix kompletterade resterande utan problem.

### TD-status

- Nuvarande aktiva: 18 (oförändrat — ingen TD lyft, allt fixat in-block)
- F2-P4-runbook-utbyggnad är **egen batch**, inte TD

### API-yta + säkerhetsinvarianter

**Primärskydd (automatisk):**
- Budget Action triggar vid 100% ACTUAL av jobbpilot-monthly $50-budget
- Bifogar JobbPilotBedrockDeny på api-task-role via APPLY_IAM_POLICY
- Explicit Deny vinner över Allow per IAM-eval-logik — blockerar all `bedrock:Invoke*` / `Converse*`
- Reversibel: Budget Action auto-detachar vid budget-cycle-reset

**Sekundärskydd (manuell):**
- F2-P4-runbook `docs/runbooks/aws-cost-recovery.md` med manuell ECS scale-down + återställnings-procedur
- Stub-version levererad i F2-P3; full utbyggnad i F2-P4

### Vad återstår av Fas 2-prereqs

| Batch | Innehåll | Status |
|---|---|---|
| ~~F2-P1~~ | `registrations_open`-flagga | ✓ F2-P0e |
| ~~F2-P2~~ | Rate-limit-policies | ✓ F2-P0e |
| ~~F2-P3~~ | Budget Actions terraform + dev-apply | ✓ **F2-P3 idag** |
| **F2-P4** | Runbook `aws-cost-recovery.md` full utbyggnad | Stub klar — full TODO-lista i runbook |
| **F2-P6** | TD-29 readiness-probe-split (`/api/live` + `/api/ready` med DB+Redis-check) | Kvar |

Efter F2-P4 + F2-P6 → Fas 2 JobTech-features får startas (P7 paginering + P8 JobTech-integration).

### Pending operativt (Klas)

- (Valfritt) Sätt `cost_anomaly_alert_email` i `terraform.tfvars` + re-apply + AWS-mail-opt-in. Idag är SNS-topic skapad men inga subscriptions.
- (Senare) Drift-reconcile på `module.ecs.aws_ecs_service.api/worker` (task-def revisions :5 → :2 / :4 → :1) och `module.rds.aws_db_parameter_group` (rds.force_ssl apply_method) — undveks via `-target=module.budget_actions` vid F2-P3-apply.

---

## Tidigare session — F2-P0 komplett

**2026-05-12 lång session** (förmiddagen, ~08:00–10:30) levererade hela F2-P0
invitations/waitlist-batch (sub-batches a–f) per ADR 0005 amendment 2026-05-12.

### Levererat denna session

| Batch | Commit | Innehåll |
|---|---|---|
| ADR | `6f0b89d` | ADR 0005 → Accepted + invitations/waitlist amendment + CTO-rapport |
| F2-P0a | `cbe4163` | Domain: Invitation + WaitlistEntry aggregates (39 nya tester) |
| F2-P0b | `0c58438` | EF mappings + migration (invitations, waitlist_entries) |
| F2-P0c | `ebdf1f1` | Application: 5 commands + handlers + validators (24 tester) |
| F2-P0d | `bcc114d` | ConsoleEmailSender + InvitationTokenGenerator (8 tester) |
| F2-P0d (disciplinretur) | `34398d1` | AWSSDK.SimpleEmailV2 + SesEmailSender, TD-69 stängd |
| F2-P0e | `64b7e2a` | 4 endpoints + 2 rate-limits + kill-switch + IFeatureFlags (12 integration-tester) |
| F2-P0e (gitleaks) | `6d2dcf3` | Fingerprint för DefaultTestPassword |
| Security | `74b152e` | `.gitignore` för STARTPROMPT + terraform .out |
| F2-P0f | `b5666d1` | `/vantelista` Next.js publik signup-sida (5 tester) |

**Total testsuite:** Domain 202 (+39), Application 249 (+32), Architecture 32,
Api.IntegrationTests 217 (+12), Web Vitest 239 (+5). +88 nya tester.

### API-yta live

**Public (anonym):**
- `POST /api/v1/waitlist` — anonym signup, rate-limit 3/24h/IP, kill-switch
- `POST /api/v1/auth/redeem-invitation` — token-redemption, rate-limit 5/h/IP, kill-switch

**Admin (SuperAdmin via Postman/curl tills Fas 6 UI):**
- `POST/GET /api/v1/admin/invitations` + `POST /{id}/revoke`
- `GET /api/v1/admin/waitlist` + `POST /{id}/approve` + `POST /{id}/reject`

**Frontend:** `/vantelista` (civic-utility-copy, server action, GDPR-notis)

### Säkerhetsinvarianter etablerade

- Kill-switch `FeatureFlags.RegistrationsOpen=false` → 503 på båda public endpoints
- HMAC-SHA256 opaque token (32 bytes random + server-secret) + single-use via xmin
- Email kommer från Invitation, inte command body (skydd mot token-stöld)
- Rate-limits per IP på alla publika endpoints
- Partial unique index på waitlist_entries.email WHERE status='Pending'
- Admin-endpoints skyddade av `[Authorize(Roles=SuperAdmin)]` + AdminAuthorizationBehavior

### Disciplinmissar fångade + fixade

1. **TD-69** felaktigt lyft för SES → Klas fångade → disciplinretur, stängd samma dag
2. **DI splittad från handlers** i F2-P0c → CI fångade → fix-forward i F2-P0d
3. **`git add -A`** med STARTPROMPT + `secrets.out` → Klas fångade INNAN push → soft-reset + `.gitignore`-fix

### Memory uppdaterat

- `feedback_nonstop_with_pr_reports.md` — non-stop med PR-rapporter
- `feedback_di_with_handlers_same_commit.md` — DI + handlers samma commit

### TD-status

- **TD-69** (SesEmailSender) — stängd samma dag (disciplinretur)
- Nuvarande aktiva: 18 (oförändrat sedan F2-kickoff)

### Vad återstår av Fas 2-prereqs

| Batch | Innehåll | Status |
|---|---|---|
| ~~F2-P1~~ | `registrations_open`-flagga | ✓ levererad i P0e |
| ~~F2-P2~~ | Rate-limit-policies | ✓ levererad i P0e |
| **F2-P3** | Terraform Budget Actions $50/mån + Bedrock-IAM-auto-disable + ECS-stop + dev-apply | Kvar |
| **F2-P4** | Runbook `docs/runbooks/aws-cost-recovery.md` | Kvar |
| **F2-P6** | TD-29 readiness-probe-split (`/api/live` + `/api/ready` med DB+Redis-check) | Kvar |

Efter dessa → Fas 2 JobTech-features får startas (P7 paginering + P8 JobTech-integration).

### Pending operativt (Klas)

- Verifiera mottagar-emails i AWS SES-konsolen för klasskamrater (sandbox-mode)
- DKIM + SPF DNS innan public launch
- `terraform apply` av Budget Actions efter F2-P3-leverans

---

## Tidigare session — Fas 2-kickoff med ADR 0005-design

**2026-05-12 Fas 2-kickoff:** senior-cto-advisor invokerad för ADR 0005-
designval. CTO-beslut: Alternativ C (invite-only public beta med hård cap) +
amendment för invitations + waitlist (Klas inputs efter Runda 1). Klas-GO
mottagen. ADR 0005 flippad PROPOSED → ACCEPTED.

**Granskningstrail:** `docs/reviews/2026-05-12-fas2-cto-adr0005.md` (båda
CTO-rundor verbatim).

### F2-P0 impl-plan (sub-batches, ~15-20h CC-tid)

| Batch | Innehåll | Status |
|---|---|---|
| F2-P0a | Invitation + WaitlistEntry aggregates + tests | Nästa |
| F2-P0b | EF mappings + migration | Planerad |
| F2-P0c | 5 commands + validators + tests | Planerad |
| F2-P0d | `IEmailSender` + `SesEmailSender` + svenska templates | Planerad |
| F2-P0e | API-endpoints + 3 rate-limit-policies + kill-switch | Planerad |
| F2-P0f | `/vantelista`-publik sida (Next.js RSC) | Planerad |

Efter F2-P0: F2-P1 (`registrations_open` feature-flag) → F2-P3 (Budget
Actions terraform) → F2-P4 (runbook) → F2-P6 (readiness-probe) → JobTech-
integration startar.

### Tidigare session — Fas 1-rensning komplett

Lång CC-session 2026-05-11 ~21:00 → 2026-05-12 ~08:00. Levererade hela
Fas 1-rensningens återstående batches (B–F) plus disciplinretur + TD-67 +
TD-25 + TD-68 (med dev-apply).

Se session-log [`2026-05-12-0800-fas1-rensning-komplett-td67-td68.md`](sessions/2026-05-12-0800-fas1-rensning-komplett-td67-td68.md) för full historik.

### Levererat denna session

| Område | Stängda TDs | Notering |
|---|---|---|
| Batch B (shadcn-first form-controls) | TD-41, TD-57 | CTO-beslut: shadcn Select + Input-primitive default |
| Batch C (a11y-pass) | TD-1, TD-2, TD-40 | Skip-link + CardTitle h3 + asChild + Slot.Root |
| Batch D (UX-pass /mig) | TD-3, TD-4, TD-5 | Stum tom-state + userId borttaget + JSDoc |
| Batch E (me-flöde fullstack) | TD-6, TD-28 | Klas-Alt1: utöka till fullstack med ny `/auth/verify`-endpoint |
| Batch F (cross-user-isolation) | TD-12 | 7 integration-tester för Application |
| Disciplinretur | TD-65, TD-66 | Reparation av disciplinmissar (Playwright E2E + Resume/Me isolation) |
| TD-67 (ADR 0031) | TD-67 | IFailedAccessLogger + strukturerad logging + ADR 0031 |
| TD-25 (resilient loop) | TD-25 | HardDeleteAccountsJob per-konto try/catch |
| TD-68 (CloudWatch) | TD-68 | Terraform-modul + dev-apply genomförd |

**Totalt:** 16 TDs stängda. **45 stängda** totalt, **18 aktiva kvar** (alla Fas 2+ eller Trigger-baserade).

### Fas 1-status

- `docs/steg-tracker.md` Fas 1: "Klar 2026-05-11" (admin-audit) → uppdaterad i denna session med Fas 1-rensningens täckning.
- **Fas 1 Minor-sektionen i tech-debt.md är TOM.** Alla aktiva TDs är Fas 2+ (PII-encryption, AI-kostnadstak), Fas 4 (AI), Fas 6 (admin-impersonation), Trigger-baserade (i18n, error-summary, paginering), eller Opportunistiska (TD-20).
- Inga blockers från TD-listan för Fas 2-start.

### Säkerhetsinvarianter etablerade

- Cross-user-isolation maskinellt bevakad: Application + Resume + JobSeeker
  (16 integration-tester totalt)
- BOLA-enumeration-detektering live i dev (CloudWatch metric filter + SNS-alarm)
- Defense-in-depth re-auth före DELETE /me (POST /auth/verify)
- Resilient hard-delete-job (per-konto try/catch)
- GDPR Art. 17 + Art. 32 implementationsbevisad i tester

### Tester (full svit grön)

| Suite | Antal |
|-------|-------|
| Backend Domain.UnitTests | 163 |
| Backend Application.UnitTests | 217 |
| Backend Architecture.Tests | 32 |
| Backend Api.IntegrationTests | +21 nya (VerifyCredentials, Apps/Resumes/Me isolation) |
| Frontend Vitest | 234 |
| tsc --noEmit | grön |
| dotnet format | ren |

### AWS-deploy denna session

| Resurs | Status |
|---|---|
| `jobbpilot-dev-secops-anomaly` SNS-topic | Live (KMS-encrypted) |
| `failed_access_attempt` metric filter | Live (api log-group) |
| `jobbpilot-dev-failed-access-anomaly` alarm | INSUFFICIENT_DATA (väntar data) |
| `jobbpilot-dev-api-log-pipeline-health` alarm | INSUFFICIENT_DATA (väntar data) |

### Pushed commits denna session (21 st)

| Commit | Scope |
|--------|-------|
| `74d28ad` | feat(web): Batch B shadcn-first form-controls |
| `2513580` | docs(tech-debt): Batch B stängningar |
| `006e3e1` | feat(web): Batch C a11y-pass |
| `bc91ff1` | docs(tech-debt): Batch C stängningar |
| `f1a82be` | feat(web): Batch D UX-pass /mig |
| `5623d01` | docs(tech-debt): Batch D stängningar |
| `9f74efb` | feat: Batch E me-flöde fullstack |
| `fdd2673` | docs(tech-debt): Batch E stängningar |
| `80a6c3c` | chore(test): VerifyCredentialsTests pattern-match |
| `4310a8e` | chore(security): .gitleaksignore fingerprints |
| `b4bb60f` | test(applications): Batch F cross-user-isolation |
| `d3cbf99` | docs(tech-debt): Batch F stängningar |
| `62e8453` | test: disciplinretur TD-65 + TD-66 |
| `71b7c9f` | docs(tech-debt): TD-65 + TD-66 stängda |
| `861a7cf` | feat(security): TD-67 + ADR 0031 |
| `ba4f36f` | docs(tech-debt): TD-67 stängd |
| `eed6cc2` | fix(worker): TD-25 resilient loop |
| `80c1f06` | docs(tech-debt): TD-25 stängd |
| `70ca42b` | feat(infra): TD-68 CloudWatch security-alarms |
| `2f66b4f` | docs(tech-debt): TD-68 Pågående |
| `45fb7f7` | docs(tech-debt): TD-68 stängd efter dev-apply |

### Lärdom sparad i memory

- `memory/feedback_td_lifting_discipline.md`:
  TD-lyftningar måste pressas mot §9.6-kriterier även om CTO/auditor föreslår.
  "Scope-disciplin per batch" eller "+1-2h CC-tid" är INTE legitima skäl.

### Nya ADRs

- **ADR 0031** — Failed cross-user access detection: strukturerad loggning +
  CloudWatch-aggregat. Bevarar ADR 0022 immutable.

---

## Nästa session — Fas 2-kickoff med ADR 0005-design

Per CLAUDE.md §9.2 är fas-skifte ett strategiskt beslut. Klas har gett GO
för Fas 2-start men Fas 2 är blockerad av prereqs per BUILD.md §18 +
`docs/steg-tracker.md` fotnot ²:

1. **ADR 0005** (go-to-market + kostnadsskydd-strategi) ska beslutas
2. **Budget Actions** + `registrations_open`-flagga implementerade
3. **Rate-limiting-utvidgning** för publika endpoints
4. **Runbook** `docs/runbooks/aws-cost-recovery.md` skapad

Startprompt för nästa /clear-session: `STARTPROMPT-FAS2-KICKOFF.md`
(skapas vid session-end denna session).

### Pending operativa uppgifter

- (Valfritt) Sätt `secops_alert_email` i dev `terraform.tfvars` +
  re-apply + AWS-mail-opt-in. Idag är SNS-topic skapad men inga
  subscriptions.
- (Valfritt) Drift-test av TD-68 anomaly-alarm: registrera 2 users,
  gör cross-user-anrop, verifiera att alarm triggar inom ~60s.
- (Senare) Prod-invokation av TD-68-modulen när prod-ECS-stack levereras.

### Förbud (default — kan lyftas av Klas)

- **INGA Fas 2-JobTech-features** utan ADR 0005-beslut + kostnadsskydd
- **INGA STEG-starter** utan Klas-GO
- **INGA ändringar** av `BUILD.md` / `CLAUDE.md` / `DESIGN.md` utan explicit instruktion
- **INGA prod-deploys** utan Klas-godkännande
