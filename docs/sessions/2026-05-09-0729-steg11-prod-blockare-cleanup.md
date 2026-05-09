---
session: STEG 11
datum: 2026-05-09
slug: steg11-prod-blockare-cleanup
status: KLAR
commits:
  - c7094aa  feat(auditing): TD-22 — IIpAnonymizer-port + app-logg-redaction (ADR 0024 D7)
  - d5dde0b  feat(worker): TD-17 — Hangfire prod-härdning + Fargate SIGTERM-handling
  - f566d5d  feat(api): TD-21 — rate-limiting + ForwardedHeaders + OnRejected
---

# STEG 11 — Fas 1 prod-blockare-cleanup

## Mål

Stäng kvarvarande tre Fas 1 prod-deploy-blockare (TD-17 Hangfire prod-härdning, TD-21 rate-limiting, TD-22 app-logg-retention). Dokumentera operativa pre-launch-gates i runbooks så Fas 0-stängning (nästa STEG) kan applicera dem operativt.

Klas:s val Alt B (av A/B/C) + ordning TD-22 → TD-17 → TD-21 (policy-först → mest-kod → mest-avgränsat).

## Block 1 — TD-22 (App-logg-redaction + retention)

**Klas-policy-beslut (4 frågor besvarade):**
1. CloudWatch retention: **30 dagar** (matchar Art. 17-fönstret)
2. IP-redaction: **/24+/48-anonymisering vid logg-tid** (defense-in-depth)
3. EmailHash-HMAC: **defer till Fas 2** (kräver KMS-integration)
4. Dokumentation: **ADR 0024 D7-tillägg** (samma policy-familj som D1-D6)

**Implementation:**

`IIpAnonymizer`-port lyft från privat metod i `RequestContextProvider.Anonymize()` till delad port (Application). `IpAnonymizer`-impl (Infrastructure) konsumeras nu både av audit-pipelinen och `AuthAuditLogger` så app-loggen får samma maskning som audit-tabellen. `IIpAnonymizer.UnknownLabel`-konstant (interface const) eliminerar magic string mellan call-sites.

`RequestContextProvider` förändrad till delegering. `AuthAuditLogger` injicerar nu porten + maskar IP innan `LogLoginSucceeded`/`LogLoginFailed`/`LogLogoutSucceeded`-anropen.

DI: `AddSingleton<IIpAnonymizer, IpAnonymizer>()` i `AddPersistence` (stateless BCL-helper, en singleton tillgänglig för Api+Worker).

**Reviews:**
- security-auditor: 0 Critical/Major. 3 Minor (null-asymmetri, CloudWatch-konfig-enforcement, exception-middleware ex.Message → TD-10).
- code-reviewer: 0 Major. 3 Minor + 3 Nit. Magic string + arch-test + IPv6-test fixade in-block. UA-trunkering accepterad medvetet (256 är audit_log-kolumn-gräns).

**Resultat:** ADR 0024 D7 ny (Sju delbeslut totalt). TD-22 markerad delvis stängd (kod-redaction klar, CloudWatch-konfig Fas 0-stängning). Ny TD-27 för EmailHash-HMAC. +13 tester (8 Theory IpAnonymizer + 4 AuthAuditLogger + 1 arch-test).

## Block 2 — TD-17 (Hangfire prod-härdning, 6 punkter)

**Implementation:**

`HangfireWorkerOptions` config-driven via `RateLimiting:*`-section. Två properties:
- `PrepareSchemaIfNecessary` — default `true` Development/Test. Production-defense via **allow-list** (`IsDevelopment || IsEnvironment("Test")`) — skyddar Staging/Preprod/Demo också. Fail-loud `InvalidOperationException` vid uppstart om annan miljö har `true`.
- `ShutdownTimeoutSeconds` — default 25 (under Fargate stopTimeout 30s). Range-validering 1-300.

Plus explicit `HostOptions.ShutdownTimeout = +3s` så timeout-kedjan (Hangfire 25s → Host 28s → Fargate 30s → SIGKILL) är synlig på ett ställe i Program.cs.

Cron-kollision åtgärdad: `detect-ghosted` flyttat 03:00 → 03:30 UTC så det inte krockar med `audit-log-retention`.

