# Admin overview performance verification — 2026-10-08

Scope: #1978, the account overview handler and its authenticated endpoint. Measurements are a dated local experiment, not a claim about production capacity. The combined backend experiment ran on HEAD b9c9e10f16a9aea1eac22eea49c1afacf9d8a05f and actual merged #1977 base 0b163d1b48fafe74a60ea497392142a461aa449f; its artifact was written at 2026-10-08T18:26:13.1469055Z.

## PostgreSQL query plan

The integration probe runs the actual `SqlAccountDirectory` command against migrated PostgreSQL in an isolated Testcontainer. Session-local `auto_explain` records JSON ANALYZE/BUFFERS for the raw Npgsql aggregate; an EF interceptor would not observe this command. No production logging/configuration is changed. The report projects only plan-node names and numerical counters, never account identifiers or SQL parameter values.

The fixture adds 200 retained accounts through production registration/deletion/suspension actors, with registrations across 90 Swedish calendar days. Twenty historical incomplete rows cite the current registration atomicity test's writer pin. The ordinary serial Admin suite left 82 retained synthetic baseline accounts; the probe added 200, producing an observed population of 282. Tables are analyzed once for this diagnostic; this reproduces a statistics regime continuous production DML can produce and makes no production auto-analyze claim.

Observed actual command-template SHA-256: `00ABAE36070B05B9909861B434D22DC2A90CE4F4933274AAEC598B4462BE6FD6`.

The plan returns exactly 90 daily rows. A totals aggregate and a grouped bounded `unnest` of daily intervals share the same classified population predicate. The planner prunes the account-list-only projections from the nonmaterialized CTE. The daily branch materializes the small joined population once and visits it for each of the 90 intervals. This local plan uses sequential scans and hash joins, with no temporary reads/writes. Top-node execution time was 2.288 ms, with 94 shared-buffer hits and no shared reads. This is appropriate for the measured MVP population; no 10k-account or concurrent-load conclusion is claimed.

## Timing verdict

After 20 warmups, 100 direct handler samples gave p50 2.646 ms, p95 3.163 ms and p99 3.821 ms. The handler p95 is within ADR 0045's 300 ms read/list budget. It includes all account totals and 90 independently calculated Swedish daily boundaries, using a reused scoped DbContext/connection.

Authenticated loopback HTTP is measured separately: two warmups and 20 successful requests through the real socket, session authentication, authorization and rate limiter gave p50 10.357 ms, p95 12.335 ms and p99 38.554 ms. This Development-host experiment includes no TLS and is not the handler's p95. Timing stays observe-only; functional assertions cover successful responses, population parity and bounded output.

Regenerate in PowerShell from the worktree after the process-environment licence setup in [local-dev-setup.md](../runbooks/local-dev-setup.md#imagesharp-build-licence-1979-pr2). Retain normal licence validation; never echo credentials or include them in arguments or evaluated binlogs:

```powershell
$env:JBL_ADMIN_OVERVIEW_PERFORMANCE_OUTPUT = 'C:/tmp/admin-overview-performance.txt'
dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class '*AdminAccountOverviewTests' --filter-method '*RetainedMvpPopulation*'
```

The emitted report includes the actual template digest, numerical plan nodes, sample counts, percentiles and limits. A successful MTP run must include a positive `total:` and `failed: 0`.

## Frontend measurement

The production-build admin harness measures `/admin` and a registration-filtered `/admin/anvandare` using the bundled Lighthouse desktop preset at 1280×900, simulated desktop throttling and three-run medians. Account authentication is local and synthetic; the agent does not access signed-in production data. The test reads the unchanged resource budgets from `lighthouserc.json`, alongside the page score, LCP and CLS checks. The directory's account panel is deferred until first opened and kept mounted thereafter so close/focus behavior remains intact. A nested account-route translation provider avoids sending the reauthentication catalog with the overview. It selects settings.account.reauth alongside the required shared catalogs. The static import graph verifies every full client translation path is covered and every declared namespace is used; a selected subtree cannot satisfy a sibling read.

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

## Actual merged #1977 base — 2026-10-08 18:54 UTC

The combined source uses actual main 0b163d1b48fafe74a60ea497392142a461aa449f, including #1977's deletion lifecycle, legal copy, feedback race repair and error/focus behavior. Ordinary production build and both authenticated Lighthouse cases passed: two tests, nine audited navigations, exact URLs and backend-read witnesses, no unhandled harness request. The overview/drill-down artifact timestamp is 2026-10-08T18:54:42.897Z.

The first combined directory measurements exceeded the unchanged document budget: 31,061 bytes unfiltered and 31,199 with registration dates, against 30,720. The delivered repair selects only settings.account.reauth at the existing account-route provider, while retaining the shared admin catalog reference and all existing messages. The reauthentication component reads that same branch explicitly. Source/runtime import-graph checks retain the top-level consumer contract and additionally verify full translation-path coverage; server-only descendants remain excluded. No copy, resource budget, dependency or lockfile changed.

| Route | LCP | Document bytes / budget | Script bytes / budget | Total bytes / budget |
|---|---:|---:|---:|---:|
| `/admin` | 892 ms | 27,079 / 30,720 | 333,862 / 358,400 | 485,325 / 819,200 |
| Registration-filtered `/admin/anvandare` | 898 ms | 28,992 / 30,720 | 337,212 / 358,400 | 530,471 / 819,200 |

Both three-run medians remain 99 performance / 100 accessibility / 96 best practices, CLS 0 / TBT 0. The separately audited unfiltered directory document is 28,858 bytes with the same 337,212 script bytes. Stylesheets, fonts, images, total transfers and third-party counts pass their unchanged budgets. These are local synthetic measurements; publication and Klas's signed-in acceptance remain separate delivery steps.

## Committed review corrections — 2026-10-08 19:36 UTC

The fresh ordinary production build at 19:33:31 UTC contains the source committed as `7f7e2e8c216507601ef2e01ae796125f6e257982`, on actual #1977 base `0b163d1b48fafe74a60ea497392142a461aa449f`. All 96 combined overview, admin-route, deletion and Lighthouse browser cases passed without retries or changed timeouts. The corrections reconcile same-page directory navigation, enlarge introduced drill-down targets and constrain long stored audit codes. No backend source/test, translation, dependency or budget changed.

The current overview/drill-down artifact timestamp is `2026-10-08T19:36:01.110Z`; exact URLs, backend-read witnesses, complete finite resource observations and zero unhandled harness requests passed. Both three-run medians are 99 performance / 100 accessibility / 96 best practices, CLS 0 / TBT 0.

| Route | LCP | Document bytes / budget | Script bytes / budget | Total bytes / budget |
|---|---:|---:|---:|---:|
| `/admin` | 902 ms | 27,093 / 30,720 | 333,884 / 358,400 | 486,479 / 819,200 |
| Registration-filtered `/admin/anvandare` | 899 ms | 28,989 / 30,720 | 337,319 / 358,400 | 527,840 / 819,200 |

The separate unfiltered directory case and all stylesheet, font, image and third-party budgets passed. The 31 current state renders include 1280, 1920 and 3440 px layouts, readable full long codes/references at 1024/1280/3440, and native browser 200% zoom (zoom 2, DPR 2, CSS viewport/scroll width 640). Computed target floors, non-overlap, keyboard drill-down and axe checks passed. Original defect captures are retained separately. These remain local synthetic observations, distinct from release and signed-in acceptance.
