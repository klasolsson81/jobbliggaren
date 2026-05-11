# Current work — JobbPilot

**Status:** **FAS 1 BLOCK A + 3 follow-up-TDs KOMPLETT 2026-05-11.** Block A (A1+A2+A3+A4+TD-43) klart sedan tidigare. **Laptop-CC-session 2026-05-11 0540→0940 stängde TD-44 + TD-45 + TD-46.** Infra-tillägg: ny `senior-cto-advisor`-agent (decision-maker vid multi-approach) + CLAUDE.md §9.6 (4-timmarsregel för in-scope-fix vs TD-skapande). **Stängda TDs totalt:** TD-15, TD-31, TD-38, TD-43, TD-44, TD-45, TD-46. **Aktiva TDs:** TD-39/40/41/42/47/48/49. **Nästa:** Block B (Application Management / Dashboard / JobTech-förstudie) eller fortsatt TDs-cleanup.
**Senast uppdaterad:** 2026-05-11
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**Laptop-CC-session 2026-05-11 0540→0940 — TDs-cleanup KLAR.** 3 TDs stängda (TD-44/45/46) + 1 infra-tillägg (senior-cto-advisor + 4h-policy). 4 commits, alla pushade till `main`. HEAD = `09ef399`.

### TDs-cleanup-session-summary

| Steg | Scope | Status | Commits |
|------|-------|--------|---------|
| TD-44 | HSTS-header anti-regression-test (3 nya `[Fact]`) | ✓ Stängd | `b742e50` |
| TD-45 | LoginForm focus-flytt vid `state.error` (a11y) | ✓ Stängd | `994bd1a` |
| TD-46 | Extrahera `pathToElementId` per-domän (Approach B) | ✓ Stängd | `c505be2` |
| Infra | `senior-cto-advisor`-agent + CLAUDE.md §9.6 (4h-regel) | ✓ Klar | `09ef399` |

### Block A sub-block-summary (referens, oförändrat)

| Sub-block | Scope | Status | Commits |
|-----------|-------|--------|---------|
| A1 | TD-15 Resume-formulär aria-invalid + focus-flytt | ✓ Stängd | 267e120 + 4df70c2 |
| A2 | JobSeeker profil-edit-yta (Vitest 75/75) | ✓ Klar | cc585a7 + d55b460 |
| A3 | TD-31 UseHttpsRedirection env-gate-test | ✓ Stängd | 1221240 + b4e9199 |
| TD-43 (parallell CC) | Komponent-tests för LoginForm + MeProfileForm + ResumeContentForm | ✓ Stängd | 6b2b0ca + 01cc656 |
| A4 | TD-38 TLS-hardening (kod + apply) | ✓ Stängd | ebb7550 + 48ebe0e + apply |

### Aktiva TDs (oförändrat sedan tidigare, plus TD-49 ny)

- **TD-39:** Error-summary-mönster för stora formulär (A1 m2)
- **TD-40:** Path-equality regression-bevakning (A1 m1)
- **TD-41:** Select-komponent-konvention native vs shadcn (A2 M1+M2)
- **TD-42:** Touch-target projektbrett <44px (A2 Mi1)
- **TD-47:** RDS CA-bundle-rotation-bevakning (A4 security S-Minor-1)
- **TD-48:** Architecture-test för Trust=true-läckage (A4 dotnet-architect Mi2)
- **TD-49:** Unit-test för HstsOptions.EnsureSafeForEnvironment (TD-44 architect Minor 2) — blockerad: `JobbPilot.Api.UnitTests`-projekt finns inte
- **TD-44/45/46 — STÄNGDA 2026-05-11**

### Nya commits (denna laptop-session)

| SHA | Beskrivning |
|-----|-------------|
| `09ef399` | chore(claude): senior-cto-advisor + 4h-TD-policy |
| `c505be2` | refactor(web): TD-46 — extrahera pathToElementId till lib/forms/ (per-domän) |
| `994bd1a` | feat(web): TD-45 — LoginForm focus-flytt vid state.error (a11y) |
| `b742e50` | test(api): TD-44 — HSTS-header anti-regression-test |

### A4 apply-fas (väntar Klas-GO)

Runbook: `docs/runbooks/td-38-tls-apply.md` (8-pkt security-auditor-checklist).

Steg vid apply:
1. Verifiera bundle-integritet mot AWS upstream
2. Skapa tag `v0.1.1-dev` → deploy-dev.yml triggar build/push
3. Bundle-smoke-test inuti container
4. Re-run Migrate-task → Secrets uppdateras med VerifyFull-CS
5. Verifiera Secrets-innehåll (assertion: ingen `Trust=true` kvar)
6. Force-new-deployment Api + Worker
7. Bevaka CloudWatch för Npgsql-handshake-errors
8. Smoke-test + TD-38-stängning

### Pre-existing infra (oförändrat från STEG 14c)

