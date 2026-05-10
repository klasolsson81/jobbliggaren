# Current work — JobbPilot

**Status:** STEG 13c APPLIED 2026-05-10 ~17:48. HTTPS aktivt på `https://dev.jobbpilot.se` med HSTS-header. ADR 0026 → Superseded by ADR 0027. Domän jobbpilot.se registrerad hos STRATO + NS pekar på AWS-Route53. ACM-cert utfärdat. ALB HTTP→HTTPS-redirect (301) + HSTS aktiv (max-age=31536000 + includeSubDomains). Api task-def rev 2 deployed. Smoke-test PASS. Worker fortfarande restart-loop pga PLACEHOLDER DB-creds (STEG 14 fixar). Spend ~$79.50/mån löper ($79 STEG 13b + $0.50 hosted zone). README.md tillagd separat. Nästa: **STEG 14** (GitHub Actions tag-pipeline + DDL-init + IAM cleanup) — stänger Fas 0 per BUILD.md §18.
**Senast uppdaterad:** 2026-05-10
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**STEG 13c APPLIED 2026-05-10.** Full DNS + TLS-aktivering på dev. Se session-logg `docs/sessions/2026-05-10-1748-steg13c-https-applied.md` för detaljer (mid-apply 308→301-fix, ECS rolling deployment, smoke-test).

### Apply-state

| Resurs | Identifier |
|---------|-----------|
| **Public URL** | `https://dev.jobbpilot.se/api/ready` (200 OK + HSTS) |
| **HTTP redirect** | `http://dev.jobbpilot.se` → `301` → `https://dev.jobbpilot.se:443` |
| **Domain** | `jobbpilot.se` (STRATO registrar, NS → AWS Route53) |
| **Hosted zone** | `Z028392711DGTDR1MGVC9` (prod/baseline.tfstate) |
| **ACM-cert** | `arn:aws:acm:eu-north-1:710427215829:certificate/f72a79d7-f964-49c7-abb5-cf81b8639d6a` |
| **ALB-DNS** | `jobbpilot-dev-alb-1232220213.eu-north-1.elb.amazonaws.com` |
| **ALB target group** | API-task rev 2 (med `Alb__HttpsEnabled=true`) |
| **HSTS** | `max-age=31536000; includeSubDomains` (1 år, ej preload) |
| **TLS-policy** | `ELBSecurityPolicy-TLS13-1-2-2021-06` |
| **Redirect** | HTTP_301 (AWS ALB hardlimit; 308 ej implementerbart utan Lambda@Edge) |

### Pre-existing infra (oförändrat sedan STEG 13b)

| Resurs | Identifier |
|---------|-----------|
| RDS | `jobbpilot-dev-rds.cj84yieu0qwc.eu-north-1.rds.amazonaws.com:5432` |
| Redis | `master.jobbpilot-dev-redis.tyzmvb.eun1.cache.amazonaws.com:6379` |
| ECR API | `710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-api:latest` (HSTS-fix) |
| ECR Worker | `710427215829.dkr.ecr.eu-north-1.amazonaws.com/jobbpilot-dev-worker:latest` |
| VPC | `vpc-0659b4386bba9dc31` |
| ECS Cluster | `jobbpilot-dev-cluster` |

## Senaste commits (denna session)

| SHA | Beskrivning |
|-----|-------------|
| (TBD denna commit) | docs: STEG 13c APPLIED — current-work + steg-tracker + session-logg + ADR 0027/0026-statusar |
| (TBD denna commit) | fix(infra): STEG 13c apply — ALB redirect 308 → 301 (AWS-API-begränsning) + HTTPS-flip tfvars-edit |
| `8befcce` | docs: lägg till professionell README med snabblänkar, mermaid-arkitektur, lokal-setup |
| `f2bc555` | docs: STEG 13c — ADR 0027 (Proposed) + 3 agent-reviews |
| `3b46945` | feat(api): STEG 13c — HSTS-impl + EnsureSafeForEnvironment + 17 tests |
| `f0f45c5` | feat(infra): STEG 13c — Route53 + ACM-moduler + 308-redirect + dev DNS+ALIAS |
| `e5eddde` | docs: STEG 13b APPLIED 2026-05-10 — current-work + steg-tracker + session-logg |

## Tester totalt

- **Backend:** 554 (157 Domain + 183 Application + 23 Architecture + 26 Worker + 165 Api Integration — 165 inkluderar 17 nya HstsOptionsTests)
- **Frontend:** 65 Vitest + 19 Playwright E2E (oförändrat)

## Open follow-ups

**Operativa AWS-uppgifter (kvarvarande Fas 0):**
- Apply STEG 14 (GitHub Actions tag-pipeline + DDL-init + Bootstrap-IAM-user cleanup)
- Worker restart-loop pågår tills STEG 14 sätter riktiga creds via `aws secretsmanager put-secret-value` post-DDL
- VPC Flow Logs aktivering (säkerhetshygien — separat task, lyfts i STEG 14 eller efter)

**Sec-Major dokumenterade i ADR 0027 (defererade till Fas 1):**
- Sec-Major-1 — DNSSEC saknas på Route53-zonen. Trigger-villkor: multi-tenant / OAuth-aktivering / säkerhetsincident / Fas 2-start
- Sec-Major-2 — verifierat fixat (HSTS aktiv)

