# Current work — JobbPilot

**Status:** **F2-P8c komplett 2026-05-13 ~07:00. Tag `v0.2.3-dev` LIVE på dev (deploy `25782988366` success). 1 commit (`81dfab6`) — 28 filer, 1868 insertions, 837/837 tester grönt (+43 nya). 6 CTO-beslut auto-go (audit-wire α + right-to-erasure-iv defererade till TD-73 prod-gating-batch). Smoke-test mot CloudWatch + admin-trigger blockerat på AWS SSO-token-refresh (Klas).**
**Senast uppdaterad:** 2026-05-13 (session-end efter F2-P8c + tag-deploy)
**HEAD:** `81dfab6`
**Deploy:** `v0.2.3-dev` live på `https://dev.jobbpilot.se/api/ready` (200 OK)
**Långsiktig bana:** `docs/steg-tracker.md`
**Tech debt:** `docs/tech-debt.md` (aktiva) + `docs/tech-debt-archive.md` (stängda)

---

## Aktivt nu — F2-P8c komplett, smoke-test väntar AWS SSO-refresh

### Levererat denna session (1 commit)

| Commit | Innehåll |
|---|---|
| `81dfab6` | feat(jobads): F2-P8c — JobTech Hangfire-jobben + race-säker upsert + 30d-retention |

### Granskningstrail

- `docs/sessions/2026-05-13-0700-f2-p8c-hangfire-jobs.md` — session-log
- Agent-rapporter inline (dotnet-architect + senior-cto-advisor + code-reviewer + security-auditor)
- Tidigare session: `docs/sessions/2026-05-13-0600-f2-p8b-jobtech-infra.md`

### Leveranser

| Område | Innehåll |
|---|---|
| **Application commands** | `UpsertExternalJobAdCommand` + `ArchiveExternalJobAdCommand` (system-commands utan IAuthenticatedRequest, ej IAuditableCommand — aggregerad audit per job-run per CTO punkt 1) + outcome-enums |
| **Application jobs** | `SyncPlatsbankenStreamJob` (cron `*/10`, overlap-window 15min), `SyncPlatsbankenSnapshotJob` (cron `0 2`, per-item mediator-loop), `PurgeStaleRawPayloadsJob` (cron `30 4`, ExecuteUpdateAsync — TD-73 punkt 2) |
| **Application ports** | `IDbExceptionInspector` (DIP, Postgres 23505), `IAppDbContext.Detach(object)`, `JobSourceRetentionOptions` |
| **Infrastructure** | `DbExceptionInspector` (internal sealed singleton), `AddJobSources` utökad med inspector + JobSourceRetentionOptions alias-bind + 3 Job-DI-registreringar |
| **Worker** | `SyncPlatsbankenStreamWorker` (Hangfire-wrapper `[DisableConcurrentExecution(540)]`), `AddJobSources` nu i Worker, RecurringJobRegistrar med 6 jobb i nytt cron-schema |
| **P8b refaktor** | `SyncPlatsbankenSnapshotCommandHandler` tunn shim runt SyncPlatsbankenSnapshotJob — admin-trigger + nattjobb delar kodväg |

### ADRs

- **ADR 0032** Accepted oförändrad. P8c-scope levererad. §8-amendment 2026-05-12 punkt 2 ✓ klar (raw_payload retention 30d). Punkt 4 (right-to-erasure-cascade) defererad till prod-gating-batch per CTO.

### TD-status

- **TD-73** Major — punkt 2 ✓ stängd, punkt 4 omformulerad till **prod-gating-batch** (audit-wire α + right-to-erasure buntade — ingen v0.2-prod-tag utan båda)

Aktiva oförändrade: 18 (TD-73 fortfarande Major aktivt).

### Reviewers INLINE (CLAUDE.md §9.2)

| Reviewer | Tidpunkt | Verdict |
|---|---|---|
| dotnet-architect | INNAN kod | Design-skiss approved 8 punkter. 4 multi-approach lyfta till CTO |
| senior-cto-advisor | EFTER architect, INNAN kod | 6 beslut entydigt mot principer (Martin/Fowler/Beck/Saltzer-Schroeder/GDPR). Audit-wire α + right-to-erasure defererade till TD-73 prod-batch (CC-tolkning av Klas "minimera STOPP") |
| code-reviewer | EFTER impl, INNAN commit | GO. 0 Blockers, 0 Major, 2 Minor (doc-fixes — båda in-block-fixade) |
| security-auditor | EFTER impl, INNAN commit | APPROVED. 0 Critical, 0 GDPR-Blockers, 0 Major, 3 Sec-Min (acceptable as-is) |

### Tester (full svit grön)

- Domain.UnitTests: 218 (oförändrat)
- Application.UnitTests: 270 → **307** (+37: handlers + jobs + inspector + P8b-test-refaktor)
- Architecture.Tests: 37 → **46** (+9: P8cJobsLayerTests)
- Api.IntegrationTests: 234 (oförändrat)
- Worker.IntegrationTests: 6 (oförändrat)
- Migrate.UnitTests: 6 (oförändrat)

Totalt backend: **837/837 grönt** (+43 nya).

### Disciplinmissar fångade + fixade

1. dotnet format whitespace i SyncPlatsbankenStreamJob switch-block → dotnet format + re-stage
2. `IDbExceptionInspector.cs` XMLdoc klipp/klistra-fel → skrev om verbatim
3. `JobSourceRetentionOptions.SectionName` dead/missvisande → konstant borttagen + alias-bind-not

### Klas-pending (smoke-test blockerat)

- **AWS SSO-token expired** — re-auth krävs för CloudWatch-läsning av:
  - `SyncPlatsbankenStreamJob: startad`-event från första `*/10`-tick (~07:00 UTC)
  - Admin-trigger `POST /api/v1/admin/job-ads/sync/platsbanken` mot dev
  - `job_ads`-tabellen får rader (External-ref + sanerad RawPayload)
  - 30d-retention manuellt-test (tillfälligt `RawPayloadRetentionDays=0`)

### Pending operativt (oförändrat sedan P8b)

- JobTech-API-key registrering (apirequest.jobtechdev.se nedlagd; JobStream v2 är open API)
- Frontend-deploy till Vercel
- BUILD.md §9.1 sync mot ADR 0032 §3 — Klas-instruktion krävs

---

## Nästa session — Klas-val

1. **TD-73 prod-gating-batch:** audit-wire-α + right-to-erasure-admin-endpoint buntade i en ADR-amendment-pass (ADR 0022 payload-aktivering + ADR 0032 §8 audit-mekanism-spec). Krävs INNAN v0.2-prod-tag.
2. **F2-P9 search/filter-yta** (TD-70) — GET `/api/v1/job-ads` med `?ssyk=...&region=...&q=...` per JobTech v2 `occupation-concept-id` + `location-concept-id`.
3. **Frontend-deploy** till Vercel + JobAd-katalog UI.

---

## Tidigare sessioner (kort)

- **2026-05-13 morgon** (denna): F2-P8c JobTech Hangfire-jobben + race-säker upsert + 30d-retention. 1 commit `81dfab6`, tag `v0.2.3-dev` deploy success. 43 nya tester.
- **2026-05-13 natt:** F2-P8b JobTech Infrastructure-leverans (Refit + Resilience + admin-trigger). 5 commits, tag `v0.2.2.1-dev`. TD-73 punkt 1+3 klara.
- **2026-05-12 kväll:** F2-P7 + P8a + bootstrap + aggregate-review. 17 commits, 3 nya ADRs (0032/0033/0034), tag `v0.2.1-dev`.