| Resurs | Identifier |
|---------|-----------|
| Public URL | `https://dev.jobbpilot.se/api/ready` (200 OK + HSTS) |
| API task-def | `jobbpilot-dev-api:3` |
| Worker task-def | `jobbpilot-dev-worker:2` |
| Tag (senaste) | `v0.1.0-dev` på SHA `8215658` |

---

## Historik före Block A (referens)

**STEG 14c APPLIED 2026-05-10 — Fas 0 stängd.** Tre block kompletta:

1. **TD-37 fix** — root cause efter 6 commits + debug-middleware/console-logger för CI-visibility. Worker fixad parallellt (test-ordering-immune via self-managed recent-partition).
2. **First formal tag-deploy** — `v0.1.0-dev` triggade deploy-dev.yml end-to-end: OIDC assume → ECR push (api+worker) → ECS task-def render+deploy (api+worker) → smoke-test PASS. IAM-policy-fix krävdes för `ecs:DescribeTaskDefinition` (terraform apply mot prod-stacken).
3. **Fas 0-stängning** — Bootstrap-IAM-user verifierat tom, README + steg-tracker uppdaterade till Fas 1.

Se session-logg `docs/sessions/2026-05-10-2200-steg14c-td37-tag-deploy-fas0-stangning.md` för detaljer.

### Apply-state

