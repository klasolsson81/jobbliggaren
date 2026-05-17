# Release-checklist (generisk, återkommande)

> Repeterbar release-procedur för JobbPilot. Gäller **varje** tag-driven
> release, oavsett fas. Skild från `v0.2-prod-launch-checklist.md` — den är
> en engångs-checklist för *första* prod-deployen; detta är den löpande
> rutinen som används om och om igen.
>
> **Skapad:** 2026-05-17 (roster-gap-CTO 2026-05-17 §1.5 — "runbook, inte
> release-manager-agent"; ADR 0045-bunt steg 6). Deploy-beslut är strategiska
> och kräver Klas-godkännande (CLAUDE.md §9.2) — denna runbook ersätter inte
> det, den strukturerar det.

---

## 1. Tag-semantik (ADR 0019)

| Tag-mönster | Miljö | Approval | Exempel |
|---|---|---|---|
| `v*-dev` | dev | Automatisk (deploy-dev.yml) | `v0.3.1-dev` |
| `v*-rc*` | staging | Automatisk till staging | `v0.3.0-rc1` |
| `v*` (ren) | prod | **Manuell approval (Klas)** | `v0.3.0` |

`main` är enda branch (ADR 0019, direct-push). Staging är *miljö*, inte
branch. Deploy sker via tag-push på `main`, aldrig via branch-merge.

---

## 2. Före tag (pre-flight)

- [ ] **main-CI grön** — `gh run list --workflow build --limit 1` → `success`
      (backend + frontend + coverage + ci alla gröna). Coverage-gaten
      (ADR 0044) får inte vara röd.
- [ ] **Observe-only-signaler granskade** (ADR 0045) — `lighthouse` /
      `loadtest` / `audit`-jobben är observe-only och blockerar inte, men
      deras `::warning::`/summary ska läsas inför release: ny CWV-regression,
      p95-budget-överskridande eller High/Critical-CVE noteras och bedöms
      (åtgärda eller medvetet acceptera + motivera).
- [ ] **Inga öppna Klas-STOPP-flaggor** i `docs/current-work.md`.
- [ ] **Aktiva Major-TD mot release-scope** genomgångna (`docs/tech-debt.md`)
      — launch-blocker-TD löst eller medvetet deferrad med motiv.
- [ ] **Migrations** — om EF Core-migration ingår: verifiera schema-mode-
      dispatch (ADR 0033) och DB-roll-separation (ADR 0034); Identity-schema-
      ändring → manuell procedur (TD-72).
- [ ] **GDPR-konsekvens** för nytt scope bedömd (CLAUDE.md §8 punkt 8) — ny
      PII? loggning? retention? Audit-wire intakt (ADR 0035)?
- [ ] **Secrets-hygien** — inga nya secrets i klartext; AWS Secrets Manager +
      KMS för allt känsligt (CLAUDE.md §5.4).
- [ ] **Lokal diff-granskning** (CLAUDE.md §6.3 mekanism 4) — Klas läser
      `git log` + `git diff` för release-spannet.

---

## 3. Tagga + deploy

```bash
# Verifiera HEAD är exakt det som ska släppas
git log --oneline -1
git rev-parse HEAD

# dev/staging — automatisk efter push
git tag v<X.Y.Z>-dev <HEAD> && git push origin v<X.Y.Z>-dev      # → dev
git tag v<X.Y.Z>-rc1 <HEAD> && git push origin v<X.Y.Z>-rc1      # → staging

# prod — KRÄVER Klas-GO innan tag-push (CLAUDE.md §9.2)
git tag v<X.Y.Z> <HEAD> && git push origin v<X.Y.Z>             # → prod (manuell approval i pipeline)
```

CC får **inte** push:a en prod-tag (ren `v*`) utan explicit Klas-GO i
sessionen. dev/rc-tags är CC-tillåtna efter grön CI.

---

## 4. Efter deploy (verifiering)

- [ ] **ECS-tasks startar** (api + worker) — `aws ecs describe-services`
      eller konsolen.
- [ ] **`/api/ready` → 200** mot målmiljöns domän (strict readiness: DB +
      Redis dependency-checks, TD-29).
- [ ] **`/api/health` → 200** (liveness).
- [ ] **Hangfire-jobben** kör enligt schema om release rör Worker
      (`*/10`-cron etc.) — verifiera i Hangfire-dashboard/loggar.
- [ ] **Audit-wire** — om release rör audit-genererande flöden: bevisa
      INSERT i `audit_log` via CloudWatch (ADR 0035).
- [ ] **CloudWatch-alarms i `OK`-state** (jobtech-sync-failures,
      auditor-write-failures, log-pipeline-health — ADR 0036).
- [ ] **Frontend** (om i scope) — Lighthouse observe-signal mot
      ADR 0045-budgetar; manuell rök-test av kritiska flöden.
- [ ] **Rollback testad/känd** — `aws ecs update-service --task-definition
      <previous>` (BUILD.md §15.4) vid issues.

---

## 5. Rollback

Vid fel efter prod-deploy:

```bash
aws ecs update-service \
  --cluster <cluster> --service <service> \
  --task-definition <previous-task-def-arn> \
  --force-new-deployment
```

Notera incidenten i `docs/sessions/` + relevant runbook. Skapa ADR om
rollback avslöjar ett arkitekturellt problem (CLAUDE.md §8 punkt 9).

---

## 6. Efter release (docs-synk)

- [ ] `docs/current-work.md` — status uppdaterad (CLAUDE.md §1.5).
- [ ] Session-logg i `docs/sessions/` om release var en egen session.
- [ ] `docs/steg-tracker.md` om STEG flyttat status.
- [ ] Tag + miljö noterad så nästa release vet senaste prod-state.

---

## Referenser

- ADR 0019 (direct-push + tag-semantik), ADR 0033/0034 (migrations/DB-roller),
  ADR 0035 (audit-wire), ADR 0036 (prod-stack-defer + ops-alarms),
  ADR 0044 (coverage-gate), ADR 0045 (perf observe-only-signaler)
- CLAUDE.md §6.3 (granskningsspärrar), §8 (DoD), §9.2 (deploy kräver Klas-GO)
- BUILD.md §15 (deployment/rollback)
- `docs/runbooks/v0.2-prod-launch-checklist.md` — engångs-checklist för
  *första* prod-deployen (komplement, inte ersättning för denna)
