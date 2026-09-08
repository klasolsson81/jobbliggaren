# 2026-09-08 — #1706: re-deriving the breadth gate's bound (protocol P1–P5)

**Session:** CC, worktree `c:/tmp/jbl-1706b`, branch `fix/1706-breadth-bound`, HEAD `3f5c142e`.
**Status:** measurement + derivation. This is the run `senior-cto-advisor` bound as P1–P5 in
`docs/reviews/2026-09-08-1706-form-cto.md`; the product half's underlying data is
`docs/reviews/2026-09-08-1706-breadth-gate-remeasurement.md`.
**Tracked deliberately** (`git add -f`) — the constant's docblock and ADR 0139 cite it.

**Supersedes nothing.** `2026-09-06-1681-membership-measurement.md`,
`2026-09-06-1681-part2-read-form-measurement.md` and
`2026-09-07-1681-part2-fanin-and-corpus-measurement.md` are dated measurements of finished events and
are left untouched (AGENTS.md §5 `Comments:`). This is the later reading beside them.

---

## P1 — anchor 1 is retired, and nothing replaces it

Bound, not re-argued: the CTO retired the twin-handler ratio because its denominator (0,27 / 0,36 /
0,41 ms p95 on the same fixture) cannot carry it, and forbade the buffer series from standing in.
**This report computes no ratio-to-baseline anywhere.** Its function — protecting ADR 0139's
*"the bucket decision and `MaxPerCriterion` are ONE decision"* conclusion — is about the **composed**
read, and P2 below measures that directly in absolute ms.

---

## P2 — the instrument, and what it reproduces

### The corpus has not moved

Read read-only from `jobbliggaren-postgres-dev` (a read-only measurement needs no GO, Klas-direktiv
2026-08-20), 2026-09-08:

| Quantity | 2026-09-06 | 2026-09-08 (both readings) |
|---|---:|---:|
| `company_register` rows / `Active` | 1 066 938 / 743 654 | **identical** |
| `job_ads` rows / `Active` | 106 071 / 41 597 | **identical** |
| `Active` ads with a non-null org.nr | 41 148 | **identical** |
| distinct org.nr over `Active` ads | 7 108 | **identical** |

`MaxPerUser` is still 20 and ADR 0045 is unchanged. **None of the three cost triggers fired.** The
re-derivation is owed on the fourth trigger — the product distribution was mis-weighted — which is
the trigger this PR also adds to the constant's own list.

### The fixture

The protocol is `2026-09-07-1681-part2-fanin-and-corpus-measurement.md`'s, verbatim: a throwaway
`postgres:18.4` (`jbl-1706b-probe`, port 55437 — never the shared dev DB, §6.5), schema created by
**real EF migrations at HEAD**, and the ports resolved from the **real `AddPersistence` DI graph**
(the `CompanyRegisterPlanFixture` construction: three configuration keys, nothing stubbed).

