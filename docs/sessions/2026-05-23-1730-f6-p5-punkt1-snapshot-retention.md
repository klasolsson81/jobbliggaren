---
session: F6 P5 Punkt 1 — snapshot-retention defense-in-depth (ADR 0032+0062-amend)
datum: 2026-05-23
slug: f6-p5-punkt1-snapshot-retention
status: LEVERERAD + DEPLOY-TRIGGAD v0.2.57-dev (deploy-run 26336452793 queued); LIVE-verifiering pending
commits:
  - "b3b5e31 feat(job-ads): F6 P5 Punkt 1 — snapshot-retention defense-in-depth"
deploys:
  - "v0.2.57-dev — tag-push 2026-05-23 Klas-GO, run 26336452793 queued (build-CI grön run 26336390555 2m55s)"
adrs:
  - "ADR 0032-amendment 2026-05-23 — snapshot-retention (Beslut 1.A–1.H inkl. post-archive circuit-breaker)"
  - "ADR 0062-amendment 2026-05-23 — ApplyCriteria Status=Active SPOT-filter (cross-ref till ADR 0032-amend)"
---

# F6 P5 Punkt 1 — snapshot-retention defense-in-depth

**HEAD vid session-end:** `b3b5e31` (origin/main). **Deploy-trigger:** `v0.2.57-dev` tag pushad 17:23 svensk tid → `deploy-dev` run `26336452793` queued. **build-CI:** grön (run `26336390555`, 2m55s, success).

## Mål

Klas-observation 2026-05-23: `/jobb`-korpus 56 660 rader vs Platsbankens ~46 800 aktiva annonser → `sync-platsbanken-snapshot` rensar inte utgångna jobb (endast UPSERT, ingen archive-pass). Punkt 1 av 5-punkts-ordningen (bekräftad 2026-05-23): leverera snapshot-retention som löser korpus-divergensen, blir förutsättning för meningsfulla räkningar i punkt 2/3/4.

## Beslutspann (agent-spårning)

| Roll | AgentId | Roll i besluten |
|---|---|---|
| senior-cto-advisor | `a8e277380b446bb02` | Q1=(c) defense-in-depth, Q2=(i) återanvänd `Archived`, Q3=(B) bulk + aggregerad audit, Q4=(W) SPOT-filter, Q5 trösklar (30k abs + 80% × max_7d rel + N=3), Q6 amendments |
| dotnet-architect | `a10f8271fe298246c` | Port-design (paritet IUserDataKeyStore TD-13 C2), `SnapshotOutcomeRecorder` single-write-pattern, raw SQL `ON CONFLICT` via `unnest(@seen_ids::text[])`, cron-schema |
| senior-cto-advisor (tilläggsrond H1) | `acfe2963371fde555` | Post-archive circuit-breaker in-block (CTO 0.25 framför security-auditor:s 0.20 — noll marginal mot förväntad 18% första-körning vore falsk-positiv-känsligt) |
| code-reviewer | `a82b9f511ec54889b` | **GO** — 0 Block, 0 Major, 2 Minor accept (M1 audit-korrelering retention→snapshot via tidsfönster-join, M2 ExpireJob unit-test try/catch paritet PurgeStaleRawPayloadsJob) |
| security-auditor | `a419beb4d87e8a46e` | **GO med villkor** — H1 (post-archive circuit-breaker) + H2 (relative-floor-test) båda in-block-fixade i samma batch; 0 Critical/Block |
| adr-keeper | `a4e7227559affffdb` | ADR 0032-amendment + ADR 0062-amendment skrivna, TD-86-not 2026-05-23 (korpus-storlek-del löses indirekt; recall-gap-rotorsak orörd) |

## Vad som gjordes

### 1. Defense-in-depth-arkitektur (Beslut 1.A–1.H)

Fem lager:

1. **Trunkering** — `SnapshotOutcome.TruncatedAndExhausted=true` → skip miss-tracking (ADR 0032-amend 2026-05-16-konsekvens: trunkerad prefix ≠ källans frånvaro)
2. **Absolut floor** — `ParsedTotal < 30 000` → skip miss-tracking (snapshot uppenbart trasig)
3. **Relativ floor** — `ParsedTotal < max_observed_7d × 0.80` → skip miss-tracking (dramatisk regression mot 7d-baslinje)
4. **N=3 konsekutiva snapshot-misses** krävs i `job_ad_snapshot_misses`-tabellen innan retention arkiverar
5. **Post-archive circuit-breaker** — `RetainPlatsbankenJobAdsJob` gör pre-archive-COUNT via `IJobAdSnapshotMissTracker.CountActiveJobAdsAsync` + `CountArchiveCandidatesAsync`, beräknar `ratio = candidates / active`; vid `ratio > MaxArchivePctPerRun (0.25)`: skriver audit `ThresholdAborted=true, AbortReason="max-archive-pct-exceeded"` + kastar `DomainException("RetainPlatsbankenJobAds.MaxArchivePctExceeded")` för fail-loud → Hangfire-retry → CloudWatch alarm via metric filter `event_name=retention_aborted`

### 2. ApplyCriteria Status=Active SPOT-filter (ADR 0062-amend, Beslut 1.G)

`JobAdSearchQuery.ApplyCriteria` (Infrastructure, port `IJobAdSearchQuery` per ADR 0062) får `source.Where(j => j.Status == JobAdStatus.Active)` som **första** filter-steg. SPOT — tre konsumenter (`ListJobAdsQueryHandler`, `RunSavedSearchQueryHandler`, `ListRecentSearchesQueryHandler`) får filtret automatiskt via ADR 0039 Beslut 1. Klas-STOPP-flaggad i CTO-domen (UX-räkne-drop synlig samma deploy) — CTO Variant 1 (filter+retention samma release) accepterad för konsistent state över alla läs-ytor.

### 3. Bulk-arkivering via `ExecuteUpdateAsync` (CTO Q3=B)

Domain-event raisas EJ per item (bulk bypassar `JobAd.Archive()`). Verifierat 0 subscribers på `JobAdArchivedDomainEvent` (architect D8.a — `grep INotificationHandler|IDomainEventDispatcher` → 0 träffar). Aggregerad audit-rad `JobAdsRetentionCompleted` per pass (Reason: `"snapshot-miss"` eller `"expired"`) via `ISystemEventAuditor` är retention-vägens accountability-spår (GDPR Art. 30).

### 4. Hangfire-cron-schema (Beslut 1.F)

| ID | Schema (UTC) | Roll |
|---|---|---|
| `sync-platsbanken-snapshot` | `0 2` | Oförändrad + ny miss-tracking-uppdatering vid komplett snapshot |
| `retain-platsbanken-job-ads` | `15 3` | Snapshot-miss-retention (N=3 + circuit-breaker) |
| `expire-job-ads` | `45 3` | Defense-in-depth ExpiresAt-cron |

`DisableConcurrentExecution(300s)`-wrappers för båda nya jobben (paritet `SyncPlatsbankenSnapshotWorker`).

### 5. EF-migration `F6P5SnapshotMisses`

Ny tabell `job_ad_snapshot_misses (source, external_id, miss_count, first_missed_at, last_missed_at)`. Composite PK + partial-index `(source, miss_count) WHERE miss_count >= 1`. Idempotent up/down. Reversibel.

### 6. Test-täckning

| Suite | Antal | Status |
|---|---|---|
| Domain.UnitTests | 399 | grön |
| Application.UnitTests | 546 (+13 nya: 4 retention + 4 snapshot-floor + 5 retain-job inkl. boundary/abort/div-by-zero) | grön |
| Architecture.Tests | 78 (+5 nya `JobAdRetentionLayerTests`: konsumentlås, ISP-skydd, IAppDbContext-läckage-test) | grön |
| Worker.IntegrationTests / Api.IntegrationTests | — | Körs i CI (Docker ej igång lokalt) |

## Detours / lärdomar

### NSubstitute CreateJob-pattern kollision

