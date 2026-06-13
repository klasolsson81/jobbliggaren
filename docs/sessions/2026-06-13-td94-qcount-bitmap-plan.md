# Session 2026-06-13 — TD-94: free-text q-COUNT root fix (perf-ratchet)

**Branch:** `feat/td94-qcount-bitmap-plan` · **HEAD at start:** `419489e`
**Scope:** TD-94 (Major, Trigger/perf-ratchet) — free-text q-COUNT violated ADR
0045 Klass (a) 300 ms p95 warm. Root fix + close.

## Outcome

Free-text q-COUNT restored within ADR 0045 budget. CTO-chosen C1+C3 implemented,
verified warm + shared-cold, TD-94 closed, OS-cold tail raised as TD-110.

## Root cause (dotnet-architect, agentId `a1ce174943247863e`)

The planner chose **Seq Scan**, which de-TOASTs the wide STORED `search_vector`
(521 MB TOAST, ~3 198 B/row) per row to evaluate `@@`. Isolation proof (forced
seqscan, same table): `lower(title) LIKE '%utvecklare%'` = 44 ms / 7 274 buffers
vs `search_vector @@ 'utvecklare'` = 531 ms / 223 335 buffers → **487 ms delta =
pure TOAST-detoast**. When the GIN bitmap serves `@@` (no full-table detoast)
every term is < 160 ms; the planner mis-costs the bitmap because TOAST-detoast
cost isn't in its model. A 2-char term ("ai") additionally forced a full-corpus
scan (a GIN trigram index cannot serve a < 3-char `LIKE '%q%'`). **Disproven**
(do not pursue): `random_page_cost`/`cpu_operator_cost` tuning (does not flip the
planner / gives fragile parallel seqscan) and OR→UNION rewrite (worse plan — `@@`
is mis-costed regardless of predicate form).

## Decision (senior-cto-advisor, agentId `a0472fa5783cdf9ea`) — C1 + C3

- **C3 (filter-semantic):** title-LIKE branch gated on `q.Length >= 3` in the
  shared `ApplyCriteria` (ADR 0039 Beslut 1 SPOT) → list + count + facets share
  the gate, no list↔count divergence. Implemented as a tuple `switch` on
  (includeTitleLike, hasSsyks).
- **C1 (execution-budget):** `CountAsync`, `FacetCountsAsync`, and
  `SearchAsync.totalCount` wrap the pure-count query in a transaction with
  `SET LOCAL enable_seqscan = off` (`CountWithBitmapPlanAsync`) to force the GIN
  Bitmap(Or) plan. The constructor changed from `IAppDbContext` to concrete
  `AppDbContext` (Infrastructure→concrete; the port doesn't expose `Database`).
- **Rejected** (CTO, SPOT-divergence quick fix): "drop title-LIKE from COUNT but
  keep it in list" — creates totalCount ≠ list rows.
- No migration. ADR 0062-amendment documents the C3 semantic change.

## Verified (warm / shared-cold, local dev corpus 42 711 active job_ads)

| Query | Before | After |
|---|---|---|
| ai (2-char, FTS-only) | 777 ms warm / 9 310 ms OS-cold | 15 ms / 16 ms |
| utvecklare (≥3, BitmapOr) | 294–413 ms | 96 ms / 157 ms |
| lärare (≥3, BitmapOr) | 332 ms | 116 ms |

All within ADR 0045 Klass (a) 300 ms p95 warm. **169 integration tests green**
(JobAds 126 + RecentSearches 8 + SavedSearches 35; +2 new TD-94 tests #8/#9).
EF SQL log confirmed `SET LOCAL enable_seqscan = off` emits on the same pinned
connection before the count.

## Deliverables

- `src/Jobbliggaren.Infrastructure/JobAds/JobAdSearchQuery.cs` — C3 gate +
  `CountWithBitmapPlanAsync` (C1).
- `tests/.../JobAds/ListJobAdsFtsTests.cs` — tests #8 (2-char gate) + #9
  (CountAsync == SearchAsync.TotalCount SPOT).
- `perf/Jobbliggaren.LoadTests/Scenarios/FreeTextCountScenarios.cs` (new) +
  `Program.cs` — NBomber `free_text_q_count` (observe-only, ADR 0045 Beslut 5).
- ADR 0062-amendment 2026-06-13 + README index row.
- TD-94 closed (archive entry); TD-110 raised (pg_prewarm/OS-cold, Hetzner-deploy).

## Reviews

- **dotnet-architect** `a1ce174943247863e` — root-cause isolation + candidate
  ranking (MANDATORY).
- **senior-cto-advisor** `a0472fa5783cdf9ea` — C1+C3 verdict (MANDATORY,
  multi-approach; CC gave no own recommendation).
- **code-reviewer** `a674d47ff56f23cab` — 0 Block / 1 Major (commit hygiene:
  `git add` the new scenario file) / 2 Minor (1 in-block-fixed comment, 1
  deliberate pragma). All 8 scrutiny points clean.
- **security-auditor** `a95e8ca9592ead34e` — ✓ Approved 0/0/0/0 (raw SQL = hard-
  coded GUC constant, no injection; q.Length≥3 gate reduces DoS surface net).

## Open / next

- OS-cold cliff structurally reduced (bitmap reads ~1 700–42 000 vs ~177 000
  buffers) but not eliminated → **TD-110** (pg_prewarm, Hetzner-deploy phase).
- ADR 0062-amendment prose is CC-authored (adr-keeper) per the ADR 0062
  proveniens precedent + memory `feedback_klas_can_override_adr_verbatim_source`
  — **Klas reviews prose post-hoc**.
- Stack (Api+Worker) stopped for builds during the session, **restarted before
  session end** (memory `feedback_restart_stack_after_commit_stop`).
