---
session: Fas 1-rensning B–F + disciplinretur + TD-67 + TD-25 + TD-68 (apply)
datum: 2026-05-12
slug: fas1-rensning-komplett-td67-td68
status: klar
commits:
  - 74d28ad feat(web) Batch B — TD-41 + TD-57 shadcn-first form-controls
  - 2513580 docs(tech-debt) Batch B stängningar
  - 006e3e1 feat(web) Batch C — TD-1 + TD-2 a11y-pass
  - bc91ff1 docs(tech-debt) Batch C stängningar (TD-40 retroaktivt)
  - f1a82be feat(web) Batch D — TD-3 + TD-4 + TD-5 UX-pass /mig
  - 5623d01 docs(tech-debt) Batch D stängningar
  - 9f74efb feat Batch E — TD-6 + TD-28 me-flöde (fullstack)
  - fdd2673 docs(tech-debt) Batch E stängningar
  - 80a6c3c chore(test) VerifyCredentialsTests pattern-match
  - 4310a8e chore(security) .gitleaksignore fingerprints
  - b4bb60f test(applications) Batch F — TD-12 cross-user-isolation
  - d3cbf99 docs(tech-debt) Batch F stängningar
  - 62e8453 test disciplinretur — TD-65 + TD-66
  - 71b7c9f docs(tech-debt) TD-65 + TD-66 stängda
  - 861a7cf feat(security) TD-67 — IFailedAccessLogger + ADR 0031
  - ba4f36f docs(tech-debt) TD-67 stängd
  - eed6cc2 fix(worker) TD-25 — HardDeleteAccountsJob resilient loop
  - 80c1f06 docs(tech-debt) TD-25 stängd
  - 70ca42b feat(infra) TD-68 — CloudWatch security-alarms
  - 2f66b4f docs(tech-debt) TD-68 Pågående
  - 45fb7f7 docs(tech-debt) TD-68 stängd efter dev-apply
fas-1-rensning:
  - Batch B stängd: TD-41 + TD-57 (shadcn-first form-controls)
  - Batch C stängd: TD-1 + TD-2 + TD-40 (a11y-pass)
  - Batch D stängd: TD-3 + TD-4 + TD-5 (UX-pass /mig)
  - Batch E stängd: TD-6 + TD-28 (fullstack me-flöde, Klas-Alt1)
  - Batch F stängd: TD-12 (cross-user-isolation)
  - Disciplinretur: TD-65 + TD-66 stängda (in-block-fix per §9.6)
  - TD-67 stängd via ADR 0031 (failed-access-detection)
  - TD-25 stängd (HardDeleteAccountsJob resilient loop)
  - TD-68 stängd efter dev-apply (CloudWatch security-alarms live)
adrs:
  - ADR 0031 (failed-access-detection) — Accepted
nya-tds-lyfta:
  - TD-67 (audit-trail för failed access — STÄNGD samma session via ADR 0031)
  - TD-68 (CloudWatch metric filter + SNS-alarm — STÄNGD samma session efter apply)
nya-tds-lyfta-kvar:
  - (inga — alla nya TDs stängda inom sessionen)
deploy:
  - terraform apply: jobbpilot-dev-secops-anomaly (SNS), metric filter, 2 alarms
session-lärdom:
  - TD-lyftningar måste pressas mot §9.6-kriterier även om CTO/auditor föreslår
  - "Scope-disciplin per batch" eller "+1-2h CC-tid" är INTE legitima skäl
  - Default = in-block-fix
  - Sparad i memory/feedback_td_lifting_discipline.md
---

# Session: Fas 1-rensning komplett — Batch B-F + disciplinretur + TD-67/TD-68

Lång sammanhängande CC-session 2026-05-11 ~21:00 → 2026-05-12 ~08:00.
Levererade hela Fas 1-rensningens batches B–F, disciplinretur av två
tidigare lyfta TDs, samt ADR 0031-implementation (TD-67) + dev-apply
av TD-68 CloudWatch security-alarms.

## Mål

Klas-startprompt 2026-05-11 var "kör Batch B" — vilket utlöste hela
batching-planens återstående batches plus discovery-driven utvidgning.

## Batches levererade i ordning

### Batch B — TD-41 + TD-57 (shadcn-first form-controls)

CTO-beslut "shadcn-first med Input-primitive som default":
- `me-profile-form.tsx`: native `<select>` → shadcn `Select` + RHF `Controller`
- `add-follow-up-form.tsx`: native `<input type="datetime-local">` → `<Input>`
- In-block-fix från design-reviewer: `className="w-full"` + `disabled={isPending}` på `channel`-Select

