# Current work — JobbPilot

**Status:** **Fas 2-kickoff 2026-05-12 — ADR 0005 Accepted (Alternativ C + invitations/waitlist amendment). Nästa arbete: F2-P0a (Invitation + WaitlistEntry aggregates).**
**Senast uppdaterad:** 2026-05-12
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md` (aktiva) + `docs/tech-debt-archive.md` (stängda)

---

## Aktivt nu — Fas 2 startad, F2-P0 (invitations + waitlist) först

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
