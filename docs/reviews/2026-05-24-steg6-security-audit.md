# Security-audit — STEG 6 backfill (Approach A)

**Datum:** 2026-05-24
**Agent:** security-auditor (agentId `ab002ec9ede71d352`)
**Status:** CONDITIONAL PASS — säkerhetsmässigt mergeklar.

---

## Klassificering

- **0 Block / 0 Critical / 0 High / 0 Medium**
- **3 Minor TDs** (alla post-MVP / fas 2+):
  - **Minor-1** AdminAuthorizationBehavior triggar inte på fire-and-forget enqueue (defense-in-depth-gap mot Mediator-pipelinen). HTTP-lager-skydd via `RequireAuthorization(AuthorizationPolicies.Admin)` räcker för MVP.
  - **Minor-2** Ingen dedikerad rate-limit för admin-endpoints. Komprometterat admin-konto kan trigga upprepade backfill-runs. Single-admin (Klas) = låg MVP-yta.
  - **Minor-3** Api enqueue:ar Application-klass utan `[DisableConcurrentExecution]`. Race ger no-op-overhead (idempotent via UNIQUE-index), inte korruption. Samma arkitekturfråga som Minor-1 — konvergerar.
- **GDPR:** PASS — ingen veto-trigger. Inga nya PII-flöden, ingen ny sub-processor, EU-residency bevarad.

## Områden granskade

| Område | Verdict |
|---|---|
| OWASP A03 SQL injection (EF Core) | PASS |
| OWASP A01 Broken Access Control (admin auth) | PASS |
| OWASP API4 Unrestricted Resource Consumption | PASS — se Minor-2 |
| GDPR Art. 32 — sanitization av extern payload | PASS |
| Refit URL-template injection | PASS |
| Hangfire-client storage-only (ADR 0023 HTTP-fri-invariant) | PASS |
| Connection-string-läckage | PASS |
| 404-semantik (annons borttagen från JobTech) | PASS |
| GDPR Art. 30 — audit-trail | PASS |
| Logging hygiene | PASS |
| Race-säkerhet mot snapshot-cron | PASS |
| Concurrency-skydd för fire-and-forget | CONDITIONAL — Minor-3 |

## Praise

- `RefetchByExternalIdAsync` återanvänder `TryConvertToImportItem` + `SanitizeForStorage` (Saltzer/Schroeder least common mechanism)
- `JobAd.Archive()` rörs medvetet INTE av per-ID-404 (ADR 0032-amendment 2026-05-23 retention-disciplin bevaras)
- `JobAdsSynced` med `JobType="backfill"` återanvänds — inget audit-koncept-bloat
- `db.Detach(newJobAd)` förhindrar UnitOfWorkBehavior-retry-storm
- Per-item child-scope isolerar EF-change-tracker
- `MaxItemsPerRun=100_000` + `PerItemDelayMs=200ms` defense-in-depth-cap
- `AsAsyncEnumerable` ↦ ADR 0045 Worker-mem soft cap 512 MiB respekterad
- `PrepareSchemaIfNecessary = false` — schema-bootstrap-äganderätt hos Worker (least privilege)
- Inga PII-fält loggas vid backfill-progression
- HTTP-fri-invariant per ADR 0023 bevarad — `AddHangfireServer` inte anropad i Api

## Operationell not för prod-deploy

Prod-deploy bör sätta `ConnectionStrings:HangfireStorage` → `jobbpilot_worker`-roll (minimal GRANT på `hangfire.*`) i stället för att Api delar `jobbpilot_app`-rollen. Inte Block (dev-fas), men följdfråga för prod-checklistan.

## Klassificering för commit-batch

**CONDITIONAL PASS, säkerhetsmässigt mergeklar.**
