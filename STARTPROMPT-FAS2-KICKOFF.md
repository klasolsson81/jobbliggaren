# Startprompt — Fas 2-kickoff: ADR 0005-design

**Skapad:** 2026-05-12 ~08:00 efter Fas 1-rensning komplett + dev-apply av TD-68.

---

## Förkrav

1. **Repo uppdaterat:**
   ```bash
   git pull origin main
   ```

2. **Verifiera HEAD ≥ `45fb7f7`:**
   ```bash
   git log --oneline -10
   ```
   Förväntat (lägsta):
   ```
   45fb7f7 docs(tech-debt): TD-68 stängd efter dev-apply — CloudWatch security-alarms live
   2f66b4f docs(tech-debt): TD-68 markeras Pågående — Terraform-kod + runbook levererad
   70ca42b feat(infra): TD-68 — CloudWatch security-alarms för failed_access_attempt
   80c1f06 docs(tech-debt): TD-25 stängd — HardDeleteAccountsJob resilient loop
   eed6cc2 fix(worker): TD-25 — HardDeleteAccountsJob per-konto try/catch (resilient loop)
   ba4f36f docs(tech-debt): TD-67 stängd via ADR 0031, TD-68 lyft för CloudWatch-infra
   861a7cf feat(security): TD-67 — failed cross-user access detection (ADR 0031)
   ```

3. **`git status` clean** (terraform `.out`-artefakter OK att ignorera, STARTPROMPT-filer kan tas bort).
4. **Lokala krav:** .NET 10 SDK, Node 22 + pnpm, Docker (Testcontainers), AWS CLI med `AWS_PROFILE=jobbpilot`.

---

## Mandatory reads (CLAUDE.md §1.5)

1. **`CLAUDE.md`** — hela. Särskilt §1.6 (docs-karta), §9.2 (fas-skifte = strategiskt beslut, kräver Klas-GO), §9.6 (in-block-fix-default + CTO-auto-follow), §9.7 (TD-livscykel).
2. **`BUILD.md` §18 (Fas 2)** + §13 (säkerhet/PII) + §15 (rate-limiting + cost-skydd).
3. **`docs/steg-tracker.md`** — Fas 2 är blockerad av prereqs (fotnot ²) tills ADR 0005 är beslutat.
4. **`docs/current-work.md`** — sessionsstatus + Fas 1-rensningens leverans.
5. **`docs/tech-debt.md`** — 18 aktiva TDs (alla Fas 2+/Trigger/Opportunistiska). Notera särskilt: **TD-26** (AI-kostnadstak Fas 4) + **TD-29** (strict readiness-probe Fas 2) + **TD-56** (ListJobAdsQuery full paginering Fas 2 JobTech).
6. **`docs/decisions/0005-go-to-market-strategy.md`** — **Status: Proposed.** Detta är den primära ADR att besluta.
7. **`docs/sessions/2026-05-12-0800-fas1-rensning-komplett-td67-td68.md`** — senaste session-log.

---

## Memory att läsa

Hela `MEMORY.md` + alla länkade memory-filer. Särskilt viktiga för Fas 2:

- `feedback_cto_decides_multi_approach.md` — multi-approach-val går till `senior-cto-advisor`, inte CC-rekommendation.
- `feedback_td_lifting_discipline.md` — **NY 2026-05-12**: TD-lyftningar måste pressas mot §9.6-kriterier (annan fas / saknad funktion-dependency). "Scope-disciplin per batch" eller "+1-2h CC-tid" är INTE legitima skäl. Default = in-block-fix.

---

## Uppdrag: Fas 2-kickoff via ADR 0005-design

Fas 2 (JobTech Integration) är **blockerad** av go-to-market-strategi-beslut enligt
BUILD.md §18 + `docs/steg-tracker.md` fotnot ². ADR 0005 finns som **Proposed**
och behöver beslutas innan Fas 2 kan startas.

### Fas 2-prereqs (per BUILD.md §18 + fotnot ²)

| # | Prereq | Status idag |
|---|---|---|
| 1 | ADR 0005 (go-to-market + kostnadsskydd-strategi) beslutat | **Proposed** |
| 2 | Budget Actions konfigurerade | Saknas |
| 3 | `registrations_open`-flagga implementerad | Saknas |
| 4 | Rate-limiting-utvidgning för publika endpoints | Saknas (TD-29 + ev. mer) |
| 5 | Runbook `docs/runbooks/aws-cost-recovery.md` | Saknas |

### Första arbete i ny session

**ADR 0005-design** är första steg. Detta är arkitektur/strategi-arbete med
multi-approach-val:
- Go-to-market-modell (free-tier-stop / hard-cap / paywall / waitlist / BYOK-only?)
- Kostnadsskydd-mekanik (Budget Actions + `registrations_open`-flagga + rate-limit?)
- Public endpoint surface i Fas 2 (anonym JobAd-listning eller auth-gated?)
- AI-kostnad-koppling till Fas 4 (TD-26 token-tak)

**CC-arbetsflöde:**

1. Discovery: läs `docs/decisions/0005-go-to-market-strategy.md` + BUILD.md §15 + §18 + §13 + befintliga rate-limit-policies (`RateLimitingOptions.cs`)
2. **Multi-approach → senior-cto-advisor** för designval (CC ger INTE egen rekommendation per memory)
3. CTO-beslut → ADR 0005 supersession eller acceptance med beslutsmotivering
4. Klas-STOPP-rapport med ADR-utkast + impl-plan
5. Efter Klas-GO: ADR-status flippas till **Accepted**
6. Impl-batch börjar (Budget Actions först, sen `registrations_open`-flagga, sen rate-limit-utvidgning, sen runbook)

### Workflow-disciplin (CLAUDE.md §9.6 + §9.7 + ny lärdom)

