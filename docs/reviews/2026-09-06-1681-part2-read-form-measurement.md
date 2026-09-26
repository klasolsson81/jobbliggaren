# 2026-09-06 — #1681 part 2: which shape the materialised read takes

**Session:** CC, worktree `c:/tmp/jbl-1681c`, branch `feat/1681-read-path`.
**Status:** measurement, taken to let `senior-cto-advisor` BIND a form on numbers rather than on a
prediction. It settles nothing on its own.

## Why a measurement was owed at all

Part 1's protocol (`docs/reviews/2026-09-06-1681-membership-measurement.md`) closes with an explicit
hand-off: *"What this does NOT settle — the SHAPE part 2 uses. Both forms were measured because the
crossover means part 2 has a real choice… The bound is derived against the per-criterion `= ANY`
shape ADR 0139 names, which is the conservative side of the crossover; a part-2 shape that is cheaper
than that does not invalidate it, and one that is more expensive would have to re-derive."*

So the question part 2 opens is not "does materialisation work" — part 1 measured that — but **which
of two statement shapes the read path takes**, given that they cross:

- **Form 1 — per-criterion `= ANY(member array)`.** The twin handler's own shape, and the one ADR
  0139 names. One statement per criterion; its cost grows with the criterion's ad set.
- **Form 2 — batched, keyed by `criterion_id`.** One statement for the whole surface, driven by
  `job_ads` and therefore flat in the member count.

Part 1 measured the crossover on the MEMBER axis. Part 2's surfaces differ on two axes part 1 did not
vary: **N** (the detail page reads ONE criterion, the list reads up to `MaxPerUser` = 20) and the
**ad-set size** per criterion, which is what the read actually carries.

## Instrument

Throwaway `postgres:18.4` on port 55434 — never the shared dev DB (§6.5), and never the
Testcontainers suite, which is an oracle instrument and not a timing one. Schema mirrors the
DELIVERED shapes: `job_ads` with `ix_job_ads_organization_number` and
`(status, published_at, id)`; `company_watch_criterion_members` with its real PK
`(criterion_id, organization_number)` and nothing else, exactly as part 1's migration created it.

Row counts are dev's own, read read-only the same day (a read-only measurement needs no GO,
Klas-direktiv 2026-08-20):

| Table | Rows | Of which |
|---|---:|---|
| `job_ads` | 106 071 | 41 597 `Active`, over 7 108 distinct `Active` org.nr |
| `company_register` | 1 066 938 | 743 654 `Active` |

Two cohorts of 20 criteria (`CompanyWatchCriterion.MaxPerUser`), **both pinned at the member bound**
(`CompanyWatchCriterionMember.MaxPerCriterion` = 1 000) so the member axis is held constant and only
the ad axis varies. Member slices are disjoint across criteria — the worst case for Form 2, which
gets no deduplication to exploit.

| Cohort | Members / criterion | Active ads / criterion |
|---|---:|---:|
| `realistic` | 1 000 | **202** |
| `atbound` | 1 000 | **2 004** |

`p95` over 40 runs per point (20 for the two slowest), via a `plpgsql` timing loop, every point
warmed first. **Never a warm singleton** — `docs/reviews/2026-09-04-1559-perf-test-writer.md:43`
forbids a verdict on single observations.

## Where the cohorts come from — the product distribution, measured on the real register

A cohort invented to make a form look good measures nothing, so the ad-set sizes were read off the
real register first (read-only, dev, `status = 'Active'`, bound-legal criteria only — those matching
at most `MaxPerCriterion` companies, since a broader one is refused by the breadth gate and has no
ad set at all):

| Criterion width | Population | Median | p95 | Max | Over `MaxSetSize` (2 000) |
|---|---:|---:|---:|---:|---:|
| 1 SNI × 1 kommun | 101 097 cells | 0 | **2** | 589 | **0** |
| 1 SNI × all 290 kommuner | 627 codes | 6 | **199** | 5 519 | **2 (0,3 %)** |
| *Adversarial*: the 1 000 register companies with the most active ads | — | — | — | **28 971** | — |

Three things follow, and all three are load-bearing below.

1. The `realistic` cohort's 202 is the measured p95 of the broadest ordinary shape, not a guess.
2. **`CriterionMatchingAds.SetTooLarge` stays live after materialisation.** The adversarial 1 000-member
   set carries 28 971 active ads — 14x `MaxSetSize` and ~3x `CriterionAdMagnitudeDto.Ceiling` — so
   both the refusal and the saturating "10 000+" arm remain reachable states rather than dead code.
   The breadth gate bounds COMPANIES; it does not bound ads, and nothing in part 1 claimed it did.
