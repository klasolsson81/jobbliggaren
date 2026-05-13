---
session: F2-P8c — JobTech Hangfire-jobben + race-säker upsert + 30d-retention
datum: 2026-05-13
slug: f2-p8c-hangfire-jobs
status: Klar — commit 81dfab6 + tag v0.2.3-dev LIVE på dev (deploy 25782988366 success). CloudWatch + admin-trigger smoke-test blockerat på AWS SSO-token-refresh (Klas).
commits:
  - 81dfab6  # feat(jobads): F2-P8c — JobTech Hangfire-jobben + race-säker upsert + 30d-retention
deploy_tag: v0.2.3-dev (live på dev.jobbpilot.se)
---

# Session 2026-05-13 (morgon) — F2-P8c JobTech Hangfire-jobben

## Mål

Per ADR 0032 §9 P8c-batch + TD-73 punkt 2:

- `SyncPlatsbankenStreamJob` (cron `*/10`)
- `SyncPlatsbankenSnapshotJob` (cron `0 2`)
- `UpsertExternalJobAdCommand` med DbUpdateException-catch-pattern (ADR 0032 §5)
- Removal-handling via `JobAd.Archive` (ADR 0032 §6)
- `PurgeStaleRawPayloadsJob` (cron `0 3` per ADR, justerat till `30 4` per
  CTO-rond) — TD-73 punkt 2
- audit-wire för `JobAdsSyncedDomainEvent` + `RawPayloadPurgedDomainEvent`
- TD-73 punkt 4 (right-to-erasure-cascade till raw_payload)
- E2E-tester

Klas-direktiv vid session-start: "håll det så automatiserat som möjligt, fråga
mig endast när det måste, eller vid stort beslut".

## Vad blev klart

| Område | Innehåll |
|---|---|
| **Application commands** | `UpsertExternalJobAdCommand` + `ArchiveExternalJobAdCommand` (system-commands utan `IAuthenticatedRequest`, ej `IAuditableCommand` per CTO-rond punkt 1) + outcome-enums |
| **Application jobs** | `SyncPlatsbankenStreamJob` (overlap-window 15min), `SyncPlatsbankenSnapshotJob` (per-item mediator-loop), `PurgeStaleRawPayloadsJob` (`ExecuteUpdateAsync`) |
| **Application ports** | `IDbExceptionInspector` (DIP, Postgres 23505), `IAppDbContext.Detach(object)`, `JobSourceRetentionOptions` |
| **Infrastructure** | `DbExceptionInspector` (internal sealed singleton), `AddJobSources` utökad med IDbExceptionInspector + JobSourceRetentionOptions alias-bind + 3 Job-DI-registreringar |
| **Worker** | `SyncPlatsbankenStreamWorker` (Hangfire-wrapper `[DisableConcurrentExecution(540)]`), `AddJobSources` nu i Worker, RecurringJobRegistrar med 6 jobb i nytt cron-schema |
| **P8b refaktor** | `SyncPlatsbankenSnapshotCommandHandler` är nu tunn shim runt SyncPlatsbankenSnapshotJob — admin-trigger + nattjobb delar kodväg |
| **TD-73 update** | Punkt 2 ✓ klar (PurgeJob); punkt 4 omformulerad till prod-gating-batch (audit-wire + right-to-erasure buntade — ingen v0.2-prod-tag utan båda) |

**Commits:** 1 (`81dfab6`) — 28 filer, 1868 insertions, 195 deletions.

## ADR-status

- **ADR 0032** Accepted oförändrad. §9 P8c-scope levererad. §8-amendment 2026-05-12
  punkt 2 (raw_payload retention 30d) ✓ klar via PurgeStaleRawPayloadsJob.
  Punkt 4 (right-to-erasure-cascade) defererad till prod-gating-batch per CTO.

## Tester (full svit grön)

