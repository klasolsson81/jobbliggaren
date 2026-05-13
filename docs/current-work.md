# Current work — JobbPilot

**Status:** **F2-P8c komplett + LIVE-VERIFIERAT 2026-05-13 ~07:15. Tag `v0.2.3-dev` deploy 25782988366 success. SyncPlatsbankenStreamJob första cron-tick (07:00:48 UTC) persisterade 417 JobAds från live JobTech v2 — fetched=565, added=417, skipped=148, errors=0, duration=41.97s. End-to-end-bekräftelse av hela P8b+P8c-stacken. TD-73 punkt 2 stängd; punkt 4 prod-gating-batch kvarstår.**
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

### Smoke-test live-resultat (efter SSO-refresh ~07:15 UTC)

```
SyncPlatsbankenStreamJob: klart — source=Platsbanken,
fetched=565, added=417, updated=0, archived=0, skipped=148, errors=0,
durationSec=41.97
```

End-to-end-verifierat: DI-graph Worker → IJobSource → JobTech v2 → handler → DB.
417 JobAds persisterade i `job_ads`-tabellen vid första `*/10`-cron-tick efter
Worker-startup. v2 path-migration + sanitizer + race-säkra upsert-handlers
alla bekräftade i prod. 148 skipped = validation-failure efter sanering
(items utan title/desc/url/company — förväntat ratio).

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
