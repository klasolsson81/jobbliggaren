# Current work — JobbPilot

**Status:** STEG 14a APPLIED 2026-05-10 ~18:50. GitHub OIDC-federation + CI/CD-pipeline aktiv. Provider + dev-deploy-roll i AWS, AWS_DEPLOY_ROLE_ARN GH-secret satt. build.yml mekaniskt verifierad — alla pipeline-steps körs korrekt på ubuntu-runner. Frontend (Next.js 16, pnpm 9 efter `packages: ['.']`-fix) helgrön. Backend Domain/Application/Architecture (363 tester) grönt i CI. Backend Integration-tests (88 Api errors + 1 Worker fail) **fail i CI men passar lokalt** — förexisterande issue, ej 14a-regression, lyft som TD-37 för 14b-fix. STEG 14a:s mål uppnått: pipelinen är skapad och fungerande (mekaniskt korrekt). Worker fortfarande PLACEHOLDER-creds (14b fixar). Spend oförändrat ~$79.50/mån. **Nästa:** **STEG 14b** (DDL-init + ConnectionStrings split + Worker-recovery + TD-37-fix), sedan STEG 14c (first formal tag-deploy + Fas 0-stängning).
**Senast uppdaterad:** 2026-05-10
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**STEG 14a APPLIED 2026-05-10.** GitHub OIDC + CI/CD-workflows. Se session-logg `docs/sessions/2026-05-10-1850-steg14a-oidc-cicd.md` för detaljer (M1+M2 strict-fix från security-auditor, 4-iteration CI-debug på SDK 10.0.201 + pnpm 9 + MTPlatform-syntax, TD-37-lyft).

### Apply-state

| Resurs | Identifier |
|---------|-----------|
| **OIDC provider** | `arn:aws:iam::710427215829:oidc-provider/token.actions.githubusercontent.com` |
| **Dev deploy-roll** | `arn:aws:iam::710427215829:role/jobbpilot-github-actions-deploy-dev` |
| **GH secret** | `AWS_DEPLOY_ROLE_ARN` satt på `klasolsson81/jobbpilot` |
| **build.yml** | Triggar på push main + PR, last run 25634087757 (mekaniskt grön — Integration-fail från TD-37) |
| **deploy-dev.yml** | Triggar på `v*-dev` tag, oprövat (testas i 14c) |
| **Trust-policy scope** | `repo:klasolsson81/jobbpilot:ref:refs/tags/v*-dev` (ENBART, strict per security-auditor) |
| **role-duration-seconds** | 900 (15 min, kort blast-radius) |
| **max_session_duration** | 3600 (1h tak) |

### Pre-existing infra (oförändrat sedan STEG 13c)

| Resurs | Identifier |
|---------|-----------|
| Public URL | `https://dev.jobbpilot.se/api/ready` (200 OK + HSTS) |
| RDS | `jobbpilot-dev-rds.cj84yieu0qwc.eu-north-1.rds.amazonaws.com:5432` |
| Redis | `master.jobbpilot-dev-redis.tyzmvb.eun1.cache.amazonaws.com:6379` |
| ECR API | `710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-api:latest` |
| ECR Worker | `710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-worker:latest` |
| ALB | `jobbpilot-dev-alb-1232220213.eu-north-1.elb.amazonaws.com` |
| ACM-cert | `arn:aws:acm:eu-north-1:710427215829:certificate/f72a79d7-f964-49c7-abb5-cf81b8639d6a` |

## Senaste commits (denna session)

| SHA | Beskrivning |
|-----|-------------|
| (TBD denna commit) | docs: STEG 14a APPLIED — current-work + steg-tracker + session-logg + tech-debt TD-37 |
| `708db86` | fix(ci): build.yml — förenkla test-output + lägg till pnpm-workspace packages-fält |
| `7da656f` | fix(ci): build.yml — backa restore/build till positional sln + pnpm --ignore-workspace |
| `673cd0b` | fix(ci): build.yml — .NET 10 SDK --solution-flag + pnpm version pin |
| `cefc587` | ci: STEG 14a — build.yml + deploy-dev.yml + 3 agent-reviews |
| `b975c9f` | feat(infra): STEG 14a — GitHub OIDC-modul + dev-deploy-roll |
| `52590f6` | docs: STEG 13c APPLIED — ADR 0027 Accepted + 0026 Superseded + current-work + steg-tracker + session-logg |