Motivering: DRY (Hunt/Thomas), SRP (Martin), Component Cohesion CCP/REP,
Konsekvens (NN/g #4), A11y (WCAG 4.1.2 + 2.1.1).

### Batch C — TD-1 + TD-2 + TD-40 (a11y-pass)

- TD-1: skip-link i `(app)/layout.tsx` med `sr-only focus:not-sr-only`-pattern + `<main id="main" tabIndex={-1}>`. Följer GOV.UK Design System recipe.
- TD-2: CardTitle ändrad från `<div>` till `<h3>` default + `asChild` via `Slot.Root` (samma som button.tsx). Consumer-uppdatering i `mig/page.tsx`: två CardTitles lyfta till `<h2>` via `<CardTitle asChild>`-pattern.
- TD-40: retroaktivt stängd — regression-test för `.refine()` leaf-paths fanns redan i `resume-schemas.test.ts:275-364`.

### Batch D — TD-3 + TD-4 + TD-5 (UX-pass /mig)

CTO-beslut: Variant (a) för alla tre.
- TD-3: stum roller-tom-state (rendera bara om `roles.length > 0`)
- TD-4: userId-fältet helt borttaget (GDPR Art. 5(1)(c) data minimization)
- TD-5: JSDoc på `getServerSession()` som "inline ADR för mikrobeslut"

Motivering: NN/g #6 + #8, GOV.UK Design Principles #2, GDPR, DRY/SoC/KISS.

### Batch E — TD-6 + TD-28 (me-flöde fullstack)

Klas valde Alt 1 (utöka till fullstack) över Alt 2 (split-batch).

Backend:
- Ny endpoint `POST /api/v1/auth/verify` (`VerifyCredentialsQuery` med auth)
- `IUserAccountService.GetEmailAsync(userId)`-användning (claim ger bara userId)
- AuthWritePolicy rate-limit (20/min/IP)

Frontend:
- `deleteAccountAction`: validate → server-trusted email-match → POST /auth/verify → DELETE /me → cookie-cleanup + redirect
- `DeleteAccountDialog` (Radix Dialog + RHF) med typed-confirmation = email
- `DeleteAccountSection` "Farligt område" under separator
- TD-6: `logoutAction` strukturerad `console.error` på network + HTTP-fail

Säkerhets-design: kombo-attack (verify → delete) skyddad av två separata
rate-limits (AuthWrite 20/min/IP + AccountDeletion 1/60s/UserId).

Pre-push: gitleaks blockerade pga const-string testlösenord. CTO-beslut
2026-05-12: matcha LoginTests-pattern (`var password = ...`) + fingerprints
i `.gitleaksignore` — INTE refactor till central konstant (bryter pattern).

### Batch F — TD-12 (cross-user-isolation)

7 integration-tester i `ApplicationsCrossUserIsolationTests.cs`:
- GET/transition/list/pipeline/follow-up/note från B mot A:s data → 404
- Defense-in-depth: A:s data orörd efter B:s alla attack-attempts

404-policy: enumeration-attack-skydd per OWASP API1:2023 BOLA.

Lyft (felaktigt) TD-65 (Playwright E2E) + TD-66 (Resume/JobSeeker
cross-user-tester) — **disciplinmiss**, se nedan.

## Klas-feedback om disciplinmiss

Klas påpekade efter Batch F: "Varför lyftes hela tiden nya TDs utan att
fixa dom direkt?"

Granskning:
- TD-63 (kind-union för writes, från Batch A) — borderline
- TD-64 (i18n-omnibus, från Batch A) — legitim (egen ADR krävs)
- **TD-65** (Playwright E2E, från Batch E) — disciplinmiss (lyftes blint
  utan att verifiera om fixtures fanns; de fanns)
- **TD-66** (Resume/JobSeeker cross-user, från Batch F) — disciplinmiss
  (mekaniskt pattern-spegling i samma fas)

CC accepterade CTO/security-auditor-rekommendationer optimerade för
"scope-disciplin per batch" — men §9.6 säger fas-tillhörighet styr, inte
CC-tid. TD-listan är inte ett dumpning-ställe.

**Lärdom sparad** i `memory/feedback_td_lifting_discipline.md`:
TD-lyftningar måste pressas mot §9.6-kriterier även om CTO/auditor föreslår.

## Disciplinretur

- TD-65 fixad: 3 Playwright-tester i `delete-account.spec.ts` (modal/disabled/happy-path inkl. Redis-session-revoke-verifiering per security-auditor M1)
- TD-66 fixad: `ResumesCrossUserIsolationTests` (7 tester) + `MeProfileCrossUserIsolationTests` (3 tester inkl. ID-injection-skydd)

## TD-67 (ADR 0031)

Failed-access-detection — strategiskt val mellan 6 alternativ (A–F).
CTO-beslut: Hybrid F (strukturerad ILogger + CloudWatch-aggregat).

Levererat:
- `IFailedAccessLogger`-port (Application) + `FailedAccessLogger`-impl
  (Infrastructure, `LoggerMessage`-source-gen, EventId 4001, Warning)
- 9 handlers modifierade med inline-pattern (Application + Resume)
- Differentiering "okänt id" vs "tillhör annan" via extra existens-query
  i error-path (klient ser identisk 404)
- ADR 0031 — bevarar ADR 0022 immutable

213/213 Application UnitTests gröna (+4 nya för logger-bevakning).

## TD-25 (HardDeleteAccountsJob resilient loop)

In-block-fix per §9.6:
- Per-konto try/catch (en exception blockerar inte andra konton)
- `OperationCanceledException` re-throws (cancel-disciplin per §3.5)
- `LogAccountFailed` (EventId 2502, Error)
- Idempotens-invariant bevarad (`processed++` efter `await` → bara success)

4 nya unit-tester (217/217 gröna).

## TD-68 — CloudWatch security-alarms (apply genomförd)

CTO-beslut: separat batch från TD-25 (rigorös, inte sammansatt).

Modul `infra/terraform/modules/cloudwatch_security_alarms/`:
- `aws_cloudwatch_log_metric_filter` (unquoted substring-pattern)
- `aws_sns_topic` (KMS-encrypted)
- `aws_sns_topic_policy` (least-privilege: Service + SourceAccount + SourceArn)
- 2 `aws_cloudwatch_metric_alarm` (failed-access + log-pipeline-health)

3 security-auditor-rundor:
- #1: 2 Major + 6 Minor
- #2 (efter M1+M2+Minor 7 fix): Approved, 4 Minor kvar
- #3 (efter alla Minor + runbook): Approved, kosmetisk IMY-fix

Klas-direktiv "Det skall göras ordentligt" → alla 4 Minor adresserade
in-block + runbook `docs/runbooks/failed-access-anomaly.md`.

Pre-apply-verifiering: `aws logs test-metric-filter` matchade 1/3 sample-
events korrekt. `terraform plan`: 5 to add, 0 change, 0 destroy. Klas-GO.

`terraform apply td68.tfplan` genomfört i dev:
- `jobbpilot-dev-secops-anomaly` SNS-topic live
- `jobbpilot-dev-failed-access-anomaly` alarm (INSUFFICIENT_DATA)
- `jobbpilot-dev-api-log-pipeline-health` alarm (INSUFFICIENT_DATA)

Prod-invokation defereras till prod-ECS-stack-leverans (modul + dev-
invokation finns, prod-invokation är 10-rader copy).

## TD-katalog efter session

- Stängda denna session: TD-1, TD-2, TD-3, TD-4, TD-5, TD-6, TD-12, TD-25,
  TD-28, TD-40, TD-41, TD-57, TD-65, TD-66, TD-67, TD-68
- Totalt stängda: 45
- Aktiva kvar: 18 (alla Fas 2+ eller Trigger-baserade)
- **Minor × Fas 1-sektionen: TOM**

## Säkerhetskonsekvens

Sessionen levererade flera säkerhets-invarianter:
- Cross-user-isolation maskinellt bevakad (Application + Resume + Me)
  via 16 integration-tester
- BOLA-enumeration-attack detekterbar i dev-trafik (TD-67 + TD-68)
- Defense-in-depth re-auth före DELETE /me (TD-28)
- Resilient hard-delete-job (TD-25)

## Tester (full svit efter session)

| Suite | Antal |
|-------|-------|
| Backend Domain.UnitTests | 163 |
| Backend Application.UnitTests | 217 |
| Backend Architecture.Tests | 32 |
| Backend Api.IntegrationTests | 9 nya för VerifyCredentials + Apps/Resumes/Me isolation |
| Frontend Vitest | 234 |
| tsc --noEmit | grön |
| dotnet format | ren |

## Pushed commits denna session

21 commits totalt (feat + docs separat per §1.5). Se `commits`-fält i
frontmatter för fullständig lista.

## Nästa session

Per Klas-val 2026-05-12: **Fas 2-kickoff med ADR 0005-design**.

Startprompten genereras parallellt och pekar på:
1. ADR 0005 (go-to-market + kostnadsskydd) som första arbete
2. Kostnadsskydd-implementation efter ADR-beslut (Budget Actions,
   `registrations_open`-flagga, rate-limiting-utvidgning, runbook
   `docs/runbooks/aws-cost-recovery.md`)
3. JobTech-integration kan starta efter dessa prereqs

## Lärdomar (memory uppdaterad)

- `memory/feedback_td_lifting_discipline.md` — TD-lyftningar måste pressas
  mot §9.6-kriterier; "scope-disciplin per batch" är inte legitimt skäl.
