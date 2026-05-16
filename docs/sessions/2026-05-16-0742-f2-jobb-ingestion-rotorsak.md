---
session: F2 jobb-ingestion rotorsak + fix (Commit 1+2)
datum: 2026-05-16
slug: f2-jobb-ingestion-rotorsak
status: Commit 1+2 pushed (70a7c54). Commit 3 + backfill + cadence + ADR 0032-amendment PENDING Klas-GO.
commits:
  - 347b238  # fix(jobads): rotorsak F2 jobb-ingestion — snapshot child-scope + IAsyncEnumerable-streaming
  - 70a7c54  # fix(jobads): rate-limiter bounded queue — stream/snapshot serialiseras vid 02:00
---

# F2 jobb-ingestion — rotorsak + fix

## Mål

Utred varför `/jobb` visar ~5k av Platsbankens ~47k aktiva annonser. Fastställ
rotorsak med evidens (ej hypotes), åtgärda så korpusen blir komplett.

## Discovery + rotorsak (evidens-backad)

Ingestion-kedjan kartlagd. JobTech-fakta web-verifierade 2026-05-16
(`raw.githubusercontent.com/Jobtechdev-content/Jobstream-content/develop/GettingStartedJobStreamSE.md`):
`/snapshot` = alla aktiva annonser, ~300 MB, ingen paginering, ingen separat
backfill-endpoint i JobStream. Stream */10 ser bara 15-min-deltan.

**CloudWatch `/aws/ecs/jobbpilot-dev/worker` (4 dygn, efter Klas `aws sso login`):**

- `SyncPlatsbankenSnapshotJob: startad` (5401): **60 ggr**
- `SyncPlatsbankenSnapshotJob: klart` (5402): **0 ggr** — aldrig slutfört sedan P8c-deploy 2026-05-13
- Fel: uncaught `DbUpdateException → Npgsql 23505 duplicate key
  "ix_job_ads_external_source_external_id"` → `Hangfire.AutomaticRetryAttribute` (fail)
- Stream-jobbet hanterar samma kollision graciöst (info 5101) — små batchar

**Rotorsak:** hela ~47k-snapshot-loopen kör i EN DI-scope → ett scoped
`IAppDbContext` vars change-tracker ackumulerar. `UnitOfWorkBehavior` kör en
SaveChanges efter varje `mediator.Send` över hela ackumulerade grafen. Snapshot
⊇ det stream redan infogat (tusentals dubbletter) → 23505 som
`UpsertExternalJobAdCommandHandler`s per-command-catch (ADR 0032 §5, antar
single-command-scope) ej kan isolera vid batch-skala → hela runnet abortar,
deterministiskt. Korpus fastnar på stream-ackumulerade ~5k.

## Beslut (senior-cto-advisor + dotnet-architect inline)

- **Variant B** (per-item child DI-scope) — återställer §5:s scope-antagande
  utan att röra dedup-mekaniken. Avvisade: A (för-load-partitionering, ny
  TOCTOU + ADR-amendment), C (ChangeTracker.Clear, port-läcka + magic chunk),
  D (raw upsert, §3.6-brott, redan avvisad ADR 0032 §11).
- **Ingen full ADR-amendment** för §5 — bara clarification-rad (granskningstrail).
- Sekundära defekter (a) OOM-streaming, (b) rate-limiter-kollision: **in-scope**
  (§9.6 — samma fas, samma kodyta, genuin lucka). (c) admin-async + Commit 3.
- Cadence: CTO-rek behåll */10 + 0 2 oförändrade; Klas avgör (eskalerad).

## Levererat (Commit 1+2, pushed)

**Commit 1 `347b238`** — rotorsaks-fix:
- `SyncPlatsbankenSnapshotJob`: `IServiceScopeFactory` ersätter `IMediator`;
  child-scope (`CreateAsyncScope`) per item.
- `FetchSnapshotAsync` → `IAsyncEnumerable` hela kedjan (`IJobSource`,
  `IJobTechStreamClient`, `PlatsbankenJobSource`, `JobTechStreamClient` via
  `DeserializeAsyncEnumerable`). `JobAdSnapshot`-record borttagen.
- Explicit `Microsoft.Extensions.DependencyInjection.Abstractions` i Application
  (ren DI-abstraktion, Clean Arch OK, tidigare transitiv) — §9.2-flaggad.
- Regressionstest `RunAsync_WhenSnapshotContainsDuplicates_IsolatesPerItemScope_AndCompletes`
  (ScopesCreated == N + no-throw + counts). test-writer: 7 + 4 testfall + StubJobSource + resilience-test.

**Commit 2 `70a7c54`** — rate-limiter: `_streamRateLimiter` QueueLimit 0→2 +
`OldestFirst` så stream/snapshot serialiserar mot 1/min vid 02:00 istället för
hård rejection. Förlegad kommentar uppdaterad (code-reviewer Min-1).

933 tester gröna, build 0 warn/0 err. code-reviewer: 0 Blockers/Majors,
2 Minors åtgärdade in-block.

## PENDING — Klas-GO krävs

1. **ADR 0032 §5-clarification + §9-amendment** — förslag visat i chatt
   (Förbud: inga ADR-0032-amendments utan Klas-GO + visa förslag först).
2. **Commit 3** — admin-endpoint synkron→async via ny port
   `IBackgroundJobLauncher` (Application) + `HangfireBackgroundJobLauncher`
   (Infrastructure) + `SyncPlatsbankenSnapshotWorker` (`DisableConcurrentExecution`
   1h) + endpoint 200→202. Gated på ADR §9-amendment-GO.
3. **Initial-backfill** — `RecurringJob.TriggerJob("sync-platsbanken-snapshot")`
   efter fix-deploy (Klas-STOPP: deploy).
4. **Cadence-beslut** — CTO-rek behåll oförändrat; verifiera empiriskt efter
   första lyckade snapshot-run att korpus → ~47k.

## Nästa session / fortsättning

Efter Klas-GO: applicera ADR 0032-clarification + §9-amendment (verbatim),
implementera Commit 3 (test-writer + code-reviewer inline), deploy + backfill-
trigger (Klas), verifiera dev-korpus → ~47k storleksordning, cadence-beslut.
Därefter åter till övrigt Fas 2-arbete.
