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