| Suite | Före → Efter |
|---|---|
| Domain.UnitTests | 218 → 218 (oförändrat) |
| Application.UnitTests | 270 → 307 (+37: handlers + jobs + inspector + refaktor av P8b-test) |
| Architecture.Tests | 37 → 46 (+9: P8cJobsLayerTests) |
| Api.IntegrationTests | 234 → 234 (oförändrat) |
| Migrate.UnitTests | 6 (oförändrat) |
| Worker.IntegrationTests | 6 (oförändrat) |

Totalt backend: **837/837 grönt** (+43 nya tester för P8c).

## Reviewers INLINE (CLAUDE.md §9.2 — disciplin från F2-P8b oförändrad)

| Reviewer | Tidpunkt | Verdict |
|---|---|---|
| dotnet-architect | INNAN kod | Design-skiss approved på 8 punkter; 4 multi-approach-val flaggade för CTO |
| senior-cto-advisor | EFTER architect, INNAN kod | 6 beslut entydigt mot principer (Martin/Fowler/Beck/Saltzer-Schroeder/GDPR Art. 5+30). Audit-wire α defererad till TD-73 prod-batch (CC-tolkning av Klas-direktiv "minimera STOPP") |
| code-reviewer | EFTER impl, INNAN commit | GO. 0 Blockers, 0 Major, 2 Minor (doc-fixes — båda in-block-fixade per §9.6) |
| security-auditor | EFTER impl, INNAN commit | APPROVED. 0 Critical, 0 GDPR-Blockers, 0 Major, 3 Sec-Min (acceptable as-is) |

## CTO-rond 2026-05-13 — 6 beslut

1. **UpsertExternalJobAdCommand-shape:** två commands (Upsert + Archive) per SRP,
   aggregerad audit per job-run, `IDbExceptionInspector`-port för 23505-detection.
2. **Cursor-state D:** overlap-window `since = now - 15min` på 10-min-cron.
   YAGNI mot `sync_cursors`-tabell; idempotency via UNIQUE-index + UpdateFromSource.
3. **Snapshot-handler-refaktor (b):** orchestrator-jobbet itererar items + kallar
   `mediator.Send(UpsertExternalJobAdCommand)` per item. P8b bulk-handler ersatt
   med tunn shim.
4. (Architect-position bekräftad — ingen separat CTO-fråga.)
5. **Audit-wire α (deferred):** `ISystemEventAuditor`-port + `audit_log.payload`
   jsonb-aktivering kräver ADR 0022 + ADR 0032 amendment. CC tolkade Klas-direktiv
   "minimera STOPP" som mandat att defera till TD-73 prod-gating-batch (gemensam
   med right-to-erasure). Interim: Serilog structured-loggning av counts+cutoff
   räcker för GDPR Art. 30.
6. (Architect-position bekräftad.)
7. **Right-to-erasure iv:** defer till samma TD-73 prod-batch. 30d-retention via
   punkt 2 minimerar PII-fönstret → faktisk Art. 17-volym liten.
8. **Cron-schema:** stream `*/10`, snapshot `0 2`, audit-retention `0 3`,
   detect-ghosted `30 3`, hard-delete `0 4`, purge-raw-payloads `30 4`.

## Disciplinmissar fångade + fixade

1. **dotnet format whitespace** — pre-commit-hook fångade switch-block-indentation
   i `SyncPlatsbankenStreamJob.cs`. Fix: `dotnet format` + re-stage.
2. **`IDbExceptionInspector.cs` XMLdoc klipp/klistra-fel** — code-reviewer fångade.
   Fix: skrev om motivering verbatim.
3. **`JobSourceRetentionOptions.SectionName` dead/missvisande** — code-reviewer
   fångade (binds mot `JobTech`-section, inte `"JobSourceRetention"`).
   Fix: konstanten borttagen + XMLdoc-not om alias-bind.

## Tag-cykel + deploy

- `v0.2.3-dev` på `81dfab6` → push 06:42 UTC → deploy run `25782988366`.
- Deploy completion: 06:50 UTC (~8 min, typisk Fargate-cykel).
- Ready-probe: `https://dev.jobbpilot.se/api/ready` → **200 OK** verifierat efter deploy.

