# 2026-09-06/07 — #1681 part 2: the grading fan-in, and the composition that was proposed to replace it

**Status:** measurement. Two passes, taken because `senior-cto-advisor` made the first a precondition
for `agents-done` and then refused to bind its own proposed alternative unmeasured.
**Tracked deliberately** (`git add -f`) — production docblocks and ADR 0139 cite it.

## Why these exist

Part 2 puts per-user GRADING of criterion ad sets on a list route for the first time, at up to 20x
the only fan-in size ever measured in production shape. The figure that existed
(`2026-09-06-1681-part2-read-form-measurement.md`, Result 2) was explicitly a **FLOOR** —
`GradeRankExpression` was not reproduced — and its series was non-monotone, so no extrapolation was
available. The CTO's wording: *"A bound on the cheap half is not a bound."*

## Instrument (both passes)

The **real** `IPerUserJobAdSearchQuery` resolved from the **real** `AddPersistence` DI graph, against
a throwaway Postgres whose schema was created by **real EF migrations at HEAD**, seeded to dev's row
counts and dev's own per-ad facet values (copied read-only; no free text, no `organization_number`).
EF's emitted SQL captured verbatim under command logging — no statement hand-written on either side.
p95 nearest-rank over **40 runs**, 5 warm-ups, **reverse-order control pass**, three profile shapes.

⚠ **The machine ran the identical fixture ~1.8x faster on 09-07 than on 09-06** (identical `matched`
counts reproduced exactly, so the fixture content is proven the same). **Absolute figures are
therefore not comparable across the two passes; ratios taken within one process are.** The second
pass measured both compositions side by side for exactly this reason.

⚠ **Fixture row width turned out to be decisive.** The part-2 statement probe was **25x narrower per
row** than dev (76.6 B vs 1 898 B). The second pass therefore ran two fixtures: a narrow reproduction
and a **width-faithful** one at 217 MB heap against dev's 202 MB. Where they disagree, the wide one
is the one matching dev's storage regime.

## Pass 1 — the fan-in as delivered

Grading p95 (ms), `id = ANY(<union>)` + status gate + `GradeRankExpression`:

| ids | realistic | rich | ceiling | floor (no grade predicate) |
|---:|---:|---:|---:|---:|
| 2 000 | 16.3–25.9 | 19.1–23.7 | 25.9–29.9 | 12.5–14.7 |
| 4 040 | 20.9–25.9 | 24.9–26.5 | 42.3–47.9 | 15.0–17.7 |
| 40 000 | 159.2–187.6 | 225.1–264.2 | 300.1–329.0 | 71.0–78.0 |

Profiles: **realistic** 5 SSYK groups / 2 län / 3 kommuner / 2 employment types (matched 5.5–6.1 %);
**rich** 50 / 21 / 100 / 8 (matched 57 %); **ceiling** all 391 SSYK / all län / all kommuner
(matched 98 %). Dev's own `job_seekers` carry no usable distribution (139 rows, all E2E artefacts),
so "realistic" is a **stated judgement, not a measured population** — said rather than dressed up.

**Why the old figure was a floor, structurally.** The floor pass plans as an **Index Only Scan** at
every size and never touches the heap. Adding the grade predicate forces a heap fetch for five facet
columns and **flips the access path**. The floor was not a cheaper version of the same query.

Composed against ADR 0045 class (a), **300 ms** (sum of p95s = UPPER BOUND, never a p95):

| State | counts | ids | grading | Σ | share |
|---|---:|---:|---:|---:|---:|
| realistic list (20 × 202 ads) | 9.4 | 12.0 | 25.9 | **47.3** | **15.8 %** |
| at-bound, every criterion refused | 25.0 | — | — | 25.0 | 8.3 % |
| ceiling (20 × ~2 000), realistic profile | 25.0 | 27.5 | 187.6 | 240.1 | 80 % |
| ceiling, **rich** profile | 25.0 | 27.5 | 264.2 | **316.7** | **105.6 %** |
| ceiling, **ceiling** profile | 25.0 | 27.5 | 329.0 | **381.5** | **127.2 %** |

⚠ **Every Σ understates**: the statement halves are floors w.r.t. row width (see above), and nothing
else in the handler is counted (criteria load, pipeline, DTO mapping, serialisation).

**Surface correction.** This route is `GET /me/company-watch-criteria`, consumed by the three
smart-watch pages — **not by `/oversikt`, which calls the org.nr twin `getCompanyWatches()`**
(verified in source). The budget CLASS and the `MeListRead` policy are unchanged, so every figure
above stands; earlier prose in this issue's reports that named `/oversikt` as this route's consumer
was wrong about the page. Part 3 is what will put these numbers on `/oversikt`.

## Pass 2 — the CTO's fourth composition: grade the corpus once, intersect in memory

