# 2026-09-08 — #1706: re-measuring the breadth gate's two halves

**Session:** CC, worktree `c:/tmp/jbl-1706`, branch `fix/1706-branschbevakning`, HEAD `f05944ba`.
**Status:** measurement. Taken because `CompanyWatchCriterionMember.MaxPerCriterion = 1000` refuses
the narrowest criterion the product can express, and #1706 requires any move to be **derived**.
**Not a proposal.** The form is `senior-cto-advisor`'s to bind and the minimisation half is
`security-auditor`'s to sign (CLAUDE.md §9.2).

**Supersedes nothing.** `docs/reviews/2026-09-06-1681-membership-measurement.md` is a dated
measurement of a finished event and is left untouched (AGENTS.md §5 `Comments:` — a dated historical
measurement is provenance). This report is the later reading beside it.

---

## 1. The corpus has not moved — no re-derive trigger fired on that axis

The 2026-09-06 derivation ends with *"Re-derive, do not nudge"* and names three triggers: `job_ads`
growing materially, `MaxPerUser` moving, or ADR 0045's budget changing. Read read-only from
`jobbliggaren-postgres-dev` (a read-only measurement needs no GO, Klas-direktiv 2026-08-20):

| Quantity | 2026-09-06 | 2026-09-08 |
|---|---:|---:|
| `company_register` rows | 1 066 938 | **1 066 938** |
| ...of which `Active` | 743 654 | **743 654** |
| `job_ads` rows | 106 071 | **106 071** |
| ...of which `Active` | 41 597 | **41 597** |

⚠ **One figure in the old report reads ambiguously and is worth pinning:** its *"41 597 Active, over
9 130 distinct org.nr"* attaches the 9 130 to the **106 071**, not to the Active subset. Measured:
9 130 distinct org.nr over all ads, **7 108** over Active ads, of which **6 920** sit in the Active
register. The sampling proportion that governs the read is therefore 6 920 / 743 654 = **0,93 %**.

`MaxPerUser` is still 20 and ADR 0045 is unchanged. **None of the three triggers fired.** The bound
is being re-derived because its *product* half was measured against the wrong weighting, not because
the corpus drifted.

---

## 2. The product half — the finding

The 2026-09-06 report concluded *"the ordinary criterion materialises with three orders of magnitude
to spare"* from: **83 of 101 180 (kommun, SNI) cells exceed 1 000** (0,082 %).

**That query reproduces exactly** — 101 180 non-empty cells, 83 over 1 000, max 9 958 — so the
instrument is the same one and everything below is comparable to it.

⛔ **But it weighted CELLS uniformly, and a user does not select cells uniformly.** The same 83 cells
hold **13,32 % of all (company, SNI-code) pairs** and **12,31 % of the active ads reachable through
a single-code single-kommun watch**.

| Kommun | Non-empty cells | Over 1 000 | Share of that kommun's company mass in refused cells |
|---|---:|---:|---:|
| Stockholm | 782 | 50 | **57,2 %** |
| Göteborg | 727 | 14 | **33,0 %** |
| Malmö | 699 | 6 | **21,0 %** |
| Nacka | 557 | 1 | 13,6 % |
| Uppsala | 629 | 1 | 7,3 % |

At the coarser selection the picker also offers — a whole 2-digit huvudgrupp × one kommun — it is
109 of 19 688 cells (0,55 %) but **27,9 % of the mass**.

**The observed population, not a tail.** Of the five criteria that exist in dev, **three exceed the
bound**, including the narrowest expressible one:

| Criterion | Predicate | Companies | Outcome |
|---|---|---:|---|
| *(unlabelled)* | `{62100} x {1480}` | **2 295** | refused — the reported defect |
| "Dataprogrammering i Göteborg" | `{62100,62201,62202} x {1480}` | 3 981 | refused |
| "CC2 kolumnmatning" | 9 codes x 26 kommuner | 27 027 | refused |
| "RL-mätning" | `{62100} x {1480}` | 2 295 | refused |
| "Hästavel i Upplands Väsby" | `{01430} x {0114}` | 6 | materialises |

⛔ **One sentence in `CompanyWatchCriterionMember.cs` is therefore false today, independently of
whether the bound moves:** *"what refuses is genuinely broad — a whole industry nationwide, or a big
city."* One SNI code in one kommun is refused. AGENTS.md §5 `Comments:` makes that a defect.

### Cap sweep

Predicate is the gate's own (`CompanyWatchBrowseQuery.FromWhere`), refusal at **> cap** (verified in
`SelectCandidatesAsync`: `LIMIT cap + 1`, abandon on the extra row).

