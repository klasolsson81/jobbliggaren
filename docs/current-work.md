# Current work — JobbPilot

**Status:** STEG 11 KLAR. **Alla Fas 1 prod-deploy-blockare stängda i kod** (TD-22 + TD-17 + TD-21). Operativa pre-launch-gates dokumenterade i runbooks. Nästa: STEG 12 — kräver beslut (Alt A Fas 0-stängning rek).
**Senast uppdaterad:** 2026-05-09
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**STEG 11 klar.** Tre block i ordning TD-22 → TD-17 → TD-21, alla med parallella security-auditor + code-reviewer reviews per CLAUDE.md §9.2. TD-21 gick även genom re-review efter Sec-Major-fix-runda. Backend totalt 502 tester gröna (157 Domain + 183 Application + 23 Architecture + 117 Api Integration + 22 Worker).

### STEG 11 — Fas 1 prod-blockare-cleanup

**Strategi:** Stäng kvarvarande tre Fas 1 prod-deploy-blockare (TD-17/21/22) i ordning policy-först → mest-kod → mest-avgränsat. Pre-launch-gates dokumenterade i runbooks så Fas 0-stängning (Alt A nästa STEG) kan applicera dem operativt.

#### Block 1 — TD-22 (App-logg-redaction + retention) — ADR 0024 D7

**Klas-policy-beslut:** 30d CloudWatch-retention (matchar Art. 17-fönstret) + IP /24+/48-anonymisering vid logg-tid (defense-in-depth) + EmailHash-HMAC defererad till Fas 2.

- `IIpAnonymizer`-port (Application) + `IpAnonymizer`-impl (Infrastructure) — lyft från privat metod i `RequestContextProvider`
- `RequestContextProvider` + `AuthAuditLogger` delegerar nu till delad port (en sanning för IPv4 /24, IPv6 /48, IPv4-mapped→IPv4)
- `IIpAnonymizer.UnknownLabel`-konstant (interface const) eliminerar magic string
- ADR 0024 utökad med D7 (Sju delbeslut totalt — räknare uppdaterad)
- Pre-launch-gate i `docs/runbooks/aws-setup.md` §3.2 (CloudWatch LogGroup retention=30)
- Tech-debt: TD-22 delvis stängd (kod-redaction klar, CloudWatch-konfig Fas 0-stängning); ny TD-27 (EmailHash-HMAC Fas 2)
- Reviews: 0 Critical/Major. Sec-Minor-3 (exception-middleware ex.Message) defererad till TD-10. Code-Minor-2 (UA-trunkering) accepterad medvetet.
- Tester: +13 (8 IpAnonymizer Theory + 4 AuthAuditLogger inkl IPv6 + 1 IIpAnonymizer arch-test allow-list)

#### Block 2 — TD-17 (Hangfire prod-härdning, 6 punkter)

5/6 punkter stängda i kod + runbook:

- ✓ Punkt 1 — `HangfireWorkerOptions` config-driven med allow-list production-defense (`IsDevelopment || IsEnvironment("Test")`). Fail-loud `InvalidOperationException` utanför dev/test om PrepareSchemaIfNecessary=true. Range-validering 1-300 på ShutdownTimeoutSeconds.
- ✓ Punkt 2 — Runbook `docs/runbooks/hangfire-schema.md` (Install.sql-export CPM-anpassad, GRANT-modell, schema-state-felsökning)
- ✓ Punkt 3 — `// SECURITY:`-kommentar i Worker/Program.cs + 8-punkts dashboard-auth-checklista i runbook §5 (CSRF, rate-limit, session-expire, CSP, no-cache, granulär audit, read-only-roll, version-check)
- ✓ Punkt 5 — Kalibrerings-fas-anteckning i runbook §8 (första 21d post-deploy är detect-ghosted-anomaliska volymer förväntade)
- ✓ Punkt 6 — `BackgroundJobServerOptions.ShutdownTimeout=25s` (default via HangfireWorkerOptions) + explicit `HostOptions.ShutdownTimeout+3s` (synlig timeout-kedja: Hangfire 25s → Host 28s → Fargate 30s → SIGKILL). Idempotency-tabell i runbook §6.
- ⏸ Punkt 4 — ConnectionStrings split (jobbpilot_app + jobbpilot_worker) defererad till Fas 0-stängning (kräver två AWS Secrets Manager-poster)

