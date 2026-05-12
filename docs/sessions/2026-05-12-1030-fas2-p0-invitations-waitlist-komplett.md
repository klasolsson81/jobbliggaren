---
session: F2-P0 invitations/waitlist-flow komplett
datum: 2026-05-12
slug: fas2-p0-invitations-waitlist-komplett
status: Klar
commits:
  - 6f0b89d  # ADR 0005 → Accepted + amendment + CTO-rapport
  - cbe4163  # F2-P0a Domain aggregates
  - 0c58438  # F2-P0b EF mappings + migration
  - ebdf1f1  # F2-P0c Application commands + handlers
  - bcc114d  # F2-P0d ConsoleEmailSender + token-gen
  - 34398d1  # F2-P0d disciplinretur (SES + TD-69 stängd)
  - 64b7e2a  # F2-P0e endpoints + rate-limits + kill-switch
  - 6d2dcf3  # F2-P0e gitleaks fingerprint
  - 74b152e  # .gitignore security-fix
  - b5666d1  # F2-P0f /vantelista Next.js
---

# Session 2026-05-12 (förmiddag) — F2-P0 komplett

## Mål

Leverera hela F2-P0 (invitations + waitlist backend + frontend) per ADR 0005
amendment 2026-05-12. Sub-batches a–f i en non-stop arbetsflöde (Klas-feedback
`feedback_nonstop_with_pr_reports`).

## Vad som blev klart

10 commits pushade till `main`. +88 tester (Domain 202, Application 249,
Architecture 32, Api.IntegrationTests 217, Web Vitest 239). Hela invitation-
flödet fungerar end-to-end:

1. Anonym besökare → `POST /api/v1/waitlist` → WaitlistEntry skapad,
   bekräftelsemejl skickat (Console eller SES per config)
2. Admin → `POST /api/v1/admin/waitlist/{id}/approve` → Invitation skapad
   atomically + email med plaintext-token skickat
3. Mottagare → `POST /api/v1/auth/redeem-invitation` → User + JobSeeker
   skapade i samma UoW, session returnerad
4. Kill-switch `FeatureFlags.RegistrationsOpen=false` blockerar både public
   endpoints med 503

## Domänmodell

**Två separata aggregates** per CTO-beslut (Evans 2003 kap. 6, Vernon 2013
kap. 10): olika livscykler, olika consistency boundaries.

- `Invitation`: Status (Pending|Redeemed|Expired|Revoked), Origin
  (DirectInvite|WaitlistApproved), TokenHash (HMAC-SHA256), xmin för single-
  use concurrency
- `WaitlistEntry`: Status (Pending|Approved|Rejected), ResultingInvitationId
  (FK till Invitation vid Approved)

Båda vägar konvergerar på samma redemption-endpoint. Origin-fält bevarar
audit-spår.

## Tekniska beslut värda att minnas

- **Opaque tokens, inte JWT** (deprecated per ADR 0017/0018) eller sessions
  (annan livscykel, SRP-brott). 32 bytes RandomNumberGenerator → URL-safe
  base64 (256 bits entropi per OWASP ASVS V3.7) + HMAC-SHA256 hex.
- **Email kommer från Invitation, inte command body** vid redemption —
  skydd mot token-stöld där angripare lurar offer klicka länk + tar över
  offers konto med eget email.
- **`registrations_open` som kill-switch** (inte normal-flow-regulator) —
  semantiken skiftade per amendment. Default false (stängd-by-default).
- **WithWebHostBuilder + FixedFeatureFlags-stub** för kill-switch-tester
  (env-var-flipping mot IOptionsMonitor fungerar inte efter app-start).
- **Admin-UI till Fas 6** (CTO-snitt) — Klas använder Postman/curl/Bruno
  under Fas 2–5. ~1 invite/vecka för 20 användare på 20 veckor = YAGNI för
  UI nu.