| Cap | Cells refused | % company mass | % active ads | Rows/user at `MaxPerUser` = 20 |
|---:|---:|---:|---:|---:|
| 1 000 (current) | 83 | 13,32 | 12,31 | 20 000 |
| 1 500 | 43 | 9,07 | 7,45 | 30 000 |
| 2 000 | 28 | 6,89 | 2,56 | 40 000 |
| 2 500 | 13 | 4,03 | 1,61 | 50 000 |
| 3 000 | 6 | 2,39 | 1,01 | 60 000 |
| 4 000 | 1 | 0,85 | 0,38 | 80 000 |
| 5 000 | 1 | 0,85 | 0,38 | 100 000 |
| 10 000 | 0 | – | – | 200 000 |

*The ad weighting counts (cell, ad) incidences: a user can reach the same ad through each SNI code
the employer carries, so that is the shape of "watches a user could build", not a count of ads.*

---

## 3. The read cost — instrument, and why its two metrics disagree

**Fixture.** A throwaway `postgres:18.4` on port 55436 (`jbl-1706-probe`). Never the shared dev DB
(§6.5). ⚠ The pre-existing `jbl-1681c-probe` container was **not** reused: it carries `job_ads` and a
`p95()` function but **no `company_register`**, so it cannot answer anchor 1 at all.