Runbook `docs/runbooks/hangfire-schema.md` (~200 rader):
- §3 Initial Install.sql-export (CPM-anpassad grep-pattern mot Directory.Packages.props)
- §4 GRANT-modell med **REVOKE PUBLIC** innan GRANT-block (least-privilege per Sec-Major-2)
- §5 8-punkts dashboard-auth-checklista (CSRF, rate-limit, session-expire, CSP, no-cache, granulär audit, read-only-roll, version-check)
- §6 Fargate SIGTERM + idempotency-tabell per jobb
- §8 Kalibrerings-fas (första 21d post-deploy är detect-ghosted-anomaliska volymer förväntade)

Punkt 4 (ConnectionStrings split jobbpilot_app + jobbpilot_worker) defererad till Fas 0-stängning — kräver två AWS Secrets Manager-poster.

**Reviews:**
- security-auditor: 4 Major flagged, alla 4 fixade in-block (allow-list defense → täcker staging/preprod, REVOKE PUBLIC → eliminerar default Postgres-läs-yta, dashboard-checklist-utvidgning, range-validering). 1 Sec-Nit (Hangfire.AspNetCore-paket-trim) defererad till TD-19 Fas 2.
- code-reviewer: 0 Major. 3 Minor (test-fil-placering pragmatic, production-defense ej direkt unit-tested, appsettings.Production.json saknas) — alla noterade i tech-debt follow-ups.

**Lärdomar:**
- Allow-list production-defense > pure `IsProduction()` — Staging/Preprod/Demo-miljöer som inte heter exakt "Production" hade hade ingen spärr utan allow-list
- Hangfire.AspNetCore drar in Microsoft.AspNetCore.* (HTTP-bagage) — bryter mot ADR 0023 Worker HTTP-fri-disciplin. Trim till Hangfire.NetCore identifierad som TD-19-utvidgning Fas 2.
- 30-min-padding mellan retention och detect-ghosted är gratis (jobben är <1s typiskt) och gör recovery-flöden tydligare

**Resultat:** TD-17 markerad delvis stängd (5/6 punkter ✓, punkt 4 ConnectionStrings split ⏸ Fas 0). +5 tester (HangfireWorkerOptions defaults + binding).

## Block 3 — TD-21 (Rate-limiting på DELETE /me + auth)

**Initial implementation:**

Tre ASP.NET Core RateLimiter-policies efter `UseAuthentication`:
- `account-deletion` 1/60s per UserId (claim "sub")
- `auth-write` 10/min per IP (login + register) [INITIALT — höjd senare]
- `auth-loose` 30/min per IP (logout)

`RateLimitingOptions` config-driven (samma mönster som HangfireWorkerOptions). ApiFactory höjer IP-policies till 10000/min via env-var så befintliga 109 tester inte rate-limit:as på localhost-partition (account-deletion håller default — varje test skapar unik user → unik partition).

**Initial reviews flaggade 3 Sec-Major:**

1. **Sec-Major-1**: `Connection.RemoteIpAddress` ger inte client-IP bakom proxy/ALB → rate-limiting effektivt no-op i prod. Krävs `UseForwardedHeaders`.
2. **Sec-Major-2**: login-rate-limit-test saknas (huvud-syftet är credential-stuffing-skydd, det MÅSTE testas).
3. **Sec-Major-3**: ingen `OnRejected`-callback → 429-events loggas inte i CloudWatch. Plus saknad `Retry-After`-header.

**Fix-runda:**

- `UseForwardedHeaders` middleware tillagd FÖRE auth med default ASP.NET-säkra-defaults (ForwardLimit=1, KnownProxies=loopback). Pre-launch KnownNetworks=ALB-VPC-CIDR-konfig dokumenterad i `aws-setup.md §3.3` med konkret kod-skiss + VPC-CIDR-discovery-kommando + verifieringssteg.
- `OnRejected`-callback via LoggerMessage source-gen (EventId 2001, "Rate limit exceeded. Path={Path} Method={Method}") — **ingen PII** (klient-IP är personuppgift per GDPR Recital 30; email/session är direkt PII). Path + Method räcker för incident-respons.
- `Retry-After`-header från `Lease.TryGetMetadata(MetadataName.RetryAfter)` (RFC 6585-compliance).
- Ny `StrictRateLimitApiFactory` (separat WebApplicationFactory) som inte sätter env-var-overrides → defaults gäller. `xunit.runner.json: parallelizeTestCollections=false` förhindrar race med `ApiFactory` på process-globala env-vars.
- 2 nya `AuthWriteRateLimitTests`: spam login → 429 inom 25 anrop (ska komma efter 20 enligt nya default), 429-respons inkluderar Retry-After.

