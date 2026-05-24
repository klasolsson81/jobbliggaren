# Code-review — STEG 6 backfill (Approach A)

**Datum:** 2026-05-24
**Agent:** code-reviewer (agentId `ad2f8482309802b7b`)
**Status:** Changes requested → in-block-fix applied. Re-review-grön efter M-1 + M-2 fix.

---

## Status

- **0 Blockers**
- **2 Major (in-block-fixade i samma commit-batch):**
  - **M-1:** `counts.Updated++` räknade fel — Skipped/Added klumpades in i Updated. **FIXAD:** switch på `UpsertOutcome` med separata räknare `Added/Updated/SkippedByHandler` + ny unit-test `RunAsync_HandlerSkipped_IncrementsSkippedByHandlerNotUpdated`.
  - **M-2:** EventId-kollision 5701-5703 mot `RetainPlatsbankenJobAdsJob`. **FIXAD:** bytt till lediga block 6001-6005.
- **6 Minor** — varierande åtgärd:
  - Min-1 (concurrency-skydd är operativ disciplin, inte mekanik) → TD-rec (Minor-1+3 från security konvergerar)
  - Min-2 (Refit double-handling av 404) → acceptabelt som-är (defense-in-depth)
  - Min-3 (BackfillJobAdSsykWorker dead-code-risk) → Klas-not
  - Min-4 (ProgressLogEvery-cosmetics) → acceptabelt
  - Min-5 (integration-test för STORED column-re-evaluation) → TD post-MVP
  - Min-6 (BackfillCounts mutable) → XML-doc tillagd "Mutable by design"

## Tester (post-fix)

- Application 585/585 PASS (+7 nya inkl. ny Skipped-test)
- Domain 404/404
- Architecture 78/78
- Migrate 6/6

## Praise (oförändrat)

- Clean Arch intakt
- CQRS-disciplin korrekt (Mediator-pipeline återanvänds)
- Egen DI-scope per item (ADR 0032 §5-paritet)
- `IDateTimeProvider`-injektion
- `CancellationToken` propageras
- `AsNoTracking().AsAsyncEnumerable()` streamar utan materialisering
- Idempotent restart-design via NULL-filter
- `OrderBy(ExternalId)` ger Postgres+InMemory-paritet (JobAdId-VO saknar IComparable)
- Architecture-test allowlist-uppdaterad
- Sanitizer återanvänd i refetch-path
- Hangfire-client storage-only i Api (ADR 0023-paritet)

## Konflikt CTO vs Klas

Code-reviewer tar inte ställning till Approach A vs C-strategin (Klas-territorium per §9.6). Approach A:s mekanik granskad och godkänd.