Plus: cron-kollision åtgärdad (detect-ghosted 03:00 → 03:30 UTC), REVOKE PUBLIC i runbook §4 (Sec-Major-2), grep-pattern fixad för CPM, Hangfire.AspNetCore-trim defererad till TD-19.

- Reviews: 4 Sec-Major (alla fixade in-block: allow-list defense, REVOKE PUBLIC, dashboard-checklist-utvidgning, range-validering). 0 Code-Major.
- Tester: +5 `HangfireWorkerOptionsTests` (defaults, section-name, full+partial overlay, missing-section)

#### Block 3 — TD-21 (Rate-limiting på DELETE /me + auth)

Tre ASP.NET Core RateLimiter-policies efter `UseAuthentication`:

- `account-deletion` 1 req/60s per UserId (claim "sub"). Anonymous → `NoLimiter` (RequireAuthorization returnerar 401 innan endpoint exekveras)
- `auth-write` 20 req/min per IP (login + register). OWASP-CGN-kompatibel default (höjd från initial 10 efter Sec-Minor-3)
- `auth-loose` 30 req/min per IP (logout). Permissivt — logout är idempotent

Defense-in-depth-fix per security-auditor:
- ✓ `UseForwardedHeaders` middleware tillagd FÖRE auth (Sec-Major-1) — pre-launch KnownNetworks=ALB-VPC-CIDR-konfig dokumenterad i `aws-setup.md §3.3`
- ✓ `OnRejected`-callback (LoggerMessage source-gen, EventId 2001, ingen PII — endast Path + Method) + Retry-After-header (RFC 6585) (Sec-Major-3)
- ✓ Separat `StrictRateLimitApiFactory` för isolerad 429-integration-test (Sec-Major-2). `xunit.runner.json: parallelizeTestCollections=false` förhindrar env-var-race mellan ApiFactory + StrictRateLimitApiFactory.
- ✓ Frontend typed-confirmation-UX + re-auth-prompt → ny TD-28 (defererad till frontend-STEG, inte prod-blocker)

- Reviews: TD-21 gick två rundor — initial 3 Sec-Major + 4 Sec-Minor. Re-review efter fix-runda **Approved** för stängning.
- Tester: +8 (6 RateLimitingOptionsTests + 2 AuthWriteRateLimitTests). 117 Api Integration-tester gröna.

### Säkerhets-fixar värda att lyfta från STEG 11

- **TD-22 / IIpAnonymizer:** defense-in-depth — audit-tabellen anonymiserades redan men app-loggen bar parallell PII. Lyft till delad port garanterar att framtida konsumenter får samma maskning.
- **TD-21 / UseForwardedHeaders:** utan denna middleware blir rate-limiting effektivt no-op i prod bakom ALB (alla requests från proxy-IP → samma bucket). Pre-launch-gate i runbook blockerar deploy utan VPC-CIDR-konfig.
- **TD-21 / OnRejected utan PII:** klient-IP är personuppgift per GDPR Recital 30 — många implementationer loggar IP "för säkerhets skull" och bryter mot Recital 30. Path + Method räcker för incident-respons utan att kompromettera GDPR.
- **TD-17 / allow-list production-defense:** pure `IsProduction()` täcker inte Staging/Preprod/Demo-miljöer. Allow-list `IsDevelopment || IsEnvironment("Test")` säkrar alla okända miljöer by default.

## Senaste commits

| SHA | Beskrivning |
|-----|-------------|
| f566d5d | feat(api): TD-21 — rate-limiting + ForwardedHeaders + OnRejected (DELETE /me + auth) |
| d5dde0b | feat(worker): TD-17 — Hangfire prod-härdning + Fargate SIGTERM-handling |
| c7094aa | feat(auditing): TD-22 — IIpAnonymizer-port + app-logg-redaction (ADR 0024 D7) |
| cfbfbc4 | docs(tech-debt): bredda TD-13 + TD-17, lägg till TD-26 (extern review-input) |
| c07e52f | chore(security): allowlist test-password fingerprint för STEG 10b DeleteMeTests |
| 5ace16f | docs: STEG 10b docs-sync (runbook + tech-debt + steg-tracker + current-work + session-logg) |

## Tester totalt

