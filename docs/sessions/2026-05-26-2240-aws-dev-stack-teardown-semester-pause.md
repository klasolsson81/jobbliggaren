---
session: AWS dev-stack teardown — semester-pause (Fas B per ADR 0066)
datum: 2026-05-26
slug: aws-dev-stack-teardown-semester-pause
status: levereras-i-PR
commits:
  - (commits skapas vid commit-fas i denna session — fylls i post-push)
pr:
  - "(PR mot main skapas i denna session — URL fylls i post-create)"
tags:
  - (inga taggar — denna session är infra-teardown, ingen deploy)
---

# AWS dev-stack teardown — semester-pause (Fas B per ADR 0066)

## Sammanfattning

Klas-prompt 2026-05-26 efter MVP-demo: stoppa AWS-kostnaderna ($115.88/mån
forecast → ~$2/mån) inför semester. VPS-uppstart planerad 0-6 mån-horisont.
Reversibel teardown — prod-baseline (Route 53 + KMS field-key + CloudTrail +
IAM Identity Center + Budgets) bevarad för enkel återstart. Vercel-frontend
pausas parallellt (Klas-uppgift).

Strategin **Fas B** per CTO-rond `a547e7ddc12dd6a81` 2026-05-26: destroy
`infra/terraform/environments/dev/`, bevara prod-baseline + bootstrap.
**Klas-direktiv "stoppa allt"** efter discovery-rapport: ren destroy utan
final-snapshot, ECR `force_delete=true`, ingen pg_dump (waitlist tom).
Reversibilitet > crypto-erasure (KMS-key bevarad, automated backups
auto-expire 14d).

## Mål

1. Skapa feature-branch `chore/aws-dev-stack-teardown-2026-05-26`.
2. Discovery-rapport till Klas på `deletion_protection`-propagering, ECR
   `force_delete`-status, RDS final-snapshot, AWS SSO-config på laptop.
3. Skapa ADR 0066 via adr-keeper (Väg A — ADR 0050 är annan strategi).
4. Edit Terraform: `deletion_protection=false` + `skip_final_snapshot=true` +
   ECR `force_delete=true` i environments/dev/main.tf + tfvars-dummy-image-tags
   för validation-pass vid destroy.
5. Arkivera TD-91 + TD-94 som obsoleta under semester-pause.
6. Setup AWS CLI + Terraform + SSO-config på laptop (stationära är primär
   maskin men laptop:en behövs för CC-verifiering).
7. Skapa PR mot main per ADR 0065 PR-flöde.
8. Klas kör `terraform apply` (deletion_protection-flip) + `terraform destroy`
   från stationära post-merge.

## Fas-flöde

| Fas | STOPPs | Klas-utfall |
|-----|--------|-------------|
| Förkrav + mandatory reads | 0 | (CC-egenarbete) |
| AWS CLI + Terraform install via winget | 0 | (Klas-direktiv "installer aws cli här med") |
| Discovery-rapport (deletion_protection + ECR + snapshot + SSO) | 1 | Klas: "Vi kan stoppa allt på jobbpilot ju nu, dvs behöver inget bakgrundsjobb, ingen snapshot osv." + "sätt upp AWS SSO" |
| adr-keeper → ADR 0066 (Väg A: ADR 0050 EJ supersedad) | 0 | (auto efter discovery-GO) |
| Terraform-edits (moduler + dev + tfvars) | 0 | (auto efter ADR-GO) |
| AWS SSO config-template + Klas hämtar SSO start URL | 1 | (Pending Klas-svar med URL) |
| TD-arkivering (TD-91 + TD-94) | 0 | (auto per CTO-Konsekvenser/Positiva) |
| Docs-sync (current-work + denna session-log) | 0 | (CC-egenarbete) |
| code-reviewer-rond | 1 | Pending |
| PR-push + Klas-merge | 1 | Pending |
| Klas terraform apply + destroy från stationära | 2 | Pending (post-merge) |

## CTO-domar

### `senior-cto-advisor` rond `a547e7ddc12dd6a81` 2026-05-26

Verbatim besluts-substans i ADR 0066. Sammanfattning av 6 beslut:

1. **Fas B** — destroy dev-stack, bevara prod-baseline + bootstrap. Avvisade:
   Fas A (60$/mån kvar = uppfyller inte målet), Fas C (förlust av Route 53-zone
   + KMS för $1/mån-besparing), Fas D (irreversibel "kanske räcker" inte).
2. **Skip pg_dump** — waitlist tom (Klas-bekräftat), korpus = publikt JobTech-
   API gratis re-import.
3. **Skip RDS final snapshot + ECR force_delete=true** — Klas-direktiv
   "stoppa allt".
4. **Skip KMS-key-deletion** — reversibilitet > crypto-erasure för 0-6 mån-
   horisont; EDPB CEF 2025 backup-overwrite-cycle godkänd Art. 17-täckning.
5. **Vercel pause** via Klas-dashboard (CC har ingen Vercel-access).
6. **AWS-konto kvar öppet** — Fas D (close account) avvisad per
   Saltzer/Schroeder fail-safe defaults.

Källor citerade: Ford/Parsons/Kua 2017 *Building Evolutionary Architectures*
kap. 4 (reversibility), Beck/Fowler 2004 *XP Explained* kap. 17 (YAGNI),
Hunt/Thomas 1999 *Pragmatic Programmer* kap. 7 (KISS), Saltzer/Schroeder 1975
IEEE-paper (fail-safe defaults), EDPB Guidelines 4/2019 + EDPB CEF 2025,
GDPR Art. 5(1)(c) + Art. 17 + Art. 32, AWS RDS-/KMS-docs, CLAUDE.md §9.6 +
§1.5, ADR 0024/0036/0044/0050/0065.

Klas-STOPP ej behövd per CLAUDE.md §9.6 (entydigt motiverad mot principer +
verbatim Klas-GO i prompt-bilaga + Klas-direktiv "stoppa allt" 2026-05-26
samma session).

## Leveranser

### A. ADR 0066 (Accepted)

**Fil:** `docs/decisions/0066-aws-dev-stack-teardown-semester-pause.md` (359
rader). Status Accepted 2026-05-26. Skapad via adr-keeper-agent (livscykel-
not: medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konvention för
denna session per Klas-direktiv).

**Cross-refs:** ADR 0005 (relevans-skifte ej supersession), 0024 (RDS backup
14d), 0036 (prod-stack deferred), 0044 (CI-aggregat), 0049 (KMS field-key
bevarad), **0050 (Hetzner-exit Proposed — EJ supersedad, komplementär)**,
0064 (Worker-Redis återställs vid återstart), 0065 (PR-flöde).

**README-index uppdaterad** med rad för ADR 0066.

### B. Terraform-edits för clean destroy

**Modul-utbyggnad** (defaults oförändrade — säkert default-läge bevarat):

| Fil | Ändring |
|-----|---------|
| `infra/terraform/modules/ecr/variables.tf` | Ny `force_delete` variable (default false) |
| `infra/terraform/modules/ecr/main.tf` | `force_delete = var.force_delete` på aws_ecr_repository |
| `infra/terraform/modules/rds/variables.tf` | Ny `skip_final_snapshot` variable (default false) |
| `infra/terraform/modules/rds/main.tf` | `skip_final_snapshot = var.skip_final_snapshot` + `final_snapshot_identifier = null` när skip=true |

**Environments/dev-overrides** (för teardown):

| Fil | Ändring |
|-----|---------|
| `infra/terraform/environments/dev/main.tf` | RDS-modul: `deletion_protection = false` + `skip_final_snapshot = true`. ECR-modul: `force_delete = true`. Båda med ADR 0066-kommentar + "återställ vid återstart"-instruktion. |
| `infra/terraform/environments/dev/terraform.tfvars` | `api_image_tag = "teardown"` + `worker_image_tag = "teardown"` (passes variable-validation `length > 0 && != "latest"` vid `terraform destroy`/plan). |

### C. TD-arkivering

- **TD-91** (RDS param-group `apply_method`-drift) — flyttad från
  `tech-debt.md` till `tech-debt-archive.md` med stängningsnotat "obsolet
  under teardown — drift försvinner med resursen". Lagt till i Stängda-
  tabellen.
- **TD-94** (`ListJobAdsQuery` perf p50 1.2s) — flyttad till arkivet med
  stängningsnotat "obsolet under teardown — query slutar köras; re-öppna
  vid återstart om rot kvarstår". "Major — F6 P5 Punkt 2-fas-stängning"-
  sektionen i tech-debt.md markerad tom 2026-05-26 med ADR 0066-referens.
