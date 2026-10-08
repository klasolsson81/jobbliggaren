charter=.claude/agents/test-writer.md bytes=17320

## Tests written for the admin account overview (#1978)

**Reviewed head:** d0be61aabe4f386ea8e9c3306cd253c47104db39 (PR #2059).
**Files:** tests/Jobbliggaren.Application.UnitTests/Time/SwedishCalendarTests.cs; tests/Jobbliggaren.Application.UnitTests/Admin/Accounts/GetAccountOverviewQueryHandlerTests.cs; tests/Jobbliggaren.Api.IntegrationTests/Admin/AdminAccountOverviewTests.cs; tests/Jobbliggaren.Api.IntegrationTests/Admin/AdminAccountOverviewAccessTests.cs.
**New cases:** 36 (10 calendar, 6 handler, 7 overview integration, 12 access integration, 1 diagnostic).

**Coverage:** 90 Swedish calendar days, midnight empty interval, both DST transitions, single injected observation time, rollups and period boundaries; retained Identity/profile population, reachable PendingDeletion+Suspended precedence, explicitly unreachable suspended-orphan safe degradation, half-open drill-down filters/pagination/status counts and admitted production hard deletion; HTTP/direct Mediator authorization, role revocation/logout, private/no-store responses, source failure and audit/failed-job observation headers.

**Premise provenance:** AccountRegistrar.OpenAsync, real self-service deletion and admin suspension. Historical incomplete accounts cite AccountRegistrationAtomicityTests' current-writer atomicity pins. Registration timestamps name the clock; the hard-deletion test asserts the production actor's own readiness predicate. The broken orphan is explicitly unreachable and asserts read-side degradation only. No production code or shared stack/database was changed by test-writer.

**Validation (driving-session MTP evidence):** handler total 6 / failed 0; calendar 46 / 0; overview/access 19 / 0 before diagnostic; diagnostic 1 / 0; full admin integration 185 / 0; full Application 20112 / 0; architecture 760 / 0 through hooks. Test execution belongs to the driving session/test-runner under this charter. The driver formatted tests and repaired decimal Actual Rows parsing and the ephemeral loopback binding.

**Performance:** actual raw command captured by session-local PostgreSQL auto_explain ANALYZE/BUFFERS; 201 retained accounts (200 added), 90 output rows, root execution 1.650 ms, 72 shared hits, no shared reads/temp spill. Warm handler p95 2.442 ms (20 warmups/100 samples), within the 300 ms read/list budget. Authenticated loopback HTTP p95 37.503 ms (2 warmups/20 samples) is a separate transport/auth observation. Artifact: artifacts/admin-overview-performance.txt. Real reauth quota remains intact with four suspended fixtures; auto_explain settings reset in finally; output contains only bounded plan metadata/numbers and public SQL-template hash.

**Limits:** local Development host/shared hardware, reused warm DbContext/connection, 200 added accounts with analyzed fixture statistics; no TLS/concurrency/10k-capacity or production auto-analyze claim. Timing is observe-only, with no volatile latency assertion.

**Run with:** dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class "*AdminAccountOverviewTests" --filter-method "*RetainedMvpPopulation*".

**Next step:** tests are GREEN. No unresolved test-writer advisory or escalation. This is test-writer completion, not the final test-runner panel; integration and final review still require the actual #1977 merge.