**Synthetic, deliberately.** A dump would create a second at-rest copy of register org.nr, some of
which are personnummer for enskilda firmor (#841). Only integers left dev: each ad's facet tuple
(taxonomy concept-ids, `remote`, `status`, `published_at`), a **dense employer index**, and each
row's composite size. No org.nr, no free text.

| Quantity | Dev | Fixture |
|---|---:|---:|
| `job_ads` rows | 106 071 | **exact** |
| ...`Active` | 41 597 | **exact** |
| ...`Active` with a non-null org.nr | 41 148 | **exact** |
| distinct org.nr over `Active` ads | 7 108 | **exact** |
| employer → ad grouping | — | dev's own, by dense index |
| heap bytes | 201 605 120 | **200 278 016 (−0,66 %)** |
| heap pages / rows per page | 24 610 / 4,31 | 24 448 / **4,34** |

⚠ **Row width is decisive and a uniform filler cannot reproduce it.** The part-2 report measured a
25x-narrow fixture planning the opposite of the truth. But a constant description length quantises
the packing — measured here at **174 MB (5 rows/page)** and **217 MB (4 rows/page)** with nothing
expressible in between, while dev sits at 4,31. Each row's filler is therefore scaled from **dev's
own per-row composite size**, which carries dev's width VARIANCE; `description` is set to
`STORAGE PLAIN` so the filler stays in the heap instead of being compressed out of it. That lands
0,66 % under dev, against the 2026-09-07 fixture's +7,6 %.

### What the instrument reproduces from the report it re-runs

The three profile shapes are the report's own — realistic 5 SSYK / 2 län / 3 kommuner / 2 employment
types, rich 50 / 21 / 100 / 8, ceiling all SSYK / all län / all kommuner — and the facet universe
they are drawn from measures **391 SSYK, 21 län, 290 kommuner, 8 employment types**, i.e. the
report's "all 391 SSYK / all län / all kommuner" verbatim. Their matched shares reproduce:

| Profile | 2026-09-07 | Here |
|---|---:|---:|
| realistic | 5,5–6,1 % | **5,5–6,6 %** |
| rich | 57 % | **56,0–56,1 %** |
| ceiling | 98 % | **97,4–99,0 %** |

---

## The cohort, and why its ad count is re-taken at every bound

The cohort is the part-2 report's `realistic` one: **20 criteria (`MaxPerUser`), all pinned AT the
member bound** so the member axis sits at its worst case and only the ad axis varies, with member
slices disjoint across criteria.

⚠ **Its ad count is a MEASURED parameter of that definition, not a constant of it.** The 2026-09-06
report derived 202 as *"the measured p95 of the broadest ordinary shape"* — one SNI code across all
290 kommuner — **restricted to bound-legal criteria**. Moving the bound changes which codes are
bound-legal, and the newly legal ones are the larger ones. Holding 202 fixed would hold an input
constant that the bound demonstrably moves, which is the exact error §2 of the remeasurement found.
So the distribution is re-read at each candidate (read-only against dev, the same query):

| Bound | Bound-legal SNI codes | Median ads | **p95 ads** | Max ads | Over `MaxSetSize` |
|---:|---:|---:|---:|---:|---:|
| 1 000 | 627 | 6 | **204** | 5 519 | 2 |
| 1 500 | 677 | 7 | **217** | 5 519 | 4 |
| 2 000 | 707 | 8 | **217** | 5 519 | 4 |
| 2 500 | 729 | 8 | **227** | 5 519 | 5 |
| 3 000 | 741 | 9 | **232** | 5 519 | 6 |
| 4 000 | 755 | 9 | **243** | 5 519 | 7 |
| 5 000 | 769 | 10 | **270** | 6 822 | 8 |

At the current bound this reproduces 2026-09-06's population exactly (627 codes, median 6, max
5 519, 2 over `MaxSetSize`); its p95 reads **204** here against the 199 printed there, a
nearest-rank-vs-interpolation difference on the same 627 values. **The instrument is the same one.**

---
## The measurement, and the two instrument defects found on the way

**The composed read is measured DIRECTLY, not summed.** Both prior reports label their Σ an upper
bound in their own words (*"a sum of p95s is not a p95 of the composed read"*). Here one DI scope
issues the handler's 40 dominant statements — 20 `CountActiveAdsAsync`, then 20
`ListActiveAdIdsAsync`, then ONE `FilterToMatchingAsync` over the union — and the p95 is taken over
that.

⚠ **It is a SUBSET of the request, regrouped, and the omission leans toward a LARGER bound.** Two
things it does not charge: `IMatchProfileBuilder.BuildFullForSortAsync`, which `MatchingBatchAsync`
runs once before phase 1, and the handler's own `db.CompanyWatchCriteria … ToListAsync`. And
production interleaves — `ResolveIdsAsync` does count(1), ids(1), count(2), ids(2) … — where this
groups the counts and then the ids. So the composed figure is 41 of roughly 43 statements in a
different order. The host factor below leans the opposite way and is the larger of the two, but the
two are named rather than netted: at 2 500 the worst repeat is 244,4 ms, and two more statements
would not have to be large to reach the line. The port reuses the
`AppDbContext`'s connection (`OpenConnectionAsync`), so 40 statements share one connection exactly as
a request does. **This is a strictly fuller instrument than 2026-09-07's composed table**, two of
whose three terms came from a server-side `plpgsql` loop with no client, no EF and no DI; that report
says so itself (*"nothing else in the handler is counted"*).