## Smoke-test status

**Verifierat:**
- ✅ Ready-probe 200 OK efter deploy
- ✅ Pipeline-mekanik (CT-propagation, handlerregistrering) bevisad redan i P8b CTO-rond 5

**Blockerat på Klas (AWS SSO-token expired):**
- ⏸️ CloudWatch worker-loggar — verifiera `SyncPlatsbankenStreamJob: startad`-event
  efter första `*/10`-cron-tick (~07:00 UTC)
- ⏸️ Admin-trigger `POST /api/v1/admin/job-ads/sync/platsbanken` mot dev —
  denna gång ska den landa items (vs P8b:s 504 ALB-timeout som CTO accepterade
  eftersom snapshot-flödet nu primärt kör som Hangfire-cron, inte synkron HTTP)
- ⏸️ DB-persist-verifiering — `job_ads`-tabellen får rader med External-ref +
  sanerad RawPayload
- ⏸️ 30d-retention-test — sätt tillfälligt `RawPayloadRetentionDays=0` →
  trigger `purge-stale-raw-payloads` via Hangfire-dashboard → bekräfta nollning

## Lärdomar

- **CTO-rond INNAN kod respekterar Klas-direktiv "minimera STOPP":**
  6 multi-approach-frågor avgjorda i en CTO-rond → CC kunde köra non-stop
  utan Klas-STOPP. Architect-position först → CTO-decision → implementation.
  Single-Klas-touch räckte (right-to-erasure-fråga som han direkt-tilldelade CTO).
- **Per-item-Mediator-loop ersätter bulk-handler graciöst:** P8b:s
  `SyncPlatsbankenSnapshotCommandHandler` reduceras till 14-rad shim utan att
  tappa funktionalitet. Admin-trigger + nattjobb delar samma kodväg.
- **`IDbExceptionInspector`-pattern är åter-användbart:** andra upsert-handlers
  (kommande BYOK-imports? AI-result-cache?) kan konsumera samma port.
- **TD-73 buntning av audit-wire + right-to-erasure är optimering av Klas-tid:**
  istället för två separata ADR-amendment-passes blir det en (ADR 0022 +
  ADR 0032) som adresserar båda samtidigt. Architects förslag, CTO-godkänd.

## Pending operativt för Klas

- **AWS SSO-refresh** krävs för CloudWatch-läsning och smoke-test mot admin-endpoint
- **JobTech-API-key registrering** — apirequest.jobtechdev.se nedlagd; JobStream
  v2 är öppen-API i alla fall (web-curl-verifierat 2026-05-13 i P8b)
- **Frontend-deploy till Vercel** (`dev.jobbpilot.se/` → 404 idag, bara `/api/*` svarar)
- **BUILD.md §9.1 sync mot ADR 0032 §3** — Klas-instruktion krävs

## Nästa session — F2-P9 (?) eller TD-73 prod-gating-batch

Per ADR 0032 §9 är P8c sista P8-batchen. Möjliga nästa steg:

1. **TD-73 prod-gating-batch:** audit-wire-α + right-to-erasure-admin-endpoint
   buntade i en ADR-amendment-pass (ADR 0022 payload-aktivering + ADR 0032 §8
   audit-mekanism-spec). Krävs INNAN v0.2-prod-tag.
2. **F2-P9 search/filter-yta** (TD-70) — GET `/api/v1/job-ads` med
   `?ssyk=...&region=...&q=...` per JobTech v2 `occupation-concept-id` +
   `location-concept-id`.
3. **Frontend-deploy** till Vercel + JobAd-katalog UI.

Klas-val.

## Tidsuppskattning

~5h CC-tid effektivt (28 filer, 1868 insertions, 4 agent-ronder + CTO-rond,
3 disciplinmiss-fixar). Reviewers-INLINE-discipline kostade ~1h extra men
sparade post-hoc fix-batch.

**HEAD vid session-end:** `81dfab6` + tag `v0.2.3-dev` deployerad live på dev.
