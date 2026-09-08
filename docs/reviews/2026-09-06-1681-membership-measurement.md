# 2026-09-06 — #1681 part 1: deriving the breadth gate's bound

**Session:** CC, worktree `c:/tmp/jbl-1681b`, branch `feat/1681-criterion-members`.
**Status:** measurement. Taken to DERIVE a constant, not to justify one already chosen.
**Tracked deliberately** (`git add -f`, the `.gitignore` exception): `CompanyWatchCriterionMember`'s
docblock cites this file from tracked source, so leaving it gitignored would ship a pointer that is
dead for anyone reading on GitHub, in a fresh clone, or in a worktree where
`sync-worktree-docs.ps1` was not run.

> ⚠ **Later reading, 2026-09-08 (#1706):** every measurement below reproduces exactly, and nothing
> here is withdrawn. What did not survive is an **inference** — Result 4 weighted (kommun, SNI) cells
> uniformly, and re-weighted by company mass the same 83 cells hold 13,32 %. See
> `docs/reviews/2026-09-08-1706-breadth-gate-remeasurement.md`. The bound is under re-derivation
> against the protocol in `docs/reviews/2026-09-08-1706-form-cto.md`.

## What had to be derived, and why a number could not simply be picked

ADR 0139 makes the breadth gate mandatory and gives it three simultaneous loads: the product's
honest "för bred", the read-cost bound, and Art. 5(1)(c) minimisation (`security-auditor`: without
it the derived storage is unbounded, and unbounded derived storage is not *"limited to what is
necessary"*). #1681's acceptance list then requires the bound itself to be **derived**, and requires
the member cap's read cost to be measured **against `ListCompanyWatchesQueryHandler`'s cost class**
— that being the one thing that can make ADR 0139's `MeListRead` conclusion wrong in hindsight.

## Instrument

Throwaway `postgres:18.4` container on port 55432 (never the shared dev DB, §6.5 — and never the
Testcontainers suite either, which is for oracles rather than timings). Schema mirrors the delivered
shapes: `company_register` with its PK, GIN `array_ops` on `sni_codes`, btrees on
`sate_kommun_code`, `status`, `synced_at` and `(company_name, organization_number)`; `job_ads` with
btrees on `organization_number` and `status`; the proposed member table with **both** candidate index
shapes present, so the probe measures the planner's CHOICE rather than one we pre-committed to.

Row counts are dev's own, read read-only from `jobbliggaren-postgres-dev` on the same day (a
read-only measurement needs no GO, Klas-direktiv 2026-08-20):

| Table | Rows | Of which |
|---|---|---|
| `company_register` | 1 066 938 | 743 654 `Active` |
| `job_ads` | 106 071 | 41 597 `Active`, over **9 130 distinct org.nr** |

Member sets are drawn as a **random sample** of the Active register, so each set carries ad-bearing
org.nr in the production proportion (9 130 / 1 066 938 = 0,86 %). The first attempt took the first N
by org.nr and produced sets with near-zero ad overlap — the queries then measured a bitmap heap scan
that touched nothing, and the numbers were meaningless. That is recorded here because the corrected
figures are 5-10x the discarded ones.

`p95` over 40 runs per point (20 or 10 for the slowest), via a `plpgsql` timing loop.
**Never a warm singleton** — `docs/reviews/2026-09-04-1559-perf-test-writer.md` forbids a verdict
on single observations, and the 6 556 ms figure this whole issue rests on is itself a singleton.

## Result 1 — the read, per member-set size

Baseline: `ListCompanyWatchesQueryHandler`'s bounded `= ANY(org.nr array)` GROUP BY over `job_ads`
for 20 followed companies — **0,24 ms p95**.

| Members | `= ANY(member array)` p95 | vs baseline | `JOIN members` p95 |
|---:|---:|---:|---:|
| 100 | **0,22 ms** | 0,9x | 6,02 ms |
| 1 000 | **1,49 ms** | 6,2x | — |
| 5 000 | 4,23 ms | 17,6x | — |
| 10 000 | 21,55 ms | 90x | 9,16 ms |
| 20 000 | 42,11 ms | 175x | — |
| 50 000 | 107,05 ms | 446x | — |
| 100 000 | 220,73 ms | 920x | — |
| 743 654 (ungated) | **2 068 ms** | 8 617x | 46,28 ms |

**The two shapes cross over, and that is the finding.** `= ANY(<member array>)` — the twin handler's
own shape, and the one ADR 0139 names (*"the read becomes the same bounded `GROUP BY` over `job_ads`
the company block already runs"*) — keeps the baseline's exact plan (`Bitmap Index Scan` on the
`job_ads` org.nr index → `Bitmap Heap Scan` → `Sort` → `GroupAggregate`) and its cost **grows with the
member count** — not strictly linearly (per-member cost ranges 0,85–2,78 µs across the series, and
5 000→10 000 members costs 5,1x for 2x the members), which is why the class boundary is argued from
the measured points either side of it rather than from a fitted slope. The `JOIN` form is bounded by `job_ads` instead: the planner drives from a full index
scan of all 41 597 active ads and hash-joins the members, so it costs the SAME at 100 members as at
10 000, and is *worse* than the baseline at every small size.

So the naive join is not in the twin handler's cost class at any member size — the linear shape is,
below a bound. That bound is what the gate has to set.

## Result 2 — the derivation, from two independent anchors

**Anchor 1, the cost class.** "Same cost class as the twin handler" read as same order of magnitude:
1 000 members is 6,2x the 0,24 ms baseline (in class), 5 000 is 17,6x (out).

**Anchor 2, the budget at the criterion cap.** `CompanyWatchCriterion.MaxPerUser` = 20, and
`/oversikt`'s budget is 300 ms p95 (ADR 0045 class (a)). The block figures below are **20 x the
per-criterion p95, i.e. an UPPER BOUND** — the sum of twenty p95s is not itself a p95 of the
composed read. Conservative in the right direction, but it is a bound and is labelled as one:

| Members | Block cost at N=20 | Share of the 300 ms budget |
|---:|---:|---:|
| 1 000 | **29,8 ms** | **9,9 %** — a block-sized share |
| 5 000 | 84,6 ms | 28 % for one block |
| 10 000 | 431 ms | over budget on its own |

**Both anchors land on 1 000.** `CompanyWatchCriterionMember.MaxPerCriterion = 1000`.

## Result 3 — what the gate is worth, on all three of its loads

**Read cost.** Ungated, the same statement is 2 068 ms p95 — the gate is worth ~1 388x there, and
turns a figure 6,9x over the whole page budget into 0,5 % of it.

**Job cost.** The register selection under `LIMIT MaxPerCriterion + 1`, measured against the REAL
1 066 938-row dev register (read-only), 12 runs each:

| Criterion | Companies | Selection cost |
|---|---:|---|
| Widest bound-legal (every SNI x every kommun) | refused at 1 001 | **11-30 ms** |
| One SNI nationwide (`46712`) | 903 | 1,2-2,4 ms (31,6 ms cold) |
| One SNI x one kommun (`71123` x `0114`) | 30 | 0,9-1,4 ms (8,0 ms cold) |

The `LIMIT` stops the bitmap heap scan early, which is what makes the REFUSAL cheap: the widest
criterion costs 11-30 ms to refuse, against the **6 556 ms** the ungated live ad count cost for the
same criterion. Writing a full 1 000-member replace (DELETE + `jsonb_to_recordset` INSERT) is
**30,67 ms p95**. So a criterion costs at most ~60 ms end to end, and the whole nightly run is
seconds at any plausible corpus — comfortably inside the 05:30 slot, with the digest window 30
minutes later.

**Storage (Art. 5(1)(c)).** Per-user derived rows fall from 743 654 x 20 = 14,9M to 1 000 x 20 =
**20 000**: a **744x** reduction, and the operative value of the minimisation argument.

## Result 4 — the product half, on the real register

A bound that refuses ordinary use would be a bug wearing a rationale, so the breadth distribution was
measured rather than assumed (read-only, dev, `company_register` where `status = 'Active'`):

| Criterion width | Population | Median | p95 | Max | Over 1 000 |
|---|---:|---:|---:|---:|---:|
| 1 SNI x 1 kommun (the narrowest expressible) | 101 180 cells | **2** | 39 | 9 958 | **83 (0,08 %)** |
| 1 SNI x all 290 kommuner (a whole industry) | 830 codes | 260 | 6 458 | 68 483 | 203 (24 %) |
| 1 kommun x all SNI (a whole municipality) | 291 kommuner | 990 | 7 444 | 112 383 | 144 (49 %) |

The ordinary criterion materialises with three orders of magnitude to spare. What refuses is
genuinely broad — a quarter of whole industries nationwide, and the big cities — and those are
exactly the criteria whose live ad count was unaffordable to compute, so the refusal lands where the
cost was.

## Result 5 — the register scan really is gone from the read path

The `= ANY(member array)` plan at 10 000 members contains no `company_register` node at all: the
`InitPlan` is an `Index Only Scan` on the member table's PK (`Heap Fetches: 0`) and the outer query
is a `Bitmap Index Scan` on `job_ads`'s org.nr index. That is #1681's fourth required measurement —
the point of the whole exercise, stated as something a plan can show rather than something the design
asserts.

## What this does NOT settle

The **shape** part 2 uses. Both forms were measured because the crossover means part 2 has a real
choice — in particular, a form that batches all of a user's criteria into one statement keyed by
`criterion_id` would be bounded by `job_ads` rather than by N, and this bound stays safe under it.
The bound is derived against the per-criterion `= ANY` shape ADR 0139 names, which is the
conservative side of the crossover; a part-2 shape that is cheaper than that does not invalidate it,
and one that is more expensive would have to re-derive.

**Re-derive, do not nudge.** The bound is a function of three measured quantities — the twin
handler's cost class, `MaxPerUser`, and ADR 0045's `/oversikt` budget. If `job_ads` grows materially,
if `MaxPerUser` moves, or if the budget changes, re-run this protocol.

## Reproducing

The probe is not committed as a script: a committed copy would decay against the real schema, the
same reasoning `docs/reviews/2026-09-06-1681-plan-probe-measurement.md` gives. To re-run, seed a
`postgres:18.4` container at the row counts in the Instrument section above (regenerate them from dev
first — they are what makes the numbers transferable), install a `p95(sql, runs)` `plpgsql` timing
loop, and issue the two statement shapes in Result 1. The real-register figures in Results 3 and 4
are plain read-only queries against `company_register` and need no fixture at all.