- **TD-95** ("Senaste sökning" tom) — redan stängd 2026-05-24 i föregående
  session (F6 P5 P4 svans-PR4). Ingen åtgärd.

### D. AWS CLI + Terraform install + SSO-config

- AWS CLI v2.34.53 installerad via `winget install Amazon.AWSCLI` (default
  path `C:\Program Files\Amazon\AWSCLIV2\aws.exe`)
- Terraform 1.15.4 installerad via `winget install Hashicorp.Terraform`
  (winget-PATH-modifikation kräver shell-restart)
- `~/.aws/config` skapad med template (`[sso-session jobbpilot]` +
  `[profile jobbpilot]`) — **SSO start URL pending Klas**

## Reviews

| Reviewer | Fynd | Resolution |
|----------|------|------------|
| `senior-cto-advisor` (a547e7ddc12dd6a81) | 6 beslut + 4 avvisade alternativ | Klas-GO verbatim i prompt; ADR 0066 transkriberat |
| `adr-keeper` (abf2689a768c10535) | ADR 0066 levererad + README-uppdaterad + cross-ref-verifierad (inga andra ADRs påverkade) | OK |
| `code-reviewer` | Pending före PR-push | Pending |

Inga `docs/reviews/`-filer skapade i denna session — agent-rapporter levereras
inline i PR-body per ADR 0065 + CLAUDE.md §9.2.

## Commits

Commits skapas vid commit-fas (post-docs-sync) i denna session. Förväntad
sekvens (egna logiska commits per CLAUDE.md §1.5 step 4):

1. `chore(infra): lägg till force_delete (ECR) + skip_final_snapshot (RDS) i moduler (ADR 0066)`
2. `chore(infra): flip RDS deletion_protection=false + skip_final_snapshot=true + ECR force_delete=true i dev (ADR 0066)`
3. `chore(infra): tfvars teardown-placeholders för api/worker_image_tag (ADR 0066)`
4. `docs(adr): 0066 — AWS dev-stack teardown 2026-05-26 — semester-pause (Fas B)`
5. `docs(tech-debt): arkivera TD-91 + TD-94 som obsoleta under teardown (ADR 0066)`
6. `docs(sessions): current-work + session-log för AWS dev-stack teardown 2026-05-26`

Eventuellt samlas commits till färre om de logiskt hör samman (terraform-
edits kan vara en commit).

## Beslut + detours

- **AWS CLI/Terraform install lokalt** — Klas-direktiv "installer aws cli här
  med" efter att han noterat att stationära har CLI:n men inte laptopen.
  Använde `winget` (PowerShell-blockad, men `winget` exponeras direkt på
  cmd-nivå i bash). Default path `C:\Program Files\Amazon\AWSCLIV2\aws.exe`
  — full path använd i denna session pga PATH ej refreshad.
- **AWS SSO config-template med placeholder** — `~/.aws/config` skapad
  preventivt med `REPLACE_WITH_SSO_START_URL` så Klas bara behöver svara med
  URL:n när han hittat den i IAM Identity Center → Settings. Snabbare än
  fram-och-tillbaka. Login kommer fail med "Invalid start url" tills URL
  fyllts i (Klas-test bekräftade detta).
- **`api_image_tag = "teardown"`** — variable-validation kräver
  `length > 0 && != "latest"`. För `terraform destroy` (som ändå raderar
  task-defs) behövs giltigt värde för att plan/destroy ska passera
  validation. Dummy-värdet signalerar tydligt att tfvars är i teardown-state.
  Vid återstart: TA BORT raderna (deploy-workflow sätter via `-var`).
- **ADR 0050 EJ supersedad av 0066** — Klas-direktiv via CC-rek: 0050 är
  permanent provider-exit till Hetzner med KMS-rehoming-blocker; 0066 är
  temporär semester-pause mot samma AWS-yta. Olika strategier, olika
  triggers. Cross-ref i 0066:s Relaterade-sektion räcker. 0050 status
  "Proposed" bevarad.
- **TD-94 + TD-91 arkivering** — CTO Konsekvenser/Positiva listade dessa
  som "obsoleta under semester-pause". Inte definitivt stängda — re-öppnas
  vid återstart om rot kvarstår.