Två iterationer fångade samma fel: `CreateJob`-helper som setter default `.Returns()` på shared tracker mock OVERRIDADE test-specifik konfiguration → 3 tester failade tyst med default-värden istället för testets uppsatta. Fix: separera `CreateJob` (bara konstruktion) från `StubTracker(active, candidates, archived)` (mock-state). Varje test äger sin egen state. Generell princip: helper-metoder ska inte ha sido-effekter på input-objekt.

### DomainException-konstruktor

Initial impl använde `new DomainException(DomainError.Validation("...", "..."))`. Faktisk signatur är `DomainException(string code, string message)` — `DomainError` är för `Result`-pattern. Fix-edit: byt till `DomainException("code", "message")`. Båda är Domain-typer men har distinkta roller (exception vs Result-error).

### Truncerad signatur-ändring på `IJobSource.FetchSnapshotAsync`

Lade till `SnapshotOutcomeRecorder` som första parameter (architect D2.B-variant). Bröt befintliga tester (`SyncPlatsbankenSnapshotJobTests` + `JobTechStreamResilienceTests`) — uppdaterade callers med ny signature. Architect motiverade single-write-pattern: caller skapar recorder, source skriver utfall innan `yield break`, caller läser efter `await foreach`. Explicit > implicit (Saltzer/Schroeder 1975). Fail-fast vid dubbel-`Record()` → `InvalidOperationException`.

## ADR-spår

- **ADR 0032-amendment 2026-05-23** (`docs/decisions/0032-jobtech-integration.md` rad 764–948). Beslut 1.A–1.H, avvisade Q1(a/b), Q2(ii/iii), Q3(A/C), Q4(Y/Z), cursor-tabell.
- **ADR 0062-amendment 2026-05-23** (`docs/decisions/0062-fts-hybrid-search-and-infrastructure-query-port.md` sist). Kort cross-ref till ADR 0032-amend Beslut 1.G för full motivering av Status=Active SPOT-filtret.
- **TD-86-not 2026-05-23** (`docs/tech-debt.md`): korpus-storlek-delen adresseras indirekt; recall-gap-rotorsak och common-term-perf är ortogonala mot korpus-storlek → TD-86 INTE stängd.

## Klas-STOPP-spår

- CTO ursprungs-rond flaggade Q4 (ApplyCriteria UX-räkne-drop) som Klas-STOPP. Klas valde Variant 1 implicit genom auto-mode (CTO rekommendation).
- CTO tilläggsrond H1 sa "Klas-STOPP behövs inte" — entydig in-block-fix per §9.6.
- Tag-push `v0.2.57-dev` — Klas-GO mottaget 17:23 → tag pushad.

## Pending för Klas (post-deploy)

1. Verifiera deploy-run `26336452793` slutförs grön (~12-15 min från queue).
2. Sanity-check: `curl -sI https://dev.jobbpilot.se/api/ready` → HTTP 200.
3. Korpus-baseline: `curl -s "https://dev.jobbpilot.se/api/v1/job-ads?pageSize=1" | jq .totalCount` — förväntat ~40k-46k samma deploy (Status=Active SPOT-filter; retention har inte arkiverat ännu).
4. Första retain-körning 03:15 UTC (kommande natt). Inga rader förväntas arkiveras första gången (miss_count=0 på alla rader; ticker först efter snapshot-cron 02:00 nästa natt).
5. ~72h post-deploy: korpus-konvergens via `SELECT count(*), reason FROM audit_log WHERE event_type='System.JobAdsRetentionCompleted' GROUP BY reason;` — förväntat rows `snapshot-miss` + `expired` med `ArchivedCount > 0`.

## Nästa session

**Punkt 2 — Jobbkort Spara + Har-ansökt** (large, backend+frontend). F6 P4b `SavedJobAd`-aggregat. "Har ansökt" via `CreateApplicationFromJobAdCommand` med JobAd-snapshot. Kräver ADR 0053-amend (lyfta deferral 2026-05-19) + ev. ny ADR för snapshot-vid-skapande vs join-vid-läs. CTO-rond obligatorisk vid sessionsstart.