**Synthetic, deliberately — not a dump of dev.** A dump would create a second at-rest copy of
register org.nr, some of which are personnummer for enskilda firmor (#841). Only integers left dev:
the per-employer active-ad count multiset.

Verified against dev before any timing was taken:

| Quantity | Target (dev) | Fixture |
|---|---:|---:|
| register rows / Active | 1 066 938 / 743 654 | **exact** |
| `job_ads` rows / Active | 106 071 / 41 148 | **exact** |
| distinct org.nr with Active ads | 7 108 | **exact** |
| ...of them in the Active register | 6 920 | **exact** |
| ads per employer: median / max | 1 / 448 | **exact** (dev's own multiset, by rank) |
| distinct org.nr over all ads | 9 130 | 9 112 (−18) |

The one miss is 0,2 % inside the **non-Active** ad population, which the read never selects
(`j.status = 'Active'`). ⚠ The first fixture spread ads **uniformly** at the mean of 5,85; dev's real
distribution has median **1**. That was corrected before the reported run — the uniform version is
not what these numbers came from.

**Result 5 reproduces.** The delivered statement's plan contains **no `company_register` node**:
`InitPlan` is an `Index Only Scan` on the member PK with `Heap Fetches: 0`, the outer query an
`Index Scan` on `job_ads`'s org.nr index. The register really is gone from the read path.

### 3a. Buffers — the stable metric

| Members | Buffers (shared hit) | vs baseline |
|---:|---:|---:|
| baseline (twin handler, 20 org.nr) | 238 | 1,0x |
| 1 000 | 396 | 1,7x |
| 1 500 | 509 | 2,1x |
| 2 000 | 559 | 2,3x |
| 2 500 | 596 | 2,5x |
| 3 000 | 656 | 2,8x |
| 4 000 | 722 | 3,0x |
| 5 000 | 823 | 3,5x |
| 10 000 | 1 297 | 5,4x |

Monotone, and markedly **sub-linear**: five times the members costs 3,5x the buffers. This is the
repo's own precedent for a cost metric on a noisy host — `2026-09-06-1681-plan-probe-measurement.md`
argues its verdict in buffers per criterion, not in ms.

### 3b. Wall clock — and the instability is itself a finding

⛔ **Anchor 1 is a RATIO against a sub-millisecond denominator, and that denominator is not stable on
this host.** The twin-handler baseline, re-taken in three separate runs of the same fixture:
**0,36 / 0,41 / 0,27 ms p95**. A 1,5x spread in the denominator alone.

The consequence is that the same measured point yields very different "x baseline" figures depending
on which run's baseline it is divided by — the delivered shape at 1 000 members came out at **7,2x**
in one run and **13,6x** in the next, from p95s of 2,94 and 3,66 ms.

p95 over 100 runs (40 at 10 000), warm-ups discarded, delivered shape
(`= ANY(ARRAY(subselect))`, `MaterialisedAdsFromWhere`):

| Members | p95 ms | x baseline (0,27 ms, same run) | x20 | share of 300 ms |
|---:|---:|---:|---:|---:|
| 1 000 | 3,66 | 13,6x | 73,2 ms | 24,4 % |
| 1 500 | 5,02 | 18,6x | 100,4 ms | 33,5 % |
| 2 000 | 5,95 | 22,0x | 119,0 ms | 39,7 % |
| 2 500 | 6,88 | 25,5x | 137,6 ms | 45,9 % |
| 3 000 | 12,53 | 46,4x | 250,6 ms | 83,5 % |
| 4 000 | 15,35 | 56,9x | 307,0 ms | 102,3 % |
| 5 000 | 15,34 | 56,8x | 306,8 ms | 102,3 % |
| 10 000 | 33,91 | 125,6x | 678,2 ms | 226,1 % |

⚠ **These absolutes do not reproduce 2026-09-06's** (1,49 ms at 1 000 there, 3,66 here) and the
session could not close that gap. The old report's *Reproducing* section does not determine the
fixture's ad-distribution shape, and its probe was deliberately not committed. **The x20 column is
therefore a shape, not a verdict** — and it is an upper bound in the report's own sense (a sum of
twenty p95s is not a p95 of the composed read).

**What the two metrics agree on:** the curve is smooth and sub-linear through the 1 000–3 000 band,
with no plan change anywhere in it. **What they disagree on:** whether 1 000 is "6,2x baseline, in
class". Buffers say 1,7x; wall clock says 7,2x or 13,6x depending on the run.

---

## 4. The refusal cost in the 1 000–2 000 band — ADR 0139's own named gap, now measured

ADR 0139 (Klas-beviljande 4) states: *"vägrans-kostnaden i bandet 1 000-2 000 är **omätt**, så att
flytta en produktsynlig tröskel där vore att välja ett tal, vilket är just det #1681 förbjuder."*

Measured against the **real** dev register, read-only, 12 runs per point, `EXPLAIN (ANALYZE)`
execution time, `SELECT organization_number ... LIMIT cap + 1`:

**The reported criterion, `{62100} x {1480}` (2 295 companies):**

| `LIMIT` | min | p50 | max |
|---:|---:|---:|---:|
| 1 001 (refuses) | 5,31 | **5,88** | 38,58 |
| 1 501 (refuses) | 5,26 | 6,11 | 9,92 |
| 2 001 (refuses) | 5,67 | 5,96 | 13,09 |
| 2 501 (materialises) | 5,78 | 6,87 | 10,77 |
| 3 001 (materialises) | 5,77 | **6,18** | 9,38 |

**Flat.** Admitting this criterion instead of refusing it costs it nothing in selection time.

**The widest bound-legal criterion (every SNI x every kommun):**

| `LIMIT` | min | p50 | max |
|---:|---:|---:|---:|
| 1 001 | 7,68 | **9,73** | 12,99 |
| 2 001 | 18,12 | 23,31 | 73,74 |
| 2 501 | 20,74 | 29,48 | 72,46 |
| 3 001 | 25,22 | 29,02 | 85,09 |
| 5 001 | 27,95 | **51,50** | 90,46 |

9,73 ms p50 at the current bound sits inside the old report's stated 11–30 ms. Across the band the
refusal roughly **2,4x's** (9,73 → 23,31 ms), and at 5 000 it is 5,3x the current figure. Still small
in absolute terms, and this is a background job, not a request path.

---

## 5. The write cost — flat in the member count, and the instrument is bloat-sensitive

DELETE + set INSERT, the shape `ReplaceAsync` performs, timed **without** the candidate selection
(which is §4's measurement):

| Members | p95 ms, pristine table | p95 ms, steady state (VACUUM FULL before each point) |
|---:|---:|---:|
| 1 000 | **34,41** | 126,69 |
| 2 000 | 255,44 | 171,09 |
| 2 500 | 490,00 | 149,59 |
| 3 000 | 571,40 | 151,71 |
| 5 000 | 498,17 | 207,15 |

⚠ **Neither column is a clean series** — repeated replace cycles accumulate dead tuples inside the
20-run loop, so the reading depends on where in that accumulation it is taken. The pristine 1 000
figure (34,41 ms) is the one that **reproduces the old report's 30,67 ms p95**, within 12 %, which is
what validates the fixture's write path.

What survives the noise is the **shape**: five times the members costs roughly 1,6x the write, so the
cost is dominated by per-statement overhead rather than by member count. At the sweep's
`SweepBatchSize` = 50 and a 60 s tick, the steady-state figure at 5 000 members would be
50 x ~0,26 s = ~13 s — **22 % of the interval**, against the ~5 % ADR 0139 records at the current
bound.

---

## 6. What this does NOT settle

- **It does not pick a bound, and it is not a recommendation.** Form: `senior-cto-advisor`.
  Minimisation: `security-auditor`.
- **It does not re-derive anchor 1.** §3b shows the ms ratio that anchor rests on is not stable on
  this host, and the 2026-09-06 absolutes could not be reproduced. Whether buffers may stand in for
  that anchor is an architectural judgement this report does not make.
- **It says nothing about the composed `MaxSetSize` x `MaxPerUser` gap** ADR 0139 records as open.
- **It does not price the restore-exposure consequence** of a larger per-user derived row count.
  Both tables are on the accepted list (Klas grant 3, extended to both 2026-09-06); whether that
  grant was given at a size is `security-auditor`'s to read and Klas's to answer.

## Reproducing

The fixture builder and probe are not committed, for the same reason the 2026-09-06 probe was not: a
committed copy decays against the real schema. To re-run: regenerate the row counts and the
per-employer ad-count multiset from dev (read-only), seed a `postgres:18.4` container to the
quantities in §3's verification table — **including the median-1 skew, which a mean-based fixture
gets wrong** — install a `p95(sql, runs)` plpgsql loop, and issue the delivered statement shape. §2's
and §4's figures are plain read-only queries against dev and need no fixture at all.