1. **Discovery först** — läs ADR 0005 + BUILD.md §15+§18 + befintliga rate-limit-modulen
2. **CTO-invocation vid multi-approach** — CC ger **inte** egen rekommendation. Strategin går till senior-cto-advisor.
3. **CC går direkt till implementation efter CTO-beslut** när motiveringen är entydig (§9.6 CTO-auto-follow). Klas-STOPP triggas vid:
   - ADR-amendments/supersessions (strategiskt beslut)
   - Deploy-beslut (per §9.2)
   - Större scope-utökning bortom batch-avtalet
4. **In-block-fix-default per fas-regel** — TD lyfts ENDAST om annan fas eller saknad funktion-dependency. Default = fixa in-block.
5. **TD-lyftnings-disciplin** (ny lärdom från Fas 1-rensning): pressa CTO/auditor-rekommendationer att lyfta TD mot §9.6-kriterier. "Scope-disciplin per batch" eller "+Xh CC-tid" är INTE legitima skäl.
6. **TD-livscykel (§9.7):** stängda TDs flyttas till `tech-debt-archive.md` i samma docs-commit som leveransen.

---

## Förbud (default — kan lyftas av Klas)

- **INGA Fas 2-JobTech-features** (söka jobb på Platsbanken, sparade sökningar, JobAd-import) utan ADR 0005-beslut + minst Budget Actions + `registrations_open`-flagga aktiva
- **INGA STEG-starter** utan Klas-GO
- **INGA ändringar** av `BUILD.md` / `CLAUDE.md` / `DESIGN.md` utan explicit instruktion
- **INGA prod-deploys** utan Klas-godkännande
- **INGA prod-Terraform-applies** utan Klas-godkännande (dev-apply av TD-68 + framtida moduler kan godkännas inline; prod kräver explicit GO)

---

## Snabbreferens — sökvägar

- **Fas 1-rensning session-log:** `docs/sessions/2026-05-12-0800-fas1-rensning-komplett-td67-td68.md`
- **ADR 0005 (Proposed):** `docs/decisions/0005-go-to-market-strategy.md`
- **ADR-index:** `docs/decisions/README.md`
- **Aktiva TDs:** `docs/tech-debt.md` (18 aktiva, alla Fas 2+/Trigger)
- **Stängda TDs:** `docs/tech-debt-archive.md` (45 stängda)
- **CLAUDE.md §9.2:** fas-skifte är strategiskt
- **CLAUDE.md §9.6:** fas-regel + CTO-auto-follow + TD-disciplin
- **CTO-agent:** `senior-cto-advisor`
- **TD-katalog-status:** 18 aktiva, 45 stängda

---

## Status TD-68 (CloudWatch security-alarms — pågående uppgifter)

Modul live i dev:
- `jobbpilot-dev-secops-anomaly` SNS-topic (KMS-encrypted)
- `failed_access_attempt` metric filter
- 2 alarms (failed-access + log-pipeline-health) — båda i INSUFFICIENT_DATA

Pending (valfritt, inte fas-blocker):
1. Sätt `secops_alert_email` i `infra/terraform/environments/dev/terraform.tfvars` + re-apply + AWS-mail-opt-in
2. Drift-test: registrera 2 users via frontend, gör cross-user-anrop, verifiera alarm-trigger inom ~60s
3. Prod-invokation av modulen (kräver prod-ECS-stack-leverans)

---

## Föreslagen prompt-text till ny CC

```
Hej. Klas-prompt: Fas 2-kickoff — ADR 0005-design.

Läs förkrav + mandatory reads. Verifiera HEAD ≥ 45fb7f7.

Fas 1-rensning är komplett (16 TDs stängda 2026-05-11→12). Fas 1 Minor-
sektionen är TOM. Fas 2 (JobTech Integration) är blockerad av ADR 0005
(go-to-market + kostnadsskydd) som idag är Proposed.

Första arbete: discovery av ADR 0005 + BUILD.md §15+§18 + befintliga rate-
limit-policies. Sen senior-cto-advisor för designval (multi-approach: go-to-
market-modell, kostnadsskydd-mekanik, public endpoint surface, AI-kostnad-
koppling). CTO-beslut → ADR-utkast → Klas-STOPP-rapport → ADR-status flippas
till Accepted efter Klas-GO.

Workflow-disciplin: §9.6 (in-block-fix-default + CTO-auto-follow) + §9.7
(TD-livscykel) + ny memory feedback_td_lifting_discipline.md (TD-lyftningar
måste pressas mot §9.6-kriterier, scope-disciplin är inte legitimt skäl).

Inga JobTech-features innan ADR 0005-beslut + minst Budget Actions +
registrations_open-flagga aktiva.
```

---

## Vad jag (denna session-CC) har gjort

Lång session 2026-05-11 ~21:00 → 2026-05-12 ~08:00. 21 commits totalt.

Fas 1-rensning komplett:
- Batch B (TD-41 + TD-57): shadcn-first form-controls
- Batch C (TD-1 + TD-2 + TD-40): a11y-pass
- Batch D (TD-3 + TD-4 + TD-5): UX-pass /mig
- Batch E (TD-6 + TD-28): me-flöde fullstack (Klas-Alt1)
- Batch F (TD-12): cross-user-isolation
- Disciplinretur (TD-65 + TD-66): reparation av disciplinmissar
- TD-67 (ADR 0031): failed-access-detection
- TD-25: HardDeleteAccountsJob resilient loop
- TD-68: CloudWatch security-alarms (modul + dev-apply)

ADRs skrivna: ADR 0031.

Memory uppdaterad: `feedback_td_lifting_discipline.md`.

TD-katalog: 18 aktiva (alla Fas 2+/Trigger), 45 stängda. Fas 1 Minor TOM.