3. A cohort in which all 20 criteria sit at the refusal bound is the arithmetic worst case, and the
   distribution says it is not an ordinary one: 2 criteria in 627 exceed 2 000, and 0 in 101 097.

## Result 1 — the two forms, p95 in ms

Baseline: `ListCompanyWatchesQueryHandler`'s own read — a bounded `= ANY(org.nr array)` `GROUP BY`
over `job_ads` for 20 followed companies — **0,166 ms p95** on this fixture. The cost class every
number below is judged against.

**The ad COUNT** (capped at `CriterionAdMagnitudeDto.Ceiling`):

| Surface | Cohort | Form 1 | Form 2 |
|---|---|---:|---:|
| Detail page (N=1) | realistic | **0,448** | 8,834 |
| Detail page (N=1) | at-bound | **1,596** | 6,979 |
| List (N=20) | realistic | **9,427** | 15,537 |
| List (N=20) | at-bound | 25,002 | **15,773** |

**The ad-id SET** (`LIMIT MaxSetSize + 1`, the structural refusal):

| Surface | Cohort | Form 1 | Form 2 |
|---|---|---:|---:|
| Detail page (N=1) | realistic | **0,825** | 9,093 |
| Detail page (N=1) | at-bound | **1,893** | 8,931 |
| List (N=20) | realistic | **11,999** | 16,576 |
| List (N=20) | at-bound | **27,463** | 41,657 |

**Form 2 is flat on the count and is NOT flat on the id set, and that asymmetry is the finding.**
The count's `GROUP BY` is driven by `job_ads` and costs the same at 202 ads as at 2 004 (15,5 → 15,8).
The id set cannot be: cutting per-criterion inside one statement needs
`row_number() OVER (PARTITION BY criterion_id ORDER BY published_at DESC, id)`, and that window
sorts the whole join result — 20 × 2 004 rows at the bound — which is why it goes 16,6 → 41,7 and
ends up the most expensive point in the whole table.

**Form 1 is dominated only once**, at N=20 counts in the at-bound cohort (25,0 vs 15,8). It wins the
other seven points, and at N=1 it wins by 5–20x, because `= ANY(member array)` touches only the
criterion's own ads while Form 2 pays the `job_ads` scan whatever N is.

## Result 2 — the grading fan-in, as a FLOOR

The personal ("matchande annonser") number cannot be answered by either statement: the grade
predicate is `GradeRankExpression` and is reached only through
`IPerUserJobAdSearchQuery.FilterToMatchingAsync`, which `CriterionMatchingAdSetResolver`'s docblock
keeps to a single home. So the ad ids cross into Application, get graded, and the matching subset
comes back — for the list, that is the union over up to 20 criteria.