⚠ **Defect 1 — a block design would have measured the machine.** The first sweep ran every repeat of
one bound before moving to the next, and the host drifted **4,8x within a single process** (the same
20 statements: 44,18 ms forward, 212,61 ms on the control pass minutes later). A block design aliases
that drift straight onto the bound axis. The sweep below therefore **interleaves**: each repeat walks
every bound, odd repeats in reverse bound order (the protocol's reverse-order control applied to the
axis that matters), and each bound's figure is the **median of its repeats' p95s** with min and max
printed beside it.

⚠ **Defect 2 — the fixture could report the previous bound's cohort.** The first sweep printed a
union that did not match the cohort it had just built (4 640 ids at bound 4 000, which is bound
3 000's 20 × 232), and a later rebuild died on a foreign-key violation with half a cohort applied.
The rebuild is now ONE transaction, and after it commits the harness **queries the database** for the
criterion count, the member count and the reachable Active-ad count, and **throws** if the port's
union disagrees with the database. Every row below is under that check. A fixture that silently
measures the wrong bound is worse than one that fails.

---

## The finding that outranks the ms series: the member lookup's plan depends on the criterion POPULATION, not on the bound

The first full sweep produced a series that was **non-monotone** — 3 000 and 4 000 breached the
budget while 5 000 did not — and a non-monotone series is either noise or a plan change. `EXPLAIN
(ANALYZE, BUFFERS)` at every candidate says which:

| Members per criterion | Criteria in the table | Member-set node | Buffers for that node |
|---:|---:|---|---:|
| 1 500 – 5 000 | **20** | `Seq Scan on company_watch_criterion_members` | 2 650 |
| 5 000 | 20, `n_distinct` overridden to 500 | `Index Only Scan using pk_company_watch_criterion_members` | **55** |
| 2 500 | **220** (200 other users' criteria) | `Index Only Scan using pk_company_watch_criterion_members` | 397 (whole id-set statement) |

⛔ **Two readings, and they are of different things — neither is "about 30x".** The member-set NODE
is 2 650 buffers against 55 (**48x**); the whole id-set STATEMENT is 3 040 against 552 (**5,5x**),
because the ad-side work is common to both plans. Quote whichever answers the question being asked,
and never a single number for "the cost of the flip".

The mechanism is the selectivity estimate for `criterion_id`: a fixture holding only ONE user's
watches has `n_distinct = 20`, so the planner prices `criterion_id = X` at 5 % of the table, and it
abandons the primary key for a sequential scan somewhere between 20 000 and 30 000 rows — 20 x 1 000
still takes the index, 20 x 1 500 does not, so 30 000 is the first point measured ALREADY flipped
rather than the turning point. Raising the estimate alone — `ALTER TABLE … SET (n_distinct = 500)`,
no data moved — flips the plan straight back, which is what identifies the cause rather than merely
correlating with it.

⚠ **That last probe is a CAUSAL instrument, not a production state.** A hand-set `n_distinct` on a
20-criterion table is a statistic no deployment produces; it earns its place by isolating the cause
and nothing else. The regime-B row below is the production-shaped reading, and it was verified by its
own `EXPLAIN` at the chosen bound.

**Two consequences, and the second is a defect in delivered code.**

1. **The bound's cost is not a function of the bound alone.** The same member count is served by two
   plans about 30x apart in buffers, and which one it gets depends on how many criteria the table
   holds — i.e. on how many users the product has. So the derivation is run in BOTH regimes and P4's
   minimum is taken over them:
   - **Regime A — few criteria, one power user.** 20 criteria, table = 20 x bound. Reachable
     **today**: the product has two accounts, and one user filling `MaxPerUser` is all it takes.
   - **Regime B — a criterion population.** 220 criteria (200 other users' watches at the same
     size), table = 220 x bound. This is the plan ADR 0139 derived against, and it is where the
     product goes as it grows. ⚠ **It is NOT the plan `CompanyWatchBrowseQueryPlanTests` pins** —
     that test EXPLAINs under `SET LOCAL enable_seqscan = off`, which forbids the alternative rather
     than pricing it, so it measures index ELIGIBILITY (its own helper says so) and can observe
     neither regime's plan CHOICE. Regime B here is verified by this report's own `EXPLAIN`.

2. ⛔ **`CompanyWatchCriterionMemberConfiguration`'s docblock is false above 1 000 in regime A.** It
   states that *"the whole member set of one criterion is a leading-column range scan … (Index Only
   Scan on this PK, Heap Fetches: 0)"*. Measured: at 1 500 members with 20 criteria it is a `Seq
   Scan`. That claim was true at the bound it was written at — 20 000 rows sits just under the flip —
   and this PR is what would make it false, so this PR is what corrects it. AGENTS.md §5 `Comments:`.
   The same correction is owed to `CompanyWatchBrowseQuery`'s `MaterialisedAdsFromWhere` docblock,
   which asserts the same `InitPlan` shape unconditionally.

⚠ **The two prior fixtures had regime A's shape too** (both seeded 20 criteria), and both reported
the Index Only Scan — correctly, because at 20 x 1 000 = 20 000 rows the table is 93 pages and the
index still wins. Nothing they measured is retracted; what is new is that the plan they measured
**stops being the plan** a little above the bound they measured it at. That is precisely why the
bound may not be moved by reading a sweep table.

---

## P3 — regime A (20 criteria): the composed p95 at each candidate

5 interleaved repeats, p95 over 40 runs each (5 warm-ups discarded), realistic profile — the
everyday state P3 binds on.

| Bound | Union ids | rep0 | rep1 | rep2 | rep3 | rep4 | **median** | worst | share of 300 ms |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 000 | 4 080 | 151,6 | 115,4 | 121,8 | 116,6 | 94,4 | **116,6** | 151,6 | 38,9 % |
| 1 500 | 4 340 | 219,7 | 129,4 | 142,4 | 125,5 | 124,2 | **129,4** | 219,7 | 43,1 % |
| 2 000 | 4 340 | 236,0 | 136,7 | 139,5 | 132,0 | 134,7 | **136,7** | 236,0 | 45,6 % |
| 2 500 | 4 540 | 244,4 | 153,6 | 161,5 | 195,4 | 182,8 | **182,8** | 244,4 | 60,9 % |
| 3 000 | 4 640 | 332,3 | 902,1 | 306,3 | 306,0 | 188,6 | **306,3** | 902,1 | **102,1 %** |
| 4 000 | 4 860 | 340,0 | 425,8 | 210,0 | 362,7 | 192,9 | **340,0** | 425,8 | **113,3 %** |
| 5 000 | 5 400 | 239,7 | 265,6 | 247,5 | 229,3 | 210,7 | **239,7** | 265,6 | 79,9 % |

**Read the repeats, not only the median.** Through 2 500 the budget holds in **5 of 5 repeats** and
the worst reading is 244,4 ms (81,5 % of budget). At 3 000 it fails in **4 of 5**, and the median
itself breaches. 5 000 comes back under the budget, but it sits **above two bounds that fail**, and
P4 forbids interpolation for exactly this reason: "the largest measured point satisfying the
ceiling" cannot mean a point reached by stepping over failures. The largest point where the ceiling
holds and holds at every point below it is **2 500**.

⚠ **rep0 is the dearest reading at every single bound** (151,6 / 219,7 / 236,0 / 244,4 / 332,3 /
340,0 / 239,7). That is a whole-sweep cold-cache effect, not a per-point one, and it is the reason
the design interleaves and takes a median rather than trusting one pass.

## P3 — regime B (220 criteria): the same sweep where the primary key is used

Two interleaved passes, forward and reverse (the protocol's own control), same 40-run p95 per point,
same realistic profile. The member table holds 200 other users' watches at the same size, so the
measured criterion is 0,45 % of its rows and the plan is the `Index Only Scan` this ADR derived
against — verified by `EXPLAIN` at the chosen bound rather than assumed.

| Bound | Union ids | forward | reverse | mean | share of 300 ms |
|---:|---:|---:|---:|---:|---:|
| 1 000 | 4 080 | 124,5 | 114,4 | **119,5** | 39,8 % |
| 1 500 | 4 340 | 129,4 | 141,3 | **135,4** | 45,1 % |
| 2 000 | 4 340 | 135,5 | 131,8 | **133,7** | 44,6 % |
| 2 500 | 4 540 | 144,1 | 144,1 | **144,1** | 48,0 % |
| 3 000 | 4 640 | 160,1 | 190,0 | **175,1** | 58,4 % |
| 4 000 | 4 860 | 172,9 | 175,1 | **174,0** | 58,0 % |
| 5 000 | 5 400 | 226,4 | 209,0 | **217,7** | 72,6 % |

**Monotone, and inside the budget at every candidate.** That is the whole difference the plan makes:
the same handler, the same rows, the same statements — and the series stops jumping. **Regime B
therefore binds nothing**; its ceiling is at or above 5 000, the highest point measured.

⚠ Two passes, not five. The regime-B rebuild rewrites 220 x bound member rows (1,1 M at bound 5 000)
and drew a concurrent autovacuum, so a five-repeat design would not have finished. Forward plus
reverse is the protocol's own control shape; it is a thinner reading than regime A's and is reported
as such. It does not need to be thicker: regime B's role in the derivation is only to show that it
does not bind, and it is inside the budget in **both** passes at **every** bound.

---

## The write half, re-derived — `SweepBatchSize` does not inherit

`security-auditor` (2026-09-08, finding 5) named `SweepBatchSize` = 50 as COMPUTED from a cost
measured at the old bound, and therefore not inherited across a bound change. Re-measured with
`CompanyWatchCriterionMemberStore.ReplaceAsync`'s own three statements in its own single transaction
— `DELETE` by `criterion_id`, the `jsonb_to_recordset` insert, the state upsert — p95 over 20 timed
runs after 5 discarded:

| Members | Replace p95 | min | max |
|---:|---:|---:|---:|
| 1 000 | 62,07 ms | 55,17 | 64,16 |
| 2 500 | **74,32 ms** | 26,00 | 74,95 |

Five halves more members costs 20 % more write: the cost is dominated by per-statement overhead, not
by member count, which is the same shape the 2026-09-08 remeasurement found. The 1 000-member figure
also cross-checks the host: 2026-09-06 measured 30,67 ms p95 for the same replace, and 62,07 / 30,67
= 2,0x, consistent with the independent host reading below.

**The arithmetic `SweepBatchSize` is computed from, re-run at 2 500:** per criterion end to end is
this replace plus the candidate selection at the matching `LIMIT` (29,48 ms p50 against dev at
`LIMIT 2501`, `2026-09-08-1706-breadth-gate-remeasurement.md` §4) = **103,8 ms**, so a tick's 50 of
them is **5,2 s, about 9 % of the 60 s interval** — against 6 % at the old bound. **50 survives the
re-derivation** with the interval more than ten times the work.

⚠ **That sum adds a p95 on a fixture to a p50 against dev** — two instruments, as `dotnet-architect`
flagged of the same arithmetic on 2026-09-08. The conclusion is insensitive to it (the headroom is an
order of magnitude) but the figure is a composed estimate, not a quantile, and is written as one.

---

## The host, measured rather than assumed

The absolute ms here are dearer than 2026-09-07's, and the report being re-run says why in advance:
*"the machine ran the identical fixture ~1.8x faster on 09-07 than on 09-06 … absolute figures are
not comparable across the two passes; ratios taken within one process are."* Two independent
readings of this pass's host:

- **Server-side control**: the same 20 count statements timed in a `plpgsql` loop with no client, no
  EF and no DI — the 2026-09-06 instrument exactly — read **17,4 ms p95** here against **9,4 ms**
  there: **1,85x**.
- **The write**: 62,07 ms against 30,67 ms for the identical replace: **2,0x**.

⚠ **No figure in this report is normalised by that factor**, and that is deliberate. Dividing by a
host constant to reach a friendlier bound would be choosing the number the CTO's ruling forbids;
measuring on a slower host errs toward a SMALLER bound, which is the safe direction. The consequence
is stated rather than hidden: **the derived bound is conservative by roughly the host factor**, and a
re-run on a quieter machine would license a larger one, not a smaller.

⚠ A second, independent reason the absolutes are dearer: two of the three terms in 2026-09-07's
composed table were server-side only. This instrument charges the client half of all three, which is
what the handler actually pays.

---

## P3's other half — the far tail, reported and NOT vetoing

The far-tail state is the part-2 report's `ceiling` cohort verbatim: 20 criteria at the member bound,
each carrying **exactly 2 000 active ads** so every one passes `MaxSetSize` and all 40 000 ids reach
the grading call. Measured at BOTH bounds **on this host, in this pass**, because that is the only
comparison the instrument licenses:

| | bound 1 000 | bound 2 500 |
|---|---:|---:|
| Σ of p95s, realistic profile | 2 038,1 ms | **1 245,1 ms** |
| Σ, rich profile | 2 145,8 ms | **1 311,9 ms** |
| Σ, ceiling profile | 2 172,6 ms | **1 387,9 ms** |
| composed p95, realistic | 1 940,8 | 1 219,2 |
| composed p95, rich | 1 322,3 | 2 300,7 |
| composed p95, ceiling | 1 312,2 | 3 612,1 |

⛔ **Read this table as a null result — and as a table that does not hold together.** Three things,
all of them reasons to take nothing from it:

1. **The composed p95 exceeds its own Σ at the chosen bound.** Σ is a sum of p95s and therefore an
   upper bound on the composed read; at 2 500 the ceiling profile reads Σ 1 387,9 against a composed
   3 612,1 (2,60x), and rich 1 311,9 against 2 300,7 (1,75x). Under the stated relation that is
   impossible, so at least one of the two columns is broken at this cohort size.
2. **Σ and the composed column disagree about direction.** Σ says the far tail is *cheaper* at the
   higher bound — which also contradicts this report's own finding that regime A is on the `Seq Scan`
   plan at 2 500. The composed column says dearer for two profiles and cheaper for one.
3. ⚠ **This table carries NO repeats, no median and no interleaving.** Every other table here was
   rebuilt to have them, because Defect 1 measured the host drifting 4,8x inside one process. These
   are single cells, and they are exposed to exactly the defect this report rejected elsewhere. That
   is a disclosure, not a caveat: the design was not applied here.

The one ordering fact worth keeping is not "non-monotone" — each bound orders the profiles
consistently, 1 000 falling with richness and 2 500 rising — but that **the two bounds order them
OPPOSITELY**, which is itself a signature of an instrument that is not resolving the variable.

**At this cohort size — 20 criteria each at the ad cap, 40 000 ids in one grading call — the
instrument does not resolve a bound effect at all**, and no claim about the far tail's direction is
made here in either direction.

⚠ **The comparison to Klas-beviljande 4's 316,7 / 381,5 ms is therefore a CROSS-HOST one and cannot
be attributed to the raise.** Those figures were taken on a host this pass measures as 1,85–2,0x
faster, with two of three terms server-side only; the same state re-measured here at the **unchanged**
bound reads 2 038–2 173 ms. A reader who subtracts the two and calls the difference "the cost of the
raise" would be reading the host. **The bound-attributable change is what the table above shows, and
it is nothing.**

Klas pre-granted this growth on 2026-09-08 — *"svansen får växa så länge vardagsfallet håller 300 ms"*
— and the everyday case does hold. **No escalation is owed and none is raised.**

---

## P4 — the bound is the MINIMUM of the derived ceilings

| Ceiling | Owner | Value | Basis |
|---|---|---:|---|
| (a) cost, regime A (few criteria) | this measurement | **2 500** | largest candidate where the everyday composed p95 is inside 300 ms in **5 of 5** repeats, and every candidate below it likewise. 3 000 breaches in 4 of 5 and breaches at the median. |
| (a) cost, regime B (a criterion population) | this measurement | ≥ 5 000 | inside the budget in both passes at every candidate measured; binds nothing |
| (b) Art. 5(1)(c) | `security-auditor` | — | 2026-09-08: the article requires the gate to EXIST and to refuse rather than truncate; *"bestämmelsen skiljer alltså inte 1 000 från 2 500"*. It sets no ceiling and she declined to offer one. Her signature on THIS raise is a separate, blocking matter (below). |
| (c) Klas-beviljande 4 | Klas | — | the far-tail growth is **pre-granted** (2026-09-08) for as long as the everyday case holds 300 ms, which it does. |

**Minimum = 2 500**, and it comes from regime A — the regime with few criteria, which is the product's
state today.

**5 000 is not licensed, and the reason is MINE rather than P4's.** P4(a) says *"the largest measured
point satisfying P3's binding ceiling"*, and read literally that is 5 000 — it is inside the budget in
regime A in 5 of 5 repeats. The clause *"and every point below it also holds"* is an addition of this
report's, not a reading of P4's interpolation ban, and it is written here as such rather than
attributed to the protocol. The ground for it: **a series that fails at 3 000 and at 4 000 establishes
no ceiling at 5 000, whatever 5 000 reads.** Taking 5 000 would mean stepping over two measured
failures and calling the far side derived.

⚠ **And the failing band's cause is UNRESOLVED, which is the honest state to leave it in.** The
`EXPLAIN` sweep shows the SAME `Seq Scan` through 1 500–5 000 — through the failing band and through
the passing 5 000 alike — so the plan change explains regime A against regime B, and does **not**
explain 3 000/4 000 against 5 000. By this report's own dichotomy that leaves noise, on a host it
measured drifting 4,8x within one process. 2 500 is therefore the conservative reading under
unresolved uncertainty, which is a weaker claim than "2 500 is where the cost crosses the line" and
is the one the data supports.

**What 2 500 buys, from the product half already measured** (`2026-09-08-1706-breadth-gate-remeasurement.md`):
the share of company mass refused falls from **13,32 % to 4,03 %**, the share of active ads from
12,31 % to 1,61 %, and the refused cells from 83 to 13. **Klas's own criterion — `{62100} × {1480}`,
2 295 companies, one SNI code in one kommun — is admitted**, which is #1706's stated acceptance.

⚠ **What it does not buy.** Some single cells stay refused at any affordable bound: the largest is
9 958 companies. The refusal is a smaller state, not an eliminated one, and the copy has to keep
working for it — which is why #1706's copy constraint survives this change rather than being closed
by it.

---

## P5 — where the result lands

The constant moves in the same commit as its literal pin (`CompanyWatchMaterialisationOptionsTests`),
ADR 0139 gains a dated amendment carrying the bound AND its magnitude, and the Art. 30 entry is
updated in the same PR. `CompanyWatchBrowseQueryPlanTests`' `SeededRows` grows from 2 000 to 3 000 so
its three refusal pins keep measuring a refusal — the fixture grows to fit the bound, never the other
way round.

⛔ **Magnitude, written here and in ADR 0139 as `security-auditor` required, never in a PR body.** At
`MaxPerUser` = 20 the worst case is **50 000 derived rows per data subject**, against 20 000 before.

## What this does NOT settle

- **`security-auditor`'s signature on the raise.** Her standing note: a PR that RAISES
  `MaxPerCriterion` is a new review of hers and inherits nothing. Her verification of the
  `CompanyWatchCriteriaList` rate-limit bucket (5 burst / 3 per minute, no downward margin left, a
  higher `PermitLimit` excluded) is **blocking**, and the bucket's own recompute trigger names this
  constant explicitly.
- **The host.** Every figure is un-normalised and the bound is conservative by roughly the measured
  host factor (1,85–2,0x). A quieter machine licenses a larger bound, never a smaller.
- **Whether regime A is the right regime to bind on as the product grows.** It is the dearer one
  today and the minimum is taken there. If the criterion population grows past the flip, regime B's
  much larger ceiling becomes the live one — and that is a re-derivation trigger in the constant's
  own list now, not a silent improvement to be spent.
- **`deriveDisplayLabel` naming the division for a single leaf** (`design-reviewer` B5). Untouched
  here on purpose: it is a separate change-reason and must not widen #1706.

## Reproducing

Not committed as a script, for the reason the sibling reports give: a committed probe decays against
the real schema. Regenerate dev's row counts, per-ad facet tuple, dense employer index and per-row
composite size (read-only); seed a `postgres:18.4` at those quantities with `description` set to
`STORAGE PLAIN` and the filler scaled per row until `pg_relation_size('job_ads')` matches dev's;
build the schema with the real EF migrations and resolve the ports from the real `AddPersistence`
graph; then measure the handler's own sequence in ONE scope, interleaving the candidate bounds across
repeats. ⚠ **Seed the member table with more than one user's criteria, or the planner will hand you a
`Seq Scan` and a series that is mostly the fixture.** The product-distribution figures are plain
read-only queries against dev and need no fixture at all.