- **AWS SES via IAmazonSimpleEmailServiceV2** + AWSSDK.Core 4.0.6.1 pinning
  för GHSA-9cvc-h2w8-phrp. Sandbox-mode räcker för klasskamrat-tester;
  Klas verifierar mottagar-emails manuellt i AWS-konsolen.

## Disciplinmissar (3 st — alla fixade)

### 1. TD-69 felaktigt lyft för SES

Lyfte TD-69 i F2-P0d med motiveringen "AWSSDK NuGet kräver Klas-GO +
domain verification är ops". Klas påpekade att detta inte är legitim
TD-lyft per §9.6 + memory `feedback_td_lifting_discipline`:
- NuGet-GO är §9.2-mikrostop, inte funktion-dependency
- SES sandbox-mode räcker → ingen blocker

**Åtgärd:** stängde TD-69 samma dag via `34398d1`. Disciplinretur-historik
i `tech-debt-archive.md`.

### 2. DI splittad från handlers (F2-P0c)

Splittrade handlers (commit A) från DI-registrering (commit B). Mellan
dessa två commits var solution i broken state — CI:s integration-tester
föll med `Unable to resolve service for type IInvitationTokenGenerator`.
Min lokala pre-push fångar inte detta (kör bara unit-tester, inte
integration). Fix-forward i F2-P0d.

**Åtgärd:** ny memory `feedback_di_with_handlers_same_commit` så framtida
CC vet att DI-registrering MÅSTE vara samma commit som nya handlers.

### 3. `git add -A` med känsliga untracked-filer

Använde `git add -A` i F2-P0e — inkluderade STARTPROMPT-temp-filer och
terraform `*.out`-artefakter (varav `secrets.out` potentiellt med riktiga
AWS-secrets). Klas fångade INNAN push. CLAUDE.md §6.1 är explicit mot
detta — disciplinmiss på grundregel.

**Åtgärd:** soft-reset + recommit utan dessa filer. `.gitignore`-uppdatering
i `74b152e` så det inte upprepas (STARTPROMPT-*.md + infra/terraform/**/*.out).

## Memory uppdaterat

- `feedback_nonstop_with_pr_reports.md` — Klas vill non-stop arbete,
  STOPP bara efter varje commit-batch
- `feedback_di_with_handlers_same_commit.md` — DI + handlers samma commit
  (CI fångar broken state mellan splittrade commits)

## TDs

- **TD-69** stängd (samma dag som lyften — disciplinretur)
- 18 aktiva TDs oförändrat sedan F2-kickoff
- TD-29 (readiness-probe) väntar på F2-P6

## Vad nästa session ska göra

**Fas 2-prereqs som återstår:**

1. **F2-P3** (Terraform): Budget Actions $50/mån + `JobbPilotBedrockInvoke`-
   IAM-policy + Lambda auto-disable + ECS-stop. Dev-apply efter `terraform
   plan`-granskning.
2. **F2-P4** (Runbook): `docs/runbooks/aws-cost-recovery.md` — budget-alert-
   respons, manuell flag-toggle, IAM-policy-återställning, ECS-restart.
3. **F2-P6** (TD-29): `/api/live` + `/api/ready` med DB+Redis-check.

Efter dessa → Fas 2 JobTech-features får startas.

## Pending operativt för Klas

- AWS SES domain verification (eller individuella mottagar-emails) innan
  klasskamrat-tester
- DKIM + SPF DNS-records hos domain-registrar
- SES production access-ansökan innan public launch
- `terraform apply` av F2-P3 Budget Actions efter leverans

## Reflektion

Non-stop-flödet fungerade bra över 10 commits. Klas fångade 3 disciplinmissar
i realtid (TD-69, git add -A, secrets.out) och pushade tillbaka. Memory-
systemet lärde sig från varje miss. Pre-push gates fångade inget av detta
eftersom de inte kör integration-tester eller granskar untrackade filer —
tekniska förbättringspunkter för framtiden men inom dagens scope-konstrnt
acceptabelt.

Klas-disciplin > automatiska checks: den mänskliga granskningen är fortfarande
första försvarslinjen.