| Resurs | Identifier |
|---------|-----------|
| **Tag** | `v0.1.0-dev` på SHA `8215658` |
| **Deploy run** | [25638084810](https://github.com/klasolsson81/jobbpilot/actions/runs/25638084810) — 3m34s, PASS |
| **Backend CI run** | [25637996682](https://github.com/klasolsson81/jobbpilot/actions/runs/25637996682) — backend + frontend + ci PASS |
| **API task-def** | `jobbpilot-dev-api:3` (ny revision deploy:ad via deploy-dev.yml) |
| **Worker task-def** | `jobbpilot-dev-worker:2` (ny revision deploy:ad) |
| **API + Worker** | 1/1 stable, smoke-test 200 + HSTS |
| **IAM-policy** | `jobbpilot-github-actions-deploy-dev` — `ecs:DescribeTaskDefinition` separerad till egen statement med `Resource: *` |
| **Bootstrap-IAM-user** | `aws iam list-users` → `Users: []` (verifierat tom) |

### Pre-existing infra (oförändrat sedan STEG 14b)

| Resurs | Identifier |
|---------|-----------|
| Public URL | `https://dev.jobbpilot.se/api/ready` (200 OK + HSTS) |
| OIDC provider | `arn:aws:iam::710427215829:oidc-provider/token.actions.githubusercontent.com` |
| Dev deploy-roll | `arn:aws:iam::710427215829:role/jobbpilot-github-actions-deploy-dev` |
| 3 Postgres-roller | `jobbpilot_migrations` + `jobbpilot_app` + `jobbpilot_worker` |
| Hangfire-schema | 13 tabeller i `hangfire`-schema, GRANT-modell aktiv |

## Senaste commits (denna session)

| SHA | Beskrivning |
|-----|-------------|
| `8215658` | chore(test): TD-37 — ta bort debug-koden efter rotorsak fixad |
| `92042cb` | **fix(test): TD-37 root cause** — sätt ConnectionStrings__Redis i fixtures |
| `27e0a87` | debug(test): TD-37 — aktiv console-logger för ASP.NET-internal categorier |
| `183eeba` | debug(test): TD-37 — IStartupFilter echar exception/5xx details till stderr |
| `de61e42` | fix(ci): TD-37 — sätt ASPNETCORE_ENVIRONMENT=Development som runner-level env |
| `c61487c` | fix(test): TD-37 follow-up — sätt ASPNETCORE_ENVIRONMENT via env-var i fixtures |
| `3b71fa5` | fix(test): TD-37 — tvinga Development-env i Api-fixtures + harden flaky tester |
| `24f04d3` | feat(migrate): STEG 14b — DDL-init + ConnectionStrings split + Worker-recovery |

## Tester totalt

- **Backend:** 554 lokalt (157 Domain + 183 Application + 23 Architecture + 26 Worker + 165 Api Integration). **CI: 554/554 grön.**
- **Frontend:** Vitest grön (oförändrat).

## Open follow-ups

**Operativa AWS-uppgifter:**
- (inga kvar — Fas 0 stängd)

**Defererade från STEG 14c:**
- (inga — debug-koden städades efter root cause)

**Övriga TD (oförändrat sedan 14b):**
- TD-13 (PII-encryption Fas 2 — kombineras med TD-27)
- TD-14 (DeleteResumeVersion Fas 4)
- TD-15 (Resume-formulär a11y Fas 1)
- TD-18 (intervju-states-utökning)
- TD-19 (Worker defense-in-depth Fas 2)
- TD-20 (SqlQuery<FormattableString>-refactor opportunistiskt)
- TD-23 (Redis MULTI/EXEC opportunistiskt)
- TD-24 (cascade-paginering Fas 4)
- TD-25 (per-konto try/catch opportunistiskt)
- TD-26 (AI-kostnadstak Fas 4)
- TD-27 (EmailHash-HMAC Fas 2)
- TD-28 (Frontend typed-confirmation-UX för DELETE /me)
- TD-29 (strict readiness Fas 2)
- TD-30 — STÄNGD per STEG 13c
- TD-31 (test för UseHttpsRedirection env-gate)
- TD-32 (TLS-policy uppgrade till PQ-2025-09 Fas 1)
- TD-33 (HSTS pipeline-gating-test via WebApplicationFactory)
- TD-34 (DNSSEC aktivering vid Fas 1-trigger)
- TD-35 (Apex + www ACM-cert vid prod-stack-rollout)
- TD-36 (mTLS / in-VPC-encryption vid Fas 2 multi-tenant)
- **TD-37 — STÄNGD per STEG 14c** (CI 554/554 grön)
- TD-38 (Trust Server Certificate hardening Fas 1)

## När nästa session startar

### Fas 1 förberedelse

**Pre-flight för Fas 1** (Core Domain):

1. SSO-login: `aws sso login --profile jobbpilot`
2. Verifiera dev-state oförändrat:
   - `curl -I https://dev.jobbpilot.se/api/ready` → 200 OK + HSTS
   - `aws ecs describe-services --cluster jobbpilot-dev-cluster --services jobbpilot-dev-api jobbpilot-dev-worker` → båda 1/1 running
3. Läs BUILD.md §18 Fas 1-milestones (auth-flöde + kärn-CRUD + dashboard)

### Fas 1-scope (BUILD.md §18)

- **Milstolpe:** "CV manuellt + 'fake' ansökningar i admin-audit"
- **Förslagna första-block:**
  - Resume-/JobSeeker-UX-pass (formulär-a11y per TD-15)
  - Application Management UX-polish (status-flöde, transition-formulär)
  - Dashboard-skiss (start-page med statistik)
  - JobTech-integration förstudie (BUILD.md §6)
- **TD att överväga in-block:**
  - TD-15 (Resume-formulär a11y) — Fas 1
  - TD-31 (UseHttpsRedirection env-gate-test) — opportunistiskt
  - TD-32 (TLS-policy PQ-2025-09) — Fas 1
  - TD-38 (Trust Server Certificate hardening) — Fas 1 innan staging

## Kända begränsningar / quirks (från STEG 14c)

- **`IWebHostBuilder.UseEnvironment()` är otillräckligt för minimal API + WebApplicationFactory** — `WebApplication.CreateBuilder()` läser ASPNETCORE_ENVIRONMENT INNAN ConfigureWebHost-callback körs. Verklig env-override sker via env-var i process FÖRE Services-access.
- **`IConnectionMultiplexer` (StackExchange.Redis) registreras med string captured vid registration-time.** ApiFactory.ConfigureServices replacar `IDistributedCache` men INTE `IConnectionMultiplexer` — fix: sätt `ConnectionStrings__Redis` env-var i `InitializeAsync` FÖRE Services-access.
- **`ecs:DescribeTaskDefinition` stödjer inte resource-level permissions** — AWS-API loggar request som `*` oavsett ARN-format i policy:n. Måste vara separat statement med `Resource: *`. Verifierat empiriskt deploy-dev.yml run 25638084810.
- **AWS ALB `RedirectActionConfig.StatusCode` hardlimited till HTTP_301 | HTTP_302** (kvar från 13c).
- **Pl/pgsql DO-blocks tar inte Npgsql-parameters** (kvar från 14b).
- **RDS-master är limited superuser** (kvar från 14b).

## Done last session (STEG 14c)

- TD-37 root cause identifierat via debug-middleware (IStartupFilter echar exception/5xx till stderr) + console-logger på Information-level för ASP.NET-internal categorier
- Fix applicerad: `ConnectionStrings__Redis` env-var i ApiFactory + StrictRateLimitApiFactory.InitializeAsync (parallell pattern som ProductionStartupFactory hade redan)
- Worker-test self-managed recent-partition (test-ordering-immune mot RunAsync_EndToEnd)
- Rate-limit-test-merge (delade budget-fix)
- ProductionStartupSmokeTests (regression-skydd för Production-env-pipeline)
- build.yml ASPNETCORE_ENVIRONMENT=Development (säkerhet mot CI-default-skew)
- Tag `v0.1.0-dev` skapad och pushad → deploy-dev.yml triggad
- IAM-policy-fix för `ecs:DescribeTaskDefinition` via terraform apply mot prod-stacken
- Deploy end-to-end PASS efter retry: OIDC + ECR push + ECS deploy + smoke-test
- README + current-work + steg-tracker + tech-debt uppdaterade till Fas 1-status
- Bootstrap-IAM-user verifierat tom (Users: [])
- **Fas 0 STÄNGD** per BUILD.md §18
