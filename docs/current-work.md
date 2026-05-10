# Current work — JobbPilot

**Status:** STEG 14b APPLIED 2026-05-10 ~21:10. **Worker recovery COMPLETE.** Migrate-task (a2a988a4) PASS efter 5 iterationer (DO-block-bug → SET ROLE-bug → charmap-bug → KMS-bug → exit 0). 3 Postgres-roller skapade (jobbpilot_migrations + jobbpilot_app + jobbpilot_worker), Hangfire-schema + 13 tabeller installerade, GRANT-modell aktiv, ConnectionStrings split skrivna till Secrets Manager. Worker + API force-new-deployment → COMPLETED stable. Smoke-test https://dev.jobbpilot.se/api/ready → 200 + HSTS aktiv. Hangfire BackgroundJobServer running med 4 workers + alla dispatchers. TD-37 (Integration-tests CI-fail) deferrad till 14c. TD-38 lyft (Trust Server Certificate hardening). Spend ~$79.65/mån. **Nästa:** **STEG 14c** (TD-37 investigation + first formal tag-deploy + Fas 0-stängning).
**Senast uppdaterad:** 2026-05-10
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**STEG 14b APPLIED 2026-05-10.** DDL-init + Worker-recovery via one-shot ECS-task. Se session-logg `docs/sessions/2026-05-10-2010-steg14b-ddl-worker-recovery.md` för detaljer (4-iteration debug-cykel, agent-fynd Sec-Minor-5 empiriskt motbevisad, RDS-master limited superuser-quirk, charmap-windows-issue).

### Apply-state

| Resurs | Identifier |
|---------|-----------|
| **Migrate task-def** | `jobbpilot-dev-migrate:4` (rev 4 efter 4 image-iterationer) |
| **Migrate-image** | `710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-migrate:14b-9113bed-fix3` |
| **Postgres-roller** | `jobbpilot_migrations` (CREATE+USAGE på hangfire) + `jobbpilot_app` (DML/DDL på public) + `jobbpilot_worker` (DML-only på hangfire.*) |
| **Hangfire-schema** | 13 tabeller skapade i `hangfire`-schema, ägs av jobbpilot_migrations |
| **App connection-secret** | `jobbpilot/dev/db/app-connection-string-BCvQsM` (jobbpilot_app-creds) |
| **Hangfire connection-secret** | `jobbpilot/dev/db/hangfire-storage-connection-string-2FI8PN` (jobbpilot_worker-creds) |
| **Worker** | task-def rev 1, 1/1 stable, BackgroundJobServer running, 4 workers + dispatchers |
| **API** | task-def rev 2, 1/1 stable, smoke-test PASS |
| **task_migrate-roll** | `jobbpilot-dev-ecs-task-migrate` (PutSecretValue på 2 ARN:er + GetSecretValue på master + KMS Decrypt+GenerateDataKey) |

### Pre-existing infra (oförändrat sedan STEG 14a)

| Resurs | Identifier |
|---------|-----------|
| Public URL | `https://dev.jobbpilot.se/api/ready` (200 OK + HSTS) |
| OIDC provider | `arn:aws:iam::710427215829:oidc-provider/token.actions.githubusercontent.com` |
| Dev deploy-roll | `arn:aws:iam::710427215829:role/jobbpilot-github-actions-deploy-dev` |
| build.yml + deploy-dev.yml | Push triggar build, tag v*-dev triggar deploy |

## Senaste commits (denna session)

| SHA | Beskrivning |
|-----|-------------|
| (TBD denna commit) | feat(migrate): STEG 14b — Migrate console-app + IAM + ECS task-def + Worker-recovery |
| `9113bed` | docs: STEG 14a APPLIED — current-work + steg-tracker + session-logg + TD-37 |
| `708db86` | fix(ci): build.yml — förenkla test-output + lägg till pnpm-workspace packages-fält |
| `7da656f` | fix(ci): build.yml — backa restore/build till positional sln + pnpm --ignore-workspace |
| `673cd0b` | fix(ci): build.yml — .NET 10 SDK --solution-flag + pnpm version pin |
| `cefc587` | ci: STEG 14a — build.yml + deploy-dev.yml + 3 agent-reviews |
| `b975c9f` | feat(infra): STEG 14a — GitHub OIDC-modul + dev-deploy-roll |
| `52590f6` | docs: STEG 13c APPLIED — ADR 0027 Accepted + 0026 Superseded |

## Tester totalt

- **Backend:** 554 lokalt (157 Domain + 183 Application + 23 Architecture + 26 Worker + 165 Api Integration). Migrate-konsol oprövad i unit-tests (one-shot ops-tool, manuellt verifierad via 5 run-task-iterationer).
- **CI-state:** 363 gröna (Domain + Application + Architecture + Frontend Vitest), 89 fail = TD-37 deferrad till 14c

## Open follow-ups

**Operativa AWS-uppgifter (kvarvarande Fas 0):**
- STEG 14c: TD-37 investigation + first formal tag-deploy via deploy-dev.yml + verify Bootstrap-IAM-user borta + Fas 0-stängning

**Defererade från STEG 14b:**
- security-auditor Sec-Minor-1 (modulo-bias rejection-sampling) — accepterad risk
- security-auditor Sec-Minor-3 (defensiv identifier-validation) — partial (ValidateIdentifier finns men inte överallt)
- code-reviewer Major-2 (full CancellationToken-flow) — light implementation klar

**Nya TDs från STEG 14b:**
- TD-38: Trust Server Certificate=true persisteras till app/worker connection-strings (Fas 1 hardening — RDS-CA-bundle in i container-truststores)

