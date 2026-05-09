# Current work — JobbPilot

**Status:** STEG 12 KLAR. **Alla kod-pre-launch-gates stängda** (Alt A1 av A4-sekvens A1→A2→A3 för Fas 0-stängning). Worker HangfireStorage-fallback + Api ForwardedHeadersConfig + production-defense + båda Production overlays. Sec-Major-1+2 fixade in-block. Nästa: STEG 13 (Terraform-stack — Alt A2).
**Senast uppdaterad:** 2026-05-09
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**STEG 12 klar.** Tre block (Worker resolver → Api config → båda overlays) + fix-runda för Sec-Major-1 (allow-list-symmetri) och Sec-Major-2 (CloudFront-prefix-list-clarification i runbook). Backend totalt 537 tester gröna (157 Domain + 183 Application + 23 Architecture + 26 Worker + 148 Api Integration), upp från 502 i STEG 11 (+35).

### STEG 12 — Kod-pre-launch-gates (Alt A1 av A4-sekvens)

**Strategi:** Klas valde A4-sekvens (A1 → A2 → A3) efter discovery-rapport som avslöjade att startpromptens Alt A antog mer infra än som existerar (inget `.github/workflows/`, inga Dockerfiles, Terraform har bara baseline-stacken). STEG 12 = A1 (smalt: kod-pre-launch-gates), STEG 13 = A2 (Terraform-stack), STEG 14 = A3 (GitHub Actions + första deploy + IAM-cleanup).

#### Block 1 — TD-17 punkt 4 (Worker HangfireStorage ConnectionString-fallback)

- `HangfireConnectionStringResolver.Resolve(IConfiguration)` — statisk testbar metod, fallback-kedja `HangfireStorage → Postgres`
- Prod-overlay sätter `HangfireStorage` → routar Worker till `jobbpilot_worker`-rollen (DML-only på `hangfire.*`); dev faller tillbaka på `Postgres` (en sanning lokalt)
- `Worker/appsettings.Production.json` (ny): `Hangfire.PrepareSchemaIfNecessary=false` + `Hangfire.ShutdownTimeoutSeconds=25`. ConnectionStrings injiceras via env-vars från ECS task-definition + AWS Secrets Manager.
- Tester: +5 (HangfireStorage-prefer + Postgres-fallback + throw-both-missing + null-arg + const-stability)

#### Block 2 — TD-21 KnownNetworks (Api ForwardedHeadersConfig + production-defense)

- `ForwardedHeadersConfig` (sealed class, init-only properties, public const SectionName) — pattern-konsistens med `RateLimitingOptions` + `HangfireWorkerOptions`
- Fail-loud parse-metoder via .NET 10 `System.Net.IPNetwork.TryParse` + `ForwardedHeadersOptions.KnownIPNetworks` (inte deprecated `Microsoft.AspNetCore.HttpOverrides.IPNetwork`)
- **Sec-Major-1 fix in-block:** `EnsureSafeForEnvironment(envName)` allow-list `IsDevelopment\|\|IsEnvironment("Test")` — symmetri med Worker `safeForAutoSchema`. Tom KnownNetworks utanför dev/test → uppstart-throw → ECS-container startar inte.
- **Sec-Major-2 docs-fix in-block:** overlay-kommentar + `aws-setup.md §3.3` förtydligade om CloudFront edge-IPs i AWS-managed prefix-list (`com.amazonaws.global.cloudfront.origin-facing`). ALB-only-deploy använder `ForwardLimit = 1`.
- `Api/appsettings.Production.json` (ny): `ForwardedHeaders.KnownNetworks=[]` (pre-launch-gate, populeras i STEG 13 efter VPC-skapande), `ForwardLimit=1`
- Tester: +31 (17 parse + 14 EnsureSafeForEnvironment)

#### Block 3 — appsettings.Production.json overlays

- Två overlay-filer + JSON-comments-stilade sektioner. ASP.NET `JsonConfigurationProvider` stödjer `// xxx`-comments (`JsonReaderOptions.CommentHandling = Skip`).
- Comments är load-bearing: dokumenterar pre-launch-gates, env-var-injection-strategi, ConnectionStrings-frånvaro

### Säkerhets-fixar värda att lyfta från STEG 12

- **Sec-Major-1 / EnsureSafeForEnvironment:** symmetri-miss mellan Worker `safeForAutoSchema` och Api `KnownNetworks`-tomt-array hade öppnat OWASP A07-yta i prod-launch-fönstret. Lyft till testbar metod ger strukturell anti-regression istället för bara runbook-disciplin.
- **Sec-Major-2 / ForwardLimit-CloudFront:** initial overlay-kommentar antydde att `ForwardLimit=2` räcker bakom ALB+CloudFront. Men CloudFront edge-IPs är dynamiska — bara VPC-CIDR i KnownNetworks → middleware stoppar vid CloudFront-hop → `RemoteIpAddress` blir CloudFront-IP, inte klient-IP. Förtydligad runbook + ALB-only-deploy använder `ForwardLimit=1`.
- **`System.Net.IPNetwork` istället för deprecated `Microsoft.AspNetCore.HttpOverrides.IPNetwork`:** ASPDEPR005-warning fångade det. Modernt API, bättre IPv6-stöd.