## Disciplin

- **Inga TDs lyfta** denna session — alla fynd antingen fixade in-block
  (Terraform-edits) eller pressade som obsoleta (TD-91 + TD-94).
- `senior-cto-advisor`-besluten verbatim från prompt-bilaga (CTO-rond
  utförd av webb-Claude pre-session); ingen ny CTO-rond behövdes (entydigt
  CTO-beslut + Klas-direktiv "stoppa allt").
- `adr-keeper` invokerad innan ADR 0066-skapande per CLAUDE.md §9.2.
- `code-reviewer` invokeras innan PR-push.
- Klas-STOPP respekterad vid varje irreversibel övergång (SSO-URL pending,
  Klas-GO för apply/destroy pending).
- Explicit pathspec på alla edits (ingen `git add -A`/`git add .`).
- `.claude/settings.json` aldrig committad (aldrig touchad).
- **PR-flöde per ADR 0065** — feature-branch `chore/aws-dev-stack-teardown-
  2026-05-26`, PR mot main, docs-sync i samma PR.
- AWS SSO-URL **känslig**: Klas postar i chatten, CC skriver in i lokal
  `~/.aws/config` (gitignored), aldrig committad.

## Pending Klas-operativt

1. **Skicka SSO start URL** så CC kan fylla i `~/.aws/config` och ge
   PS-kommando för `aws sso login --profile jobbpilot`.
2. **Granska PR-diff** i GitHub-vyn innan merge.
3. **Merge PR** efter `ci`-aggregatet grönt.
4. **Köra `terraform apply` (deletion_protection-flip) från stationära** —
   verifierar plan-output verbatim innan auto-approve. CC ger exakta
   PS-kommandon.
5. **Köra `terraform destroy` från stationära** — verifierar destroy-plan
   verbatim. Förväntad apply-tid 20-40 min (NAT/RDS långsamma).
6. **Vercel pause** via dashboard (https://vercel.com/dashboard → projekt
   jobbpilot → Settings → pause production deployments).
7. **Cost Explorer-verify** 24-48h post-destroy: forecast next month <$10
   (target ~$2). Om högre: STOPP + ny CC-session.

### M-1 fynd från code-reviewer (a4b6c3fc3e227f238 2026-05-26) — återstarts-villkor

8. **Återstart inom 7d av Secrets Manager-secrets:** alla 4 Secrets
   Manager-secrets i dev-stacken har `recovery_window_in_days = 7` (default
   för `aws_secretsmanager_secret`):
   - `jobbpilot/dev/db/app-connection-string` (`environments/dev/main.tf:101`)
   - `jobbpilot/dev/db/hangfire-storage-connection-string` (`environments/dev/main.tf:112`)
   - Redis auth-token-secret (`modules/redis/main.tf:43-44`)
   - Redis connection-string-secret (`modules/redis/main.tf:73-74`)

   Vid `terraform destroy` schedule:as dessa för deletion 7d ut. **Om Klas
   återstartar dev-stacken inom 7d-fönstret** misslyckas `terraform apply`
   med `InvalidRequestException: A resource with the ARN already exists and
   is scheduled for deletion` eftersom secret-namnen är "ockuperade" i
   PendingDeletion-state.

   **Två lösningar vid återstart inom 7d:**
   - **(a) Restore-secrets-pre-apply:** `aws secretsmanager restore-secret --secret-id <name> --profile jobbpilot --region eu-north-1` för alla 4 namnen, sen `terraform apply`.
   - **(b) Vänta ut fönstret:** vänta 7d efter destroy, sen `terraform apply`. AWS auto-raderar då secrets och namnen blir tillgängliga igen.

   Återstart efter 7d-fönstret = ingen åtgärd behövs. Vid faktisk
   återstart: dokumentera i återstarts-runbook (skrivs vid återstart per
   ADR 0066 §Mitigering).

   **Inget akut att göra nu** — observation flaggad för framtida session.

## Nästa

- Klas merger PR
- Klas kör terraform-flöden från stationära (apply + destroy)
- Klas pauser Vercel via dashboard
- 24-48h post-destroy: Cost Explorer-verifiering
- (post-semester) ny CC-session för VPS-uppstart eller ADR 0050-genomförande