**Verdict: mixed, and it loses where it matters.**

p95 ms, realistic profile, baseline (varies with n) → candidate (constant):

| union ids | narrow fixture | width-faithful fixture |
|---:|---|---|
| 200 | 4.5 → 19.0 (4.2x worse) | 3.7 → 48.3 (13.1x worse) |
| **600** (the common case) | **5.9 → 19.0 (3.2x worse)** | **15.3 → 48.3 (3.2x worse)** |
| 2 000 | 9.1 → 19.0 (2.1x) | 19.2 → 48.3 (2.5x) |
| 8 000 | 20.2 → 19.0 (break-even) | 48.1 → 48.3 (break-even) |
| 40 000 | 84.5 → 19.0 (4.4x better) | 254.5 → 48.3 (5.3x better) |

Break-even ≈ 7 500–8 000 ids (realistic), 8 000–15 800 (rich), 19 000–20 000 (ceiling) — reaching it
needs 4+ criteria all pinned at the 2 000-ad cap. **The detail page, which has no problem today,
regresses hardest** (2.5x realistic, 4.4x rich, 6.3x ceiling) precisely because its N is capped small.

**The crossing, and the half nobody had counted:**

| profile | ids returned | share of Active | raw Guid bytes | **measured managed alloc/call** |
|---|---:|---:|---:|---:|
| realistic | 2 535 | 6.1 % | 39.6 KiB | 1.02 MB |
| rich | 23 932 | 57.5 % | 374 KiB | 8.50 MB |
| ceiling | 40 845 | 98.2 % | 638 KiB | **15.47 MB** |

**Its own premise does not survive.** The composition was proposed because removing the
40 000-element array removes the bad row estimate and the plan flip. On the **width-faithful** fixture
the plan flips anyway — `Parallel Seq Scan` for the *narrowest* profile, `Parallel Bitmap Heap Scan`
for the other two — and the estimate is not fixed but made **constant**: ~415 rows against actuals of
2 535 / 23 932 / 40 845, i.e. **6x / 57x / 97x** under-estimate.

**Neither composition is the stable one.** The baseline shows four plan shapes on the wide fixture
(pk bitmap → status Index Scan → Bitmap Heap Scan → Gather + Parallel Bitmap Heap Scan), estimates
1/2/8/158 against actuals up to 39 277 — **249x**.

## Non-monotonicity and outliers, as measured

- narrow/ceiling: n=200 → 4.065 ms, n=400 → 16.661 ms — a 2x size increase costs 4.1x, because the
  plan switches from the pk bitmap to the status Index Scan, which walks the whole Active index
  however few ids were passed.
- narrow/realistic n=12 000 p95 35.475 **>** n=16 000 p95 33.528 (inverted).
- wide/realistic n=1 000 p95 19.722 **>** n=2 000 p95 19.207 (inverted).
- wide/rich n=600: one 995.944 ms sample in 40, median 10.966. Reported, not smoothed.
- Pass 1's floor series was non-monotone (2 000 cost more than 4 040); pass 2's own series is
  monotone apart from the inversions above. Reverse-order control reproduces every ordering.

## What these measurements decided

1. **The fan-in is a COST, not a floor**, for the call as production issues it — with three residuals
   that all push dearer: the related-SSYK arm is unmeasured (inert today), id sets are uniform random
   where real criterion sets are clustered, and production hardware/`shared_buffers` are unmeasured
   (the 40 000 point touches ≈ 144 MB against a 128 MB `shared_buffers`).
2. **One composed state exceeds the budget** — the ad-set ceiling with a broad profile. Reachability:
   2 of 627 whole-industry criteria and 0 of 101 097 (kommun, SNI) cells exceed 2 000 ads, so it needs
   20 such criteria at once. Far out on the distribution; still a reachable state.
3. **The alternative composition loses** and its stability premise fails. Not adopted.
4. **Klas accepted the far-out state 2026-09-07**, on `senior-cto-advisor`'s recommendation and with
   its two conditions: an EXPLAIN pin on the grade query's plan (because a plan that flips inside the
   operating range makes an accepted number silently stop being true), and `security-auditor`'s
   bucket signature, which neither the acceptance nor the label fix discharges.

## Reproducing

Not committed as a script, for the reason the sibling reports give. Seed a `postgres:18.3` container
at dev's row counts **and dev's per-ad facet distribution**, build the schema with
`dotnet ef database update` rather than hand-written DDL, resolve the real port from `AddPersistence`,
and time it from the app process with a fresh DI scope per call. Pad `description` with
`STORAGE PLAIN` to reach dev's heap page count — row width changes the plan, and a narrow fixture
will tell you the opposite of the truth. ⚠ `ALTER SYSTEM RESET session_preload_libraries` when done
with `auto_explain`; setting it to `''` writes `'""'` and locks every new connection out with `58P01`.
