# Current work — JobbPilot

**Status:** **STEG 14c APPLIED 2026-05-10 ~21:55. FAS 0 STÄNGD.** TD-37 root cause identifierat och fixat (ConnectionStrings__Redis env-var saknades i ApiFactory + StrictRateLimitApiFactory → IConnectionMultiplexer.Connect() failade på Linux-CI utan default Redis). Backend CI grön (554/554). First formal tag-deploy `v0.1.0-dev` PASS via deploy-dev.yml end-to-end (OIDC + ECR + ECS + smoke). IAM-policy `ecs:DescribeTaskDefinition` separerad till egen statement med `Resource: *` (AWS API loggar request som `*` oavsett ARN-format). Bootstrap-IAM-user verifierat tom (`aws iam list-users → []`). Worker + API stable, smoke-test 200 + HSTS aktiv. Spend ~$79.65/mån. **Nästa:** **Fas 1** (Core Domain — auth-flöde + kärn-CRUD-polish, BUILD.md §18).
**Senast uppdaterad:** 2026-05-10
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

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