## Tester totalt

- **Backend:** 554 lokalt (157 Domain + 183 Application + 23 Architecture + 26 Worker + 165 Api Integration)
  - **CI-state:** 363 gröna (Domain + Application + Architecture + Frontend Vitest), 89 fail (TD-37 — investigation vid 14b)
- **Frontend:** Vitest helgrön i CI

## Open follow-ups

**Operativa AWS-uppgifter (kvarvarande Fas 0):**
- STEG 14b: DDL-init via AWS Session Manager port-forward + ConnectionStrings split + Worker-recovery
- STEG 14b: TD-37 Investigate Integration-tests-fail i CI
- STEG 14c: First formal tag-deploy via deploy-dev.yml + verify Bootstrap-IAM-user borta + Fas 0-stängning

**Defererade från STEG 14a-reviews (TD-kandidater):**
- versions.tf saknas i route53/acm-moduler (chore-commit)
- Architecture-tests-split från unit/integration (CI-feedback-optimering)
- Terraform-CI-race-runbook-tillägg
- Drop `--no-build` i build.yml (om MTPlatform-issue dyker upp)
- Node-version → `.nvmrc`
- ECR_REGISTRY-account-ID-duplikering

**Nya TDs från STEG 14a:**
- TD-37: Backend Integration tests fail i CI (Major, blockerar tag-deploy via deploy-dev.yml förrän löst)

**Övriga TD (oförändrat):**
- TD-13 (PII-encryption Fas 2 — kombineras med TD-27)
- TD-14 (DeleteResumeVersion Fas 4)
- TD-15 (Resume-formulär a11y Fas 1)
- TD-18 (intervju-states-utökning)
- TD-19 (Worker defense-in-depth Fas 2 — inkl Hangfire.AspNetCore-trim)
- TD-20 (SqlQuery<FormattableString>-refactor opportunistiskt)
- TD-23 (Redis MULTI/EXEC opportunistiskt)
- TD-24 (cascade-paginering Fas 4)
- TD-25 (per-konto try/catch opportunistiskt)
- TD-26 (AI-kostnadstak Fas 4)
- TD-27 (EmailHash-HMAC Fas 2)
- TD-28 (Frontend typed-confirmation-UX för DELETE /me)
- TD-29 (strict readiness Fas 2)
- TD-30 — STÄNGD (domänköp + Route53 + ACM-cert) per STEG 13c
- TD-31 (test för UseHttpsRedirection env-gate — utvidgas av TD-33)
- TD-32 (TLS-policy uppgrade till PQ-2025-09 — Fas 1)
- TD-33 (HSTS pipeline-gating-test via WebApplicationFactory)
- TD-34 (DNSSEC aktivering vid Fas 1-trigger)
- TD-35 (Apex + www ACM-cert vid prod-stack-rollout)
- TD-36 (mTLS / in-VPC-encryption vid Fas 2 multi-tenant)

## När nästa session startar

### STEG 14b förberedelse

**Pre-flight för STEG 14b** (`DDL-init + ConnectionStrings split + Worker-recovery + TD-37-fix`):

1. SSO-login: `aws sso login --profile jobbpilot`
2. Verifiera dev-state oförändrat sedan denna session:
   - `curl -I https://dev.jobbpilot.se/api/ready` → 200 OK + HSTS-header
   - `aws ecs describe-services --cluster jobbpilot-dev-cluster --services jobbpilot-dev-api jobbpilot-dev-worker` → api 1/1, worker restart-loop
   - `aws iam get-role --role-name jobbpilot-github-actions-deploy-dev` → exists
3. Verifiera last build.yml-run state via `gh run list --workflow=build.yml --limit 1`

### STEG 14b-scope

- **TD-37 investigation först** (ej blocker för DDL men måste lösas före first formal tag-deploy):
  - Reproducera Integration-test-fail lokalt med samma Linux-Docker-setup (devcontainer eller `act`)
  - Verbose Serilog-output i Test-env för 500-error-roten
  - Verifiera DB-migrations-flow under WebApplicationFactory
  - Eventuell isolering till separat workflow-job (continue-on-error)