**Övriga TD (oförändrat):**
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
- TD-37 (Backend Integration tests fail i CI — investigera vid 14c)
- TD-38 (Trust Server Certificate hardening Fas 1)

## När nästa session startar

### STEG 14c förberedelse

**Pre-flight för STEG 14c** (`TD-37 investigation + first formal tag-deploy + Fas 0-stängning`):

1. SSO-login: `aws sso login --profile jobbpilot`
2. Verifiera dev-state oförändrat:
   - `curl -I https://dev.jobbpilot.se/api/ready` → 200 OK + HSTS
   - `aws ecs describe-services --cluster jobbpilot-dev-cluster --services jobbpilot-dev-api jobbpilot-dev-worker` → båda 1/1 running, COMPLETED
   - `aws ecs describe-services --cluster jobbpilot-dev-cluster --services jobbpilot-dev-worker --query 'services[0].deployments[].rolloutState'` → COMPLETED
3. Verifiera GH secret `AWS_DEPLOY_ROLE_ARN` finns: `gh secret list -R klasolsson81/jobbpilot`

### STEG 14c-scope

**Block 1 — TD-37 investigation:**
- Reproducera lokalt med Linux-Docker (devcontainer eller `act`)
- Verbose Serilog för 500-error-roten på register-endpoint
- Verifiera DB-migrations under WebApplicationFactory + Testcontainers
- Eventuell isolering till separat workflow-job (continue-on-error)

**Block 2 — First formal tag-deploy:**
- `git tag v0.1.0-dev && git push origin v0.1.0-dev`
- GitHub Actions deploy-dev.yml triggar
- Verifiera CI/CD-pipelinen end-to-end (OIDC assume + ECR push + ECS update + smoke)

**Block 3 — Bootstrap-cleanup verify + Fas 0-stängning:**
- `aws iam list-users --profile jobbpilot` → tom (verifierat i 14a-discovery)
- ADR 0028 (om Fas 0-completion-decision nödvändig)
- README.md update "Status: Fas 0 → Fas 1"
- **Stänger Fas 0** per BUILD.md §18

## Kända begränsningar / quirks (uppdaterade från STEG 14b)

- **Pl/pgsql DO-blocks tar inte Npgsql-parameters** — `@role`-referenser propagerar inte in i pl/pgsql-scope. Använd två-stegs SELECT + DDL-pattern.
- **RDS-master är limited superuser** — kan inte SET ROLE utan explicit `GRANT … TO CURRENT_USER`. `CREATE SCHEMA AUTHORIZATION X` + Phase C-GRANTs kräver master-membership i target-rollen.
- **Hangfire.PostgreSql 1.21.1 har ingen exporterad Install.sql** i NuGet-paketet. `PostgreSqlObjectsInstaller.Install(conn, schemaName)` är officiella API:t (public static sedan 1.0).
- **AWS CLI charmap-codec failer på `→`-tecken i Windows-shell** — håll container-app-loggar ASCII-only.
- **`kms:GenerateDataKey` krävs för KMS-encrypted Secrets vid PutSecretValue** (envelope-encryption av nya version). security-auditor Sec-Minor-5 motbevisad empiriskt.
- **`dotnet test` på sln-nivå** — .NET 10 SDK 10.0.201 kräver `--solution`-flag specifikt för `dotnet test`.
- **MTPlatform-test-runner** — `--results-directory` är VSTest-only och triggar help-output i MTPlatform → exit 1.
- **pnpm 9+** kräver `packages:`-fält i pnpm-workspace.yaml om filen finns.

## Done last session (STEG 14b)

- Ny `src/JobbPilot.Migrate/` console-app: csproj + Program.cs (4 phases) + MigrateLog (LoggerMessage source-gen) + Dockerfile (multi-stage .NET 10 runtime, USER app)
- Directory.Packages.props: Npgsql 10.0.1→10.0.2 (transitive constraint), AWSSDK.SecretsManager 4.0.4.20, Microsoft.Extensions.Logging.Console 10.0.7
- modules/iam_ecs: ny `task_migrate`-roll med count-pattern. Permissions: GetSecretValue på master + GetSecretValue/PutSecretValue/DescribeSecret på 2 connection-secrets + KMS Decrypt+GenerateDataKey + ECS Exec
- modules/ecs: ny `aws_ecs_task_definition.migrate` med count-pattern + variables/outputs
- environments/dev: utvidgade ECR (+migrate), CloudWatch Logs (+migrate), iam_ecs (migrate-args), ecs (migrate-args + environment), variables (migrate_image_tag), outputs (`migrate_run_task_command` med interpolerad CLI-string)
- 3 agent-reviews i `docs/reviews/2026-05-10-steg14b-{security,code,architect}.md` (alla APPROVE-WITH-FIXES, fixar applicerade)
- 5 docker build + push-iterationer till ECR (`jobbpilot-dev-migrate:{14b-9113bed,fix1,fix2,fix3,latest}`)
- 5 terraform apply-iterationer (Phase 1 IAM/ECR/Logs + Phase 2 task-def rev 1-4 + IAM-policy-revert)
- 5 aws ecs run-task-iterationer (4 fail för debug + 1 PASS)
- Final task `a2a988a4dcf54273859ea5d27347f578` exit 0 — alla 4 phases PASS
- aws ecs update-service --force-new-deployment × 2 (api + worker) → båda COMPLETED stable
- Smoke-test https://dev.jobbpilot.se/api/ready → 200 + HSTS aktiv
- Hangfire BackgroundJobServer + dispatchers verified via Worker CloudWatch logs
- TD-37 deferrad till 14c, TD-38 lyft (Trust Server Certificate hardening Fas 1)