Plus Sec-Minors:
- Anonymous DELETE /me → `RateLimitPartition.GetNoLimiter("anonymous-deletion")` (RequireAuthorization 401:ar innan endpoint, ingen attack-yta)
- AuthWrite default 10 → **20/min** (OWASP-CGN-kompatibel, höjd för skolor/företagsnät)
- QueueLimit=0-kommentar om DoS-risk
- Semantik-kommentarer ovanför varje AddPolicy

**Re-review (security-auditor):** **Approved** — TD-21 kan stängas. Alla 3 Sec-Major adresserade med korrekt scope. Default ASP.NET-beteendet är säkert (accepterar inte spoofat XFF), pre-launch-gate i runbook blockerar prod-launch utan VPC-CIDR-konfig.

**Lärdomar:**
- xunit collection-parallelism + process-globala env-vars är inkompatibla — `parallelizeTestCollections=false` krävs när två fixturer delar env-vars
- LoggerMessage source-gen är obligatorisk under CA1848 i JobbPilot — `_logger.LogWarning(...)` i hot path bryter build
- DELETE /me-rate-limit-test är svår eftersom DELETE invaliderar sessionen (efterföljande anrop får 401 innan rate-limiter triggar) — login-spam testar samma IP-partition-mekanik istället

**Resultat:** TD-21 stängd. Ny TD-28 (frontend typed-confirmation-UX). +8 tester (6 RateLimitingOptions + 2 AuthWriteRateLimit).

## Beslut

- **Allow-list-pattern för production-defense** över pure `IsProduction()` — robusthet mot okända miljö-namn (Staging/Preprod/Demo)
- **Defense-in-depth genom delad port** — `IIpAnonymizer` säkerställer audit-tabellen och app-loggen aldrig drift:ar isär på maskning
- **OnRejected utan PII** — Path + Method räcker för incident-respons; klient-IP är personuppgift per GDPR Recital 30
- **Pre-launch-gates som operativ-deferral** — KnownNetworks/CloudWatch-retention/ConnectionStrings split dokumenteras i runbook hellre än att blockera kod-stängning. Fas 0-stängning applicerar gates som operativ-uppgift.
- **TD-17 punkt 4 + frontend-UX defererade till separata TD** — TD-21:s frontend-UX (typed-confirmation + re-auth-prompt) bröts ut till TD-28 så scope-creep undviks och frontend-STEG kan ta dem separat

## Commits

| SHA | Beskrivning |
|-----|-------------|
| c7094aa | feat(auditing): TD-22 — IIpAnonymizer-port + app-logg-redaction (ADR 0024 D7) |
| d5dde0b | feat(worker): TD-17 — Hangfire prod-härdning + Fargate SIGTERM-handling |
| f566d5d | feat(api): TD-21 — rate-limiting + ForwardedHeaders + OnRejected |

## Tester totalt

- Backend: 502 (157 Domain + 183 Application + 23 Architecture + 117 Api Integration + 22 Worker) — +27 sedan STEG 10b
- Frontend: oförändrat

## Nästa session

STEG 12 — kräver beslut. Tre kandidater:

- **Alt A — Fas 0-stängning (rek)**: applicera STEG 11:s pre-launch-gates + första prod-deploy. CloudWatch retention=30, ALB ForwardedHeaders KnownNetworks, ConnectionStrings split, Hangfire schema-DDL, REVOKE PUBLIC, bootstrap-IAM-cleanup, appsettings.Production.json overlay, GitHub Actions tag-deploy verifierad.
- **Alt B — Fas 1-features**: Application UX-pass, Resume-version-Tailored, etc.
- **Alt C — TD-19 Worker defense-in-depth**: Hangfire.AspNetCore → Hangfire.NetCore + arch-test-utökning.

Rekommendation: Alt A naturlig efter STEG 11 — gates dokumenterade, Klas applicerar dem operativt och får första prod-miljö.

## Open follow-ups (tech-debt)

- TD-17 punkt 4 (ConnectionStrings split, Fas 0)
- TD-19 utvidgad med Hangfire.AspNetCore-trim
- TD-22 CloudWatch retention=30 (Fas 0-stängning operativ)
- TD-27 EmailHash-HMAC (Fas 2 — kombineras med TD-13 KMS)
- TD-28 frontend typed-confirmation-UX (Fas 1 frontend-STEG)
