# CTO triage — C2 test-isolation finding (rename-collateral)

- **Date:** 2026-06-13
- **Agent:** senior-cto-advisor (decision-maker, CLAUDE.md §9.2) — finding triage, CC gave no own recommendation.
- **Context:** Product rename PR `refactor/rename-jobbliggaren` (ADR 0069). All gates green except 4 integration tests.

## Finding

`C2ReverseLookupMigrationTests` (6 tests) passes in class-isolation but **4 fail deterministically**
in the full `Api.IntegrationTests` project (476/4), incl. under `-parallel none`. Root cause: the
class shares the `[Collection("Api")]` Testcontainers DB and runs a **whole-table** `saved_searches`
migration replay (`BuildReverseLookupSql` — guard `RAISE EXCEPTION` on any scalar/unmappable `Ssyk`,
plus a table-wide UPDATE). `SearchCriteriaJsonbBackcompatTests` inserts legacy `Ssyk` rows and never
cleans them. The JobbPilot→Jobbliggaren rename changed xunit's name-based execution order, so the
polluter now runs before the C2 success tests → the global guard aborts. A pre-existing shared-fixture
fragility (PR #61 was CI-green under the old order), **surfaced** by the rename — not a product defect.

## Verdict — Path A (in-scope, two-part fix), B/C/D rejected

- **A (chosen):** minimal test-isolation fix INSIDE the rename PR. Both fixtures clear the whole
  `saved_searches` table before each test (so the global replay sees only that test's own row).
  Victim (`C2ReverseLookupMigrationTests`, all 6 tests) AND polluter (`SearchCriteriaJsonbBackcompatTests`).
  Rationale: FIRST/Isolated (Meszaros Interacting-Tests / Shared-Fixture smell); D7 atomicity preserved
  (no second PR); green-CI gate is what makes the rename "done" (Mastercard bar). D7 "purely a rename"
  forbids foreign cargo (the D3 AWS teardown), NOT making your own change pass — this is rename-collateral.
- **B (separate pre-fix PR):** rejected — breaks the single-atomic-PR-on-quiet-tree design (D7) for no
  real purity gain (the fix is collateral, not foreign).
- **C (TD + `[Skip]` the 4 tests):** rejected hard — §9.6 (same-phase, same-touch, trivial cost → not
  TD-eligible) AND it disables the fail-safe guard test (Saltzer/Schroeder) → DoD regression dressed as tidiness.
- **D (push, let CI arbitrate):** rejected — deterministic (not env-specific); CI runs the same command →
  same red.

## Implementation + result

`IAsyncLifetime.InitializeAsync` → `DELETE FROM saved_searches;` (scoped `AppDbContext`, CT propagated,
`GC.SuppressFinalize` for CA1816) before every test in both fixtures, with an isolation-invariant comment.
No TD raised (in-block per §9.6). Verified: `Api.IntegrationTests` **476/0 ×2**.

Full verdict + references recorded in the session log `docs/sessions/2026-06-13-2200-rename-jobbliggaren.md`.