- **DDL-init** via AWS Session Manager + port-forward via ECS Exec på Api-task (gratis, omedelbart, SSM agent finns redan på Fargate via EcsExecMessaging-policy):
  - Exportera Hangfire `Install.v22.sql` från lokal NuGet-cache
  - Skapa `jobbpilot_migrations` + `jobbpilot_app` + `jobbpilot_worker`-roller per `docs/runbooks/hangfire-schema.md §3-4`
  - REVOKE PUBLIC + ALTER DEFAULT PRIVILEGES
- **ConnectionStrings split:**
  - `aws secretsmanager put-secret-value --secret-id jobbpilot/dev/db/app-connection-string ...` (jobbpilot_app-creds)
  - `aws secretsmanager put-secret-value --secret-id jobbpilot/dev/db/hangfire-storage-connection-string ...` (jobbpilot_worker-creds)
- **Worker-recovery:**
  - `aws ecs update-service --force-new-deployment` på api + worker
  - Verifiera Worker stable + Hangfire-jobs running

## Kända begränsningar / quirks (uppdaterade från STEG 14a)

- **AWS ALB `RedirectActionConfig.StatusCode`** hardlimited till HTTP_301 | HTTP_302
- **STRATO som registrar** — UI varnar 24h men praktiskt ~30 min
- **`dig` saknas på Windows** — använd PowerShell `Resolve-DnsName -Server`
- **HSTS-pipeline-ordning kritisk:** `UseForwardedHeaders → UseHsts → UseHttpsRedirection`
- **Worker restart-loop fortsätter** tills STEG 14b
- **`dotnet test` på sln-nivå** — .NET 10 SDK 10.0.201 kräver `--solution`-flag specifikt för `dotnet test` (inte för restore/build)
- **MTPlatform-test-runner CLI-flaggor** måste passa via `--`-separator. `--results-directory` är VSTest-only och triggar help-output i MTPlatform → exit 1
- **pnpm 9+** kräver `packages:`-fält i pnpm-workspace.yaml om filen finns. Solo-projekt deklareras med `packages: ['.']`
- **`pnpm/action-setup@v4`** kräver `version`-input om `packageManager`-fält saknas i package.json
- **GitHub OIDC trust-policy:** strikt scope-disciplin — undvik `pull_request`/`refs/heads/main` när workflows inte gör AWS-anrop därifrån (security-auditor M1+M2)
- **Säkerhets-flagga:** security-auditor + code-reviewer agenter har bypass-väg via `powershell.exe` trots user-deny-rule. Granska `.claude/settings.json` permission-config

## Done last session (STEG 14a)

- Ny Terraform-modul `modules/github_oidc/` (4 filer, 4 AWS-resurser): OIDC-provider, dev-deploy-roll, inline-policy least-privilege, role-policy-attachment
- Anrop från `prod/baseline` (delad resurs likt KMS/Route53). Outputs: `github_oidc_provider_arn`, `github_actions_deploy_dev_role_arn`
- 2 GitHub Actions workflows: `build.yml` (push main+PR, backend+frontend parallellt + ci-aggregat), `deploy-dev.yml` (tag v*-dev, OIDC + Docker build/push + ECS deploy + smoke-test)
- 3 agent-reviews parallellt: security-auditor (APPROVE-WITH-FIXES, M1+M2 strict-fix tillämpade), code-reviewer (APPROVE-WITH-FIXES, n3+n1 fix), dotnet-architect (APPROVE-WITH-FIXES, MTPlatform-syntax + Worker-warning fix). Rapporter sparade till `docs/reviews/2026-05-10-steg14a-*.md`
- `terraform apply` PASS — 4 add (OIDC-provider + role + policy + attachment)
- GH secret `AWS_DEPLOY_ROLE_ARN` satt
- 4 commits + push: feat infra OIDC, ci workflows+reviews, fix CI iter 1, fix CI iter 2
- 4-iteration CI-debug med kollektiv lärdom: SDK 10.0.201 `--solution`-flag specifikt för `dotnet test`, MTPlatform `--results-directory` är VSTest-only, pnpm 9 kräver `packages:`-fält
- `web/jobbpilot-web/pnpm-workspace.yaml` fix:ad med `packages: ['.']` (legitim förexisterande config-bug exponerad av CI)
- TD-37 lyft för Integration-tests-CI-debug (89 backend-test-fail i CI passar lokalt)
- build.yml mekaniskt verifierad: alla pipeline-steps körs korrekt; failures är runtime-test-fel (TD-37), inte pipeline-konfig