- **Backend:** 502 (157 Domain + 183 Application + 23 Architecture + 117 Api Integration + 22 Worker) — +27 sedan STEG 10b
- **Frontend:** 65 Vitest + 19 Playwright E2E (oförändrat)

## När nästa session startar

1. Kör `git log --oneline -10` — verifiera HEAD = f566d5d
2. Verifiera backend-tester: kör test-exen direkt under `tests/*/bin/Debug/net10.0/`
3. Läs `docs/steg-tracker.md` §6 för STEG 12-kandidater
4. Läs senaste session-logg (STEG 11) för detaljer
5. Läs `docs/runbooks/aws-setup.md` §3.2-3.4 (pre-launch-gates) + `docs/runbooks/hangfire-schema.md` (om Alt A Fas 0-stängning)

## Kända begränsningar / quirks

- **postgres-dev** på port **5435**
- **`dotnet ef`** plockar inte upp `appsettings.Local.json` — använd `export ConnectionStrings__Postgres=...`
- **`dotnet test`** på solution-nivå returnerar "Zero tests ran" (xunit.v3.mtp-v2-issue) — kör test-exen direkt
- **API kräver `ASPNETCORE_ENVIRONMENT=Development`** för Redis-connstring
- **Hangfire 3 jobs**: audit-log-retention 03:00 + detect-ghosted **03:30** (flyttat STEG 11) + hard-delete-accounts 04:00 UTC
- **Hangfire-schema** skapas automatiskt vid Worker-start i dev men FAIL-LOUD i Staging/Production utan explicit overlay (TD-17)
- **Rate-limiting**: 1/60s per UserId på DELETE /me, 20/min per IP på auth-write, 30/min per IP på auth-loose
- **xunit.runner.json**: `parallelizeTestCollections=false` så env-var-overlay inte race:as mellan ApiFactory och StrictRateLimitApiFactory
- **`UseForwardedHeaders` aktiv i Api**: i dev no-op (direkt-anrop), i prod kräver `KnownNetworks=ALB-VPC-CIDR` innan första traffic
- **Worker.csproj** drar fortfarande in Hangfire.AspNetCore (TD-19 Fas 2 trim)

## Open follow-ups

**Fas 0-stängning operativa pre-launch-gates (alla dokumenterade i runbooks):**
- CloudWatch LogGroups retention=30 (`aws-setup.md` §3.2)
- ALB ForwardedHeaders KnownNetworks=VPC-CIDR (`aws-setup.md` §3.3)
- Bootstrap-IAM-user cleanup (`aws-setup.md` §3.4)
- Hangfire schema-DDL via Install.sql + REVOKE PUBLIC (`hangfire-schema.md` §3-4)
- ConnectionStrings split (jobbpilot_app + jobbpilot_worker) (`hangfire-schema.md` §4)
- `appsettings.Production.json` overlay (Hangfire-konfig)

**Övriga TD:**
- TD-13 (PII-encryption Fas 2)
- TD-14 (DeleteResumeVersion Fas 4)
- TD-15 (Resume-formulär a11y Fas 1)
- TD-18 (intervju-states-utökning)
- TD-19 (Worker defense-in-depth Fas 2 — inkl Hangfire.AspNetCore-trim per STEG 11)
- TD-20 (SqlQuery<FormattableString>-refactor opportunistiskt)
- TD-23 (Redis MULTI/EXEC opportunistiskt)
- TD-24 (cascade-paginering Fas 4)
- TD-25 (per-konto try/catch opportunistiskt)
- TD-26 (AI-kostnadstak Fas 4)
- TD-27 (EmailHash-HMAC Fas 2)
- TD-28 (Frontend typed-confirmation-UX för DELETE /me)

## STEG 12 — kräver beslut

**Alt A — Fas 0-stängning** (rek): applicera STEG 11:s pre-launch-gates + första prod-deploy. security-auditor invokeras vid IAM/secrets/deploy-block.

**Alt B — Fortsätt Fas 1-features:** Application UX-pass, Resume-version-Tailored, etc.

**Alt C — TD-19 Worker defense-in-depth:** Hangfire.AspNetCore → Hangfire.NetCore + arch-test-utökning.

Min rek: Alt A naturlig efter STEG 11 — gates dokumenterade, Klas applicerar dem operativt.
