# Admin overview performance verification — 2026-10-08

Scope: #1978, the account overview handler and its authenticated endpoint. Measurements are a dated local experiment, not a claim about production capacity.

## PostgreSQL query plan

The integration probe runs the actual `SqlAccountDirectory` command against migrated PostgreSQL in an isolated Testcontainer. Session-local `auto_explain` records JSON ANALYZE/BUFFERS for the raw Npgsql aggregate; an EF interceptor would not observe this command. No production logging/configuration is changed. The report projects only plan-node names and numerical counters, never account identifiers or SQL parameter values.

The fixture adds 200 retained accounts through production registration/deletion/suspension actors, with registrations across 90 Swedish calendar days. Twenty historical incomplete rows cite the current registration atomicity test's writer pin. With the baseline administrator the observed population is 201. Tables are analyzed once for this diagnostic; this reproduces a statistics regime continuous production DML can produce and makes no production auto-analyze claim.

Observed actual command-template SHA-256: `F3F96A56680505CD706AE80D9C7F22484A234B9DD1C7B0A912F18B24B5459715`.

The plan returns exactly 90 daily rows. A totals aggregate and a grouped bounded `unnest` of daily intervals share the same classified population predicate. The planner prunes the account-list-only projections from the nonmaterialized CTE. The daily branch materializes the small joined population once and visits it for each of the 90 intervals. This local plan uses sequential scans and hash joins, with no temporary reads/writes. Top-node execution time was 1.650 ms, with 72 shared-buffer hits and no shared reads. This is appropriate for the measured MVP population; no 10k-account or concurrent-load conclusion is claimed.

## Timing verdict

After 20 warmups, 100 direct handler samples gave p50 2.230 ms, p95 2.442 ms and p99 2.819 ms. The handler p95 is within ADR 0045's 300 ms read/list budget. It includes all account totals and 90 independently calculated Swedish daily boundaries, using a reused scoped DbContext/connection.

Authenticated loopback HTTP is measured separately: two warmups and 20 successful requests through the real socket, session authentication, authorization and rate limiter gave p50 13.218 ms, p95 37.503 ms and p99 47.748 ms. This Development-host experiment includes no TLS and is not the handler's p95. Timing stays observe-only; functional assertions cover successful responses, population parity and bounded output.

Regenerate in PowerShell from the worktree:

```powershell
$env:JBL_ADMIN_OVERVIEW_PERFORMANCE_OUTPUT = 'C:/tmp/admin-overview-performance.txt'
dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class '*AdminAccountOverviewTests' --filter-method '*RetainedMvpPopulation*'
```

The emitted report includes the actual template digest, numerical plan nodes, sample counts, percentiles and limits. A successful MTP run must include a positive `total:` and `failed: 0`.

## Frontend measurement

The production-build admin harness measures `/admin` and a registration-filtered `/admin/anvandare` using the bundled Lighthouse desktop preset at 1280×900, simulated desktop throttling and three-run medians. Account authentication is local and synthetic; the agent does not access signed-in production data. The test reads the unchanged resource budgets from `lighthouserc.json`, alongside the page score, LCP and CLS checks. The directory's account panel is deferred until first opened and kept mounted thereafter so close/focus behavior remains intact. A nested account-route translation provider avoids sending the shared reauthentication catalog with the overview; the existing import-graph equality fitness function verifies both providers.

```powershell
$env:PATH = 'C:/Program Files/Git/usr/bin;' + $env:PATH
$env:ADMIN_HARNESS_PORT_BASE = '3130'
$env:ADMIN_OVERVIEW_SCREENSHOT_DIR = 'C:/tmp/2026-10-08-admin-overview-1978'
pnpm exec playwright test -c playwright.admin.config.ts tests/admin/admin-overview-lighthouse.spec.ts
```

The JSON output records the dated measurements and each unchanged budget. Final frontend results are recorded in the scope verification report after the merged #1977 contract and UI are integrated.

## Cold-cache browser measurement — 2026-10-08 11:45 UTC

The caller owns Chromium and installs the secure synthetic cookie in its default context. Lighthouse resets origin/cache state on each audited target with explicit storage types that exclude cookies. Populated-content preflights run before measurement and then navigate away, so their polling cannot satisfy a measurement's backend-read witnesses. Every run pins the requested/final URL and the required backend reads. Missing or invalid resource observations fail the test. Cleanup runs even when imports, reports or assertions fail.

On base `7b66eb0b82f8878aea7923fb0def74ebfd7caff2`, a verified cold-cache overview first exceeded the existing script budget: 380,878 bytes versus 358,400. The network trace and compiled route manifests identified speculative user-overview chunks fetched through the shared header's brand link. The brand keeps its target; automatic prefetch is disabled while admin navigation is displayed. User navigation and other HeaderStrip consumers retain the default.

The source-delta recheck exited successfully: one Lighthouse test, six measurements, no unhandled harness requests. Both three-run medians were 99 performance, 100 accessibility and 96 best practices, with CLS 0 and TBT 0.

| Route | LCP | Document bytes / budget | Script bytes / budget | Total bytes / budget |
|---|---:|---:|---:|---:|
| `/admin` | 890 ms | 26,291 / 30,720 | 333,666 / 358,400 | 486,276 / 819,200 |
| Registration-filtered `/admin/anvandare` | 901 ms | 30,550 / 30,720 | 336,562 / 358,400 | 527,707 / 819,200 |

Stylesheets, fonts, images and third-party request counts also met the unchanged budgets. Existing header, guest-shell and translation-boundary tests passed 33 cases. The artifact's measurement timestamp is `2026-10-08T11:45:04.899Z`. These are local pre-integration results; the merged #1977 lifecycle contract still requires the combined final check.