## Senaste commits

| SHA | Beskrivning |
|-----|-------------|
| d879f96 | docs(runbooks): STEG 12 Sec-Major-2 — ForwardLimit + CloudFront-prefix-list |
| bb26fec | feat(api): TD-21 — ForwardedHeadersConfig + production-defense (STEG 12) |
| f8488b4 | feat(worker): TD-17 punkt 4 — HangfireConnectionStringResolver-fallback (STEG 12) |
| 8211ddb | docs: STEG 11 docs-sync (current-work + steg-tracker + session-logg) |
| f566d5d | feat(api): TD-21 — rate-limiting + ForwardedHeaders + OnRejected |
| d5dde0b | feat(worker): TD-17 — Hangfire prod-härdning + Fargate SIGTERM-handling |

## Tester totalt

- **Backend:** 537 (157 Domain + 183 Application + 23 Architecture + 26 Worker + 148 Api Integration) — +35 sedan STEG 11
- **Frontend:** 65 Vitest + 19 Playwright E2E (oförändrat)

## När nästa session startar

1. Kör `git log --oneline -10` — verifiera HEAD = (post docs-sync-commit)
2. Verifiera backend-tester: kör test-exen direkt under `tests/*/bin/Debug/net10.0/`
3. Läs `docs/steg-tracker.md` §3 STEG 13 (Terraform-stack scope)
4. Läs senaste session-logg (STEG 12) för detaljer
5. Läs `docs/runbooks/aws-setup.md` (hela — STEG 13 är Terraform-baseline-utvidgning)
6. Läs `docs/runbooks/hangfire-schema.md` (operativ procedur som ska tas via Terraform/IaC)

## Kända begränsningar / quirks

- **postgres-dev** på port **5435**
- **`dotnet ef`** plockar inte upp `appsettings.Local.json` — använd `export ConnectionStrings__Postgres=...`
- **`dotnet test`** på solution-nivå returnerar "Zero tests ran" (xunit.v3.mtp-v2-issue) — kör test-exen direkt
- **API kräver `ASPNETCORE_ENVIRONMENT=Development`** för Redis-connstring
- **Hangfire 3 jobs**: audit-log-retention 03:00 + detect-ghosted **03:30** + hard-delete-accounts 04:00 UTC
- **Hangfire-schema** skapas automatiskt vid Worker-start i dev men FAIL-LOUD utanför Development/Test (TD-17 + STEG 12 explicit)
- **Rate-limiting**: 1/60s per UserId på DELETE /me, 20/min per IP på auth-write, 30/min per IP på auth-loose
- **xunit.runner.json**: `parallelizeTestCollections=false`
- **`UseForwardedHeaders` aktiv i Api**: i dev no-op (direkt-anrop); i prod **fail-loud-throw vid uppstart om KnownNetworks tom utanför Development/Test** (STEG 12 Sec-Major-1)
- **Worker.csproj** drar fortfarande in Hangfire.AspNetCore (TD-19 Fas 2 trim)
- **HangfireStorage ConnectionString-fallback** (STEG 12): prod-overlay sätter `ConnectionStrings:HangfireStorage` (jobbpilot_worker-roll); dev faller tillbaka på `ConnectionStrings:Postgres`

## Open follow-ups

**Operativa AWS-uppgifter (alla dokumenterade i runbooks, appliceras i STEG 13/14):**
- VPC + subnets + RDS + Redis + ECS + ECR + ALB + Route53 + ACM (STEG 13 Terraform)
- CloudWatch LogGroups retention=30 (`aws-setup.md` §3.2 — STEG 13 Terraform inline)
- ALB ForwardedHeaders KnownNetworks=VPC-CIDR (`aws-setup.md` §3.3 — STEG 13 Terraform overlay-population)
- ConnectionStrings split (jobbpilot_app + jobbpilot_worker) (`hangfire-schema.md` §4 — STEG 14)
- Hangfire schema-DDL via Install.sql + REVOKE PUBLIC (`hangfire-schema.md` §3-4 — STEG 14)
- GitHub Actions tag-pipeline (`v*-dev`/`v*-rc`/`v*`) — STEG 14
- Bootstrap-IAM-user cleanup (`aws-setup.md` §3.4 — STEG 14 sista steg)

**Övriga TD:**
- TD-13 (PII-encryption Fas 2)
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

**Sec-Minor från STEG 12 (defererade):**
- Sec-Minor-1: ForwardedHeaders flag-set hårdkodad (lyfts om CloudFront-Host-routing aktualiseras)
- Sec-Minor-2: Worker-allow-list `IsEnvironment("Test")` är dead branch tills test-fixture sätter `DOTNET_ENVIRONMENT=Test`
- Sec-Minor-3: Felmeddelanden vid CIDR/IP-parse läcker raw-värdet (CIDR/IP är publik infra-info, ingen secret-leak)
- Sec-Nit-1: Verifiera Prettier i lint-staged strippar inte JSON-comments i `appsettings.Production.json`