| Ids in one `FilterToMatchingAsync` call | p95 |
|---:|---:|
| 2 000 (detail page's existing worst case, unchanged) | 8,381 |
| 4 040 (list, `realistic` cohort: 20 × 202) | 4,544 |
| 40 000 (list, arithmetic worst case: 20 × 2 000) | 23,301 |

⚠ **These are FLOORS and the report says so rather than letting a reader take them for the cost**
(repo precedent: #824). The fixture reproduces the `id = ANY(...)` fan-in and the status gate; it does
NOT reproduce `GradeRankExpression`, which needs the real generated shadow columns and concept
arrays. The grade predicate's own cost is **unmeasured here**.

⚠ **The series is not monotone** — 2 000 ids cost more than 4 040, reproducibly, across warmed
re-runs. Reported as measured rather than smoothed. It is not a rounding artefact and it means the
fan-in cost is not linear in the id count, so a bound derived by scaling one of these points would be
wrong.

Note the worst case is smaller than 40 000 in practice for a structural reason, not an optimistic
one: a criterion whose ad set exceeds `MaxSetSize` is **refused**, contributes zero ids, and is
rendered "för bred". The 40 000 row is the arithmetic ceiling of criteria sitting just under the
refusal.

## Result 3 — the register scan is gone from THIS read path too

#1681's fourth required measurement, restated against part 2's own statements rather than inherited
from part 1's. `EXPLAIN (ANALYZE, BUFFERS)` of Form 1's id-set statement:

```
Limit (actual time=1.505..1.523 rows=219 loops=1)
  Buffers: shared hit=43
  InitPlan 1
    ->  Index Only Scan using pk_company_watch_criterion_members  (rows=1000)
          Index Cond: (criterion_id = '…'::uuid)
          Heap Fetches: 0
  ->  Sort  (Sort Method: quicksort  Memory: 40kB)
        ->  Bitmap Heap Scan on job_ads j
              Recheck Cond: (organization_number = ANY ((InitPlan 1).col1))
              Filter: (status = 'Active')
              ->  Bitmap Index Scan on ix_job_ads_organization_number
```

**There is no `company_register` node in the plan at all**, and the plan is the one the bound was
derived against: Index Only Scan on the member PK with `Heap Fetches: 0`, feeding a Bitmap Index Scan
on `job_ads`. 43 buffers warm for the id set, 37 for the count — against the **6 556 ms** the live
register join cost for the ad count of the widest bound-legal criterion.

That last comparison is the point of the whole issue, stated as something a plan can show rather than
something the design asserts.

## Result 4 — the composed surface against ADR 0045

`/oversikt`'s budget is **300 ms p95** (ADR 0045 class (a)).

⚠ **Two corrections, made after `senior-cto-advisor` found both in review (2026-09-06). They are
composition defects in this section, not measurement defects, and the first version of this table
carried them.**

**(A) The at-bound row cannot charge the id set AND zero the fan-in — those are two different
states.** What zeroes the fan-in is `CriterionMatchingAdSetResolver`'s guard order: magnitude →
`> MaxSetSize` → `SetTooLarge`, at which point `ListActiveAdIdsAsync` **is never called**. At 2 004
ads every criterion in that cohort is refused, so its id column is 0 too. The id-set measurement is
still meaningful — a criterion at exactly `MaxSetSize` passes the gate and the port then reads
`MaxSetSize + 1` rows, which is that statement's reachable worst case — but it belongs to a
different row than the one that zeroes grading. Both readings are therefore given below.

**(B) A sum of an upper bound and a floor is neither.** The counts and ids columns are measured p95s
and compose to an upper bound (a sum of p95s is not itself a p95 of the composed read, as in part 1).
The fan-in column is a **FLOOR** (Result 2: `GradeRankExpression` is not reproduced). So the Block
column below is labelled per what it actually is, and the budget claim is split accordingly.

**Statement halves only — fully measured, upper bound:**

| Form | Cohort | Counts | Ids | Statements | Share of 300 ms |
|---|---|---:|---:|---:|---:|
| Form 1 | realistic | 9,4 | 12,0 | **21,4** | **7,1 %** |
| Form 1 | at-bound, criteria refused (ids not read) | 25,0 | — | **25,0** | **8,3 %** |
| Form 1 | at-bound, criteria at the gate (ids read) | 25,0 | 27,5 | **52,5** | **17,5 %** |
| Form 2 | realistic | 15,5 | 16,6 | 32,1 | 10,7 % |
| Form 2 | at-bound, criteria refused (ids not read) | 15,8 | — | 15,8 | 5,3 % |
| Form 2 | at-bound, criteria at the gate (ids read) | 15,8 | 41,7 | 57,5 | 19,2 % |

**Adding grading** (realistic cohort, the only row where ids are both read and graded): + the 4 040-id
fan-in **floor** of 4,5 ms → Form 1 ≈ 25,9 ms, Form 2 ≈ 36,6 ms. **Neither figure is a bound**, because
its third term is a floor.

**What this establishes and what it does not.** For the **statement halves** both forms fit
comfortably, and that is measured. For the **composed surface including grading** nothing here
establishes a budget verdict at all — the dominant term is unmeasured, and Result 2's series is
non-monotone, so no extrapolation is available either. That gap is the reason the grading fan-in has
to be measured against the real schema before `agents-done` rather than argued from this table.

## What this measurement does NOT settle

- **Which form is chosen.** That is `senior-cto-advisor`'s, on the numbers above (§9.2: multi-approach
  choices; the driving session gives no recommendation of its own).
- **Whether the bound needs re-deriving.** Part 1's rule is that a cheaper shape than the
  per-criterion `= ANY` inherits the derivation and a dearer one re-derives it. The table above shows
  the two forms cross, so the answer is per-point rather than global — it follows from whichever form
  is bound, and cannot be stated before that.
- **The grade predicate's cost.** Result 2 is a floor (see its warnings). If the chosen form makes the
  fan-in load-bearing, it needs measuring against the real schema before `agents-done`.

## Reproducing

Not committed as a script, for the reason
`docs/reviews/2026-09-06-1681-plan-probe-measurement.md` gives: a committed copy decays against the
real schema. Seed a `postgres:18.4` container at the row counts in the Instrument section
(regenerate them from dev first — they are what makes the numbers transferable), build the two
cohorts at the member bound with disjoint slices, install a `p95(double precision[])` helper and the
four timing loops (one per form per question), warm every point, then read the four statement shapes
in Result 1. The register-distribution figures are plain read-only queries against `company_register`
and `job_ads` and need no fixture at all.