**Sec-Minor från STEG 13c:**
- Sec-Minor-1 — TLS-policy 2021-06 → uppgrade till PQ-2025-09 vid Fas 1 (TD-32)
- Sec-Minor-2 — Plain-text ALB→ECS intra-VPC (Fas 0 acceptans, Fas 2-uppgrade till mTLS — TD-36)
- Sec-Minor-3 — verifierat fixat (timeouts.create=75m)

**Nya TDs från STEG 13c (33-36):**
- TD-32: TLS-policy uppgrade till `ELBSecurityPolicy-TLS13-1-2-2025-09` (post-quantum) — Fas 1
- TD-33: HSTS pipeline-gating-test via WebApplicationFactory — anti-regression Sec-Major-2
- TD-34: DNSSEC aktivering vid Fas 1-trigger (cross-region KMS us-east-1 + KSK-rotation-runbook)
- TD-35: Apex (`jobbpilot.se`) + `www.jobbpilot.se` ACM-cert + ALB-cert-association vid prod-stack-rollout
- TD-36: mTLS / in-VPC-encryption (ALB → ECS) vid Fas 2 multi-tenant

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
- TD-30 — **STÄNGD** (domänköp + Route53 + ACM-cert) per STEG 13c
- TD-31 (test för UseHttpsRedirection env-gate — utvidgas av TD-33)

## När nästa session startar

### STEG 14 förberedelse

**Pre-flight för STEG 14** (`GitHub Actions tag-pipeline + första prod-deploy`):

1. SSO-login: `aws sso login --profile jobbpilot`
2. Verifiera dev-state oförändrat sedan denna session:
   - `curl -I https://dev.jobbpilot.se/api/ready` → 200 OK + HSTS-header
   - `aws ecs describe-services --cluster jobbpilot-dev-cluster --services jobbpilot-dev-api` → 1/1 running
3. Verifiera RDS reachable för DDL-init (manual SSO + jump till private subnet eller AWS Session Manager)

### STEG 14-scope (per BUILD.md + steg-tracker.md)

- `.github/workflows/` för build+test+push-to-ECR + tag-trigger för deploy (`v*-dev`/`v*-rc`/`v*`)
- Hangfire schema-DDL via `Install.sql` + REVOKE PUBLIC i RDS (per `docs/runbooks/hangfire-schema.md §3-4`)
- ConnectionStrings split i AWS Secrets Manager (jobbpilot_app + jobbpilot_worker via DDL-roles)
- Bootstrap-IAM-user cleanup (per `aws-setup.md §3.4` — STEG 14 sista steg)
- Första formal deploy till `dev.jobbpilot.se` via tag-pipeline
- **Stänger Fas 0** per BUILD.md §18

## Kända begränsningar / quirks

- **AWS ALB `RedirectActionConfig.StatusCode` hardlimited till HTTP_301 | HTTP_302** — 308 (POST-method-bevarande) ej implementerbart utan Lambda@Edge eller CloudFront-rewrite. Mitigation: HSTS+browser-cache gör POST-anrop går direkt till HTTPS efter första GET.
- **STRATO som registrar** — UI varnar "upp till 24h" för NS-propagering men praktiskt ~30 min för `.se`-zonen.
- **`dig` saknas på Windows** — använd PowerShell `Resolve-DnsName -Server <resolver>` istället för DNS-debugging.
- **HSTS-pipeline-ordning kritisk:** `UseForwardedHeaders → UseHsts → UseHttpsRedirection`. Bakom ALB är `Request.IsHttps` annars false → HSTS sätts aldrig. Dokumenterat i Program.cs-kommentar.
- **Worker restart-loop fortsätter** tills STEG 14 (förväntad transient state).
- **`dotnet test` på solution-nivå/csproj-nivå** hittar inte tester med filter-syntax — kör test-exen direkt: `tests/.../bin/Debug/net10.0/JobbPilot.Api.IntegrationTests.exe -class "FullyQualifiedName"` (xunit3-syntax: `-class`, inte `--filter`).

## Done last session (STEG 13c)

- Två nya Terraform-modules: `modules/route53/` (apex zone) + `modules/acm/` (DNS-validerad cert med 75m-timeout för svensk-registrar-marginal)
- ALB-modul: HTTP→HTTPS-redirect aktivt (HTTP_301 efter mid-apply 308→301-fix pga AWS-API-begränsning)
- env-stacks: `prod/` + `dev/` edits — module-anrop, A-ALIAS-record, output-exports
- HSTS-implementation: `HstsOptions.cs` (sealed class + EnsureSafeForEnvironment-paritet med ForwardedHeadersConfig), Program.cs (services.AddHsts + UseHsts gate:at), `appsettings.Production.json` overlay
- HstsOptionsTests: 11 testmetoder, 17 test-cases, alla gröna
- ADR 0027 (Accepted) supersedar ADR 0026 (Superseded)
- 3 agent-reviews: code-reviewer / security-auditor / dotnet-architect (alla APPROVE-with-fixes, alla viktiga fynd inline-fixade)
- README.md tillagd (sober civic-utility-stil, 2 Mermaid-diagram, 17 snabblänkar)
- 5 commits till `main` + push: `f0f45c5`, `3b46945`, `f2bc555`, `8befcce`, samt 2 final commits för STEG 13c apply (denna session)
- Smoke-test PASS: `https://dev.jobbpilot.se/api/ready` → 200 OK + HSTS-header verifierat via curl
