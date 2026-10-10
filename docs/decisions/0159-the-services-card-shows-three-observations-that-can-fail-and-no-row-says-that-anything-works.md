# ADR 0159 — The services card shows three observations that can fail, and no row says that anything works

**Date:** 2026-10-10
**Status:** Accepted
**Decider:** Klas Olsson, through his assignment of the services card in issue #1982 (2026-10-10, "Tjänster"-kortet: deliver it, or
say it is not worth a session). `senior-cto-advisor` ruled the design before any code
(`docs/reviews/2026-10-10-1982-services-cto.md`: Q1 the rows, Q2 the mechanism, Q3 the import source, Q4 and Q5 the row
semantics, Q6 the omissions and the fold-in of #2084, Q7 the close, Q8 no GO; its conditions and residuals s1–s7 are this ADR's) ·
a planning review (the Plan agent) tested the draft before the CTO ruled.
**Amends:** ADR 0150, by its Amendment of 2026-10-10 (services) and a dated pointer in its header. D4's #1982 row is flipped in
full, and one D8 row records the handoff's six dotted rows.
**Related:** [#1982](https://github.com/klasolsson81/jobbliggaren/issues/1982) (the issue this ADR ships under; it closes it) ·
[#2084](https://github.com/klasolsson81/jobbliggaren/issues/2084) (the attention card, folded in, D7) ·
[#1972](https://github.com/klasolsson81/jobbliggaren/issues/1972) (the epic) · ADR 0150 (D2: honest unavailability) ·
ADR 0158 (the Server card's in-API sampler: the pattern this follows, and the reason it is not repeated as a shared loop) ·
ADR 0157 (the Backup card: the closed-union and time conventions) · ADR 0154 (§3: every merge is a deploy; §4: what needs Klas's
GO) · ADR 0009 and AGENTS.md §2.1 (ports) · ADR 0023 (the Worker runs the Hangfire server) · CLAUDE.md §9.2 (the read rule)
**Measured against:** `origin/main` at `6a0cc4a31`; the box (`jp-vps`), read-only, 2026-10-10.

> **Provenance.** Written by the driving session from the CTO's ruling (CLAUDE.md §9.2, §13). The rows, the mechanism, the
> import source, the semantics, the omissions, the fold-in and the lapse trigger are the CTO's ruling; the mandatory agents
> review them in the PR. `docs/runbooks/admin-service-observations.md` is the one home for the definitions and for checking them
> against the box; this ADR does not repeat its numbers.

---

## Context

The "Tjänster" card of the `/admin` overview said "Kommer snart" after the Backup (ADR 0157) and Server (ADR 0158) cards became
real. The handoff drew it as six rows with a green or amber dot and a response time each (the VPS, DNS and mail at Strato,
PostgreSQL, the Hangfire worker, JobTech/Platsbanken, the mail send), a footnote "Kontrolleras var 60:e sekund från servern" and
a header line "5 av 6 tjänster fungerar". ADR 0150 D2 forbids a health claim without an observation behind it, and D8 already
records that nothing observed those rows.

Measured at `6a0cc4a31`, and on the box read-only on 2026-10-10 (CLAUDE.md §9.2 (a): names, status, counts, no row):

- The box runs `web`, `api`, `worker`, `caddy`, `redis`, `redis-volatile`, `seq` and `postgres`.
- **A service the request already depends on cannot show a failure on the card that request loads.** Every admin request reads
  Postgres (`AdminRoleAuthorizationHandler`) and the session lives in the persistent Redis (`RedisSessionStore`); the API, the
  web and Caddy are the delivery path itself. A row for any of them could only ever read positive while it was visible.
- The volatile Redis (sign-in challenges, OAuth state, registration claims, rate budgets) is **not** on the admin path. Its
  outage locks every other user out of signing in while the signed-in admin's page keeps working, and nothing watches
  `/api/ready`.
- The API registers a Hangfire **client** only; the Worker runs the Hangfire server (ADR 0023), and its heartbeat is the one
  out-of-band sign that the process lives. A dead Worker leaves the attention card at "no failed jobs".
- The Platsbanken stream job (`*/10 * * * *`) writes the audit row `System.JobAdsSynced` in a `finally`, even for a failed
  run. With ingestion off it returns **before** the audit and writes nothing. Ingestion is on on the box (Klas, 2026-08-17)
  and off by default in the shipped configuration, and `JobTech__IngestEnabled` exists only in the Worker's environment.
- **A recorded run proves less than it seems to.** `PlatsbankenJobSource.StreamChangesAsync` treats any
  `HttpRequestException`, `JsonException` or `IOException` as mid-stream truncation and ends the stream; `JobTechStreamClient`
  turns a non-2xx response into an `HttpRequestException` with `EnsureSuccessStatusCode`. A dead upstream therefore records a
  fresh, empty, **successful** run (`Fetched=0`, `Errors=0`, Hangfire `Succeeded`). Found by reading the code at `6a0cc4a31` and
  reproduced at the source level on 2026-10-10 with a throw-away test (a real `JobTechStreamClient` answering 500, 503 and 403
  under a real `PlatsbankenJobSource` yields no change and throws nothing); that the job then records `Errors=0` follows from its
  code and is not reproduced end to end against a real outage. Filed as
  [#2103](https://github.com/klasolsson81/jobbliggaren/issues/2103); this ADR changes none of that behaviour. The existing pins
  stop short of it (`PlatsbankenStreamTruncationTests`, `SyncPlatsbankenStreamJobTests.RunAsync_WithIngestDisabled_NeverReachesTheJobSource`).

## Decision

**The API samples three sources on a timer, keeps the newest result in memory, and judges it against its own clock when the
card is read. No row is ever "ok".**

### D1 — Three rows, each of which can fail on its own

| Row (wire id) | Source | States |
|---|---|---|
| `backgroundJobs` | The newest heartbeat among the Hangfire server records, read through `JobStorage` | Alive, Silent, NoServer, Unknown |
| `volatileStore` | The already-registered readiness check `redis-volatile`, run through `HealthCheckService` | Reachable, Unreachable, Unknown |
| `jobImport` | The newest `System.JobAdsSynced` stream row of Platsbanken inside 24 h, read from the audit log | Recorded, Overdue, NoneRecorded, Unknown |

**No state means that something works.** A heartbeat is not jobs running; a PONG is not sign-in working; a recorded run is not
JobTech answering. A positive state is written as an observation with its time, in normal ink, like the Backup card, and the
card's words are the page's (`state.ok`/`state.warning` and `AdminServiceStatus.label/detail` are deleted).

**Left out, and why.** Postgres, persistent Redis, the API, the web and Caddy: they serve the card itself, so a row could never
show a failure. Seq: no source, and its loss costs logs, not anything a user does. JobTech upstream: no record carries it (s1),
and an outbound probe would be a new external integration. E-mail: issue #1981 owns it, and the E-post card already says the
capability is unavailable, so there is no e-mail row and no "Saknar verifierad datakälla" row. The card carries no scope line by
default; `design-reviewer` may require one if "Tjänster" reads as complete.

### D2 — Mechanism: an in-API sampler, the pattern of ADR 0158

`ServiceObservationService`, a `BackgroundService` in the Api (`PeriodicTimer`), takes the first sample at once and then one
every `ServiceObservation:SampleIntervalSeconds` (default 30; validated 5–300), inside the same nested `StopHost` guard as
`HostObservationService` (a throwing sampler never faults the host). `ServiceObservationSampler` (Application) reads the three
sources through ports and keeps the newest outcome of each as an immutable snapshot. `GET /api/v1/admin/overview/services`
(`AdminServiceObservationEndpoints`, its own file) reads the snapshot and does no I/O.

- Each source is read inside its own deadline (`ProbeTimeoutSeconds`, default 5) with a `WaitAsync` backstop for a source that
  ignores its token. A source that throws or times out becomes missing for that tick and **only** that row reads Unknown; the
  previous value is never carried forward as new. Only the exception's type name reaches a log line (a driver message can embed
  a connection string).
- **Why a sampler and not a probe per request.** `IMonitoringApi.Servers()` is synchronous, so a request that called it would
  be sync I/O in the request pipeline (AGENTS.md §5) or `Task.Run` around I/O (§3). The endpoint does no I/O, so probe load is
  one tick per interval and not one per poll times open tab.
- The synchronous Hangfire read is bounded by the storage connection's command timeout, which this ADR does not set; it is a
  residual of the existing `AdminBackgroundJobsEndpoints` precedent (reviewed in #204), which this ADR refuses to grow.
- **The two Api samplers are not merged into one loop.** Their ticks differ (one synchronous, one asynchronous); the third
  sampler is what would justify the extraction.
- Composition is the Api's alone. The Hangfire adapter (`HangfireWorkerHeartbeatSource`) and the health-check adapter
  (`ReadinessVolatileStoreProbe`) are in the Api; the SQL source is registered by `AddInfrastructure`, which only the Api calls;
  nothing goes into `AddPersistence` or `AddApplication`, which the Worker loads too. The Worker must not watch itself.
- Options (`ServiceObservation`): 30 s interval, `StaleAfterSeconds` 120 (more than three times the interval, validated at
  start), `ProbeTimeoutSeconds` 5. Every key has a default. The verdict thresholds below are **constants**, not options:
  a setting can be made to disagree with the job it describes, and a test pins each to its source.

### D3 — The Worker row

- **The verdict is the age of the newest evidence of life**, judged at read time against `IDateTimeProvider`: the newest heartbeat
  among the current server records and the newest one this process saw on an earlier tick. **Never a server count**: after a
  kill a stale record and a fresh one coexist until a live server's watchdog removes the old one.
- **Alive** when the newest sign of life is at most 120 s old (Hangfire's default heartbeat of 30 s plus one sampling interval,
  doubled, and under its 5-minute server timeout; a test pins `new BackgroundJobServerOptions()` and that the Worker's
  `Program.cs` sets none of the timings). **Silent**: records exist and the newest heartbeat is older. **NoServer**: no record,
  and no sign of life inside the window, once the sampler has watched for as long as the window; until then the row is Unknown,
  so a release shows no false warning whichever container restarts first. A graceful stop removes the record, and the last sign
  of life it gave stays true, which is why that carried evidence is kept.
- A null or future heartbeat is Unknown. On one clock that is unreachable; the test asserts safe degradation only. Time zone
  and `Kind` of the heartbeat are proven against real Hangfire.PostgreSql storage on a Testcontainers Postgres under a
  `Europe/Berlin` session, never read on the box (CLAUDE.md §9.2 (b)).
- **The label claims what was seen:** "senaste livstecken {tid}". Not "fungerar", not "svarar" (a heartbeat answers nothing).

### D4 — The volatile-store row

The check is run through `HealthCheckService` filtered on `VolatileRedisHealthCheckExtensions.HealthCheckName`, because that
check's own documentation rules out a second public route to the connection. Only Healthy or Unhealthy crosses; a missing
entry or any other status was not a measurement and reads Unknown; the check's description (it carries a latency) never leaves
the API. The row says "svarar" or "svarar inte" and nothing about sign-in working. A test pins that the check is registered under
that name with the `ready` tag, so a rename cannot turn the row into a permanent Unknown.

### D5 — The import row

- **Source.** Raw parameterised SQL in Infrastructure behind the Application port `IImportRunRecords` (`SqlImportRunRecords`;
  precedent `JobAdSnapshotMissTracker`, ADR 0009 axis 3), named for the records so the logs page (#1980) can extend it.
  Filtering a key inside the jsonb payload needs a provider JSON operator, which AGENTS.md §2.1 keeps out of Application, and
  filtering in memory has two bad forms: a `Take()` before the filter can falsely report "none recorded" because other job types
  share the event type, and no `Take()` loads every row of the window. The event type, the window, the source and the job type
  are all in the query; the window bound is a parameter from `IDateTimeProvider`, never `now()`; the projection is `occurred_at`
  and the error count. The event type and the `"stream"` token are one constant (`JobAdsSynced.EventTypeName`,
  `StreamJobType`) that the writers and the readers share.
- **Plan.** The only index on `audit_log` is `ix_audit_log_occurred_at`. The window is what bounds the scan and prunes the daily
  partitions. Measured 2026-10-10 on a Testcontainers `postgres:18`, 200 000 other audit rows over 60 days (a development
  workstation, not the box): with no stream row at all (ingestion off) 3 316 buffer hits and 2.1 ms; with a stream run every ten
  minutes for 60 days 27 buffer hits and 0.6 ms; with 100 000 further rows inside the last 24 h 263 buffer hits and 0.8 ms; each
  an index scan on `occurred_at` per partition inside the window. No index is needed. A needed index would be a STOPP back to the
  CTO. The measurement is regenerated by the throw-away test described in the runbook §8.
- **States.** Recorded: the newest run finished at most 25 minutes ago (two missed runs of `*/10 * * * *` plus five minutes; a
  test pins it to the registrar's cron). Overdue: older. **NoneRecorded is a warning, worded as a fact**: neutral would make a
  stalled import less alarming once it ages past 24 h, which is non-monotone, and the Backup card already shows "no run recorded"
  as a warning while it is expected (ADR 0157 D5). With ingestion off the warning is still true (s2).
- **The error count** is shown only when it is above zero, in the warning form, and never as "0 fel": with a blind upstream a zero
  reads as a clean bill. `Fetched` stays off the wire because its zero is ambiguous.
- **What the row does not know (s1).** A recorded run proves that the Worker ran and recorded the stream job, not that JobTech
  answered. The copy never names Platsbanken's health.

### D6 — The wire

`{ staleAfterSeconds, workerAliveWithinSeconds, importOverdueAfterSeconds, backgroundJobs{state,sampledAt,heartbeatAt},
volatileStore{state,sampledAt}, jobImport{state,sampledAt,lastRunAt,errors} }`. A state with a time carries it and a state
without one carries none; Unknown carries no time at all, so a verdict that could not be judged is withdrawn, never shown as an
old one. The three limits are numbers, so the page words "över 25 minuter" from the API's own value. **The browser never
receives** a server name, queue or worker count, a health-check description, data or exception, a latency, an audit payload,
aggregate id, source or fetched count, a reason code or any free text; a test pins the serialised key set per state.
`X-Admin-Sampled-At` is the API read instant; each row's own `sampledAt` is in the body. The endpoint carries the Admin policy,
`IAdminRequest`, the `admin-read` bucket and `private, no-store` on every path; the BFF read goes through `gated` (#2064).

### D7 — The attention card (folds in #2084)

CTO ruling Q6. This change adds warning states, so the attention card must not read as a general all-clear next to a silent Worker
or a backup that is off. Backup and services lines go into the card together (the card changes whenever a warning-bearing source
joins the overview), each linking to its own card, reusing the existing words; an Unknown or failed source never reads as clear.
This closes #2084.

### D8 — Operations and authority

The release is image only: no compose change, host unit, mount, migration or new dependency. **No GO is needed from Klas**
(ADR 0154 §4: A2 covers a release that changes no deploy file). It is a STOPP back to the CTO if the PR carries a diff under
`deploy/` (for example `IngestEnabled` in the API's environment), a migration, a package, or any change to how
`SyncPlatsbankenStreamJob` or `PlatsbankenJobSource` behave (the constants of D5 change no behaviour). On the box an agent reads
counts and timestamps only; the signed-in card is Klas's (§9.2 (d)).

## Alternatives considered

- **Rows for Postgres and persistent Redis, for completeness.** Rejected (D1): unfalsifiable by construction, a "Fungerar" whatever
  the wording.
- **Dropping the volatile-store row.** Rejected: it is the one store whose outage locks every other user out while the admin's page
  keeps working, and it is cheap. It is the first row to cut if the work had to be cut.
- **A probe per request.** Rejected (D2): it cannot be built without sync I/O in a request or `Task.Run` around I/O.
- **Hangfire's recurring-job state for the import row.** Rejected: `LastJobState` reads Succeeded both for the ingest-off no-op and
  for a dead upstream, and the API reads it synchronously. Strictly less information than the audit row.
- **EF over `AuditLogEntries` with the payload parsed in memory.** Rejected (D5).
- **A neutral NoneRecorded.** Rejected (D5): non-monotone.
- **An e-mail row, or a "Saknar verifierad datakälla" row.** Rejected: #1981 owns e-mail health, and the E-post card already says so.
- **A shared sampler loop for the two Api samplers.** Deferred to the third (D2).

## Consequences

### Positive

- An image-only release; no host artifact, no channel to validate. The sources are ones the API already reaches.
- Each row carries its own state and time, so one failing source never blanks the other two (ADR 0150 D2).
- A dead Worker, a lost volatile Redis and a silent import become visible on the one page the product owner opens, and the attention
  card stops being quiet about them.

### Negative and risks

- **s1.** The import row cannot see a JobTech outage (D5). [#2103](https://github.com/klasolsson81/jobbliggaren/issues/2103) records
  the cause in `PlatsbankenJobSource` (a request failure is recorded as a successful empty run); this ADR does not change that behaviour.
- **s2.** The API cannot read `IngestEnabled`. With ingestion off, the row's warning is still true.
- **s3.** A heartbeat is not jobs running (`WorkerCount = 4` pool exhaustion is a known failure mode); the import row covers one job only.
- **s4.** Detection latency is up to 120 s: a dead Worker can read as Alive for that long. A stopped sampler shows as Unknown after `StaleAfterSeconds`.
- **s5.** One replica: the snapshot and the carried evidence are per process.
- **s6.** The heartbeat and the API share the host's clock only because they run on one box.
- **s7.** A PONG is not sign-in working.
- Every release shows the Worker row as Unknown for the seconds the Worker takes to start, and the live region may announce it.
- A strict response schema on the web means a new state token in the API fails the whole source until the web ships; they deploy together.

**One lapse trigger, one reader.** This decision must be re-examined, and the card treated as unverified, when
`JOBTECH_INGEST_ENABLED` changes on the box, or when the Worker runs as more than one instance or on a host other than the API's.
Both are box writes that need Klas's GO (ADR 0154 §4), and Klas is the reader. What to re-run is
`docs/runbooks/admin-service-observations.md` §8.

## Implementation

One vertical slice in one PR. The backend: `Application/Admin/ServiceObservations/` (the ports, the sampler, the pure evaluator, the
closed DTO and the query), `Api/Endpoints/AdminServiceObservationEndpoints.cs`, `Api/Hosting/ServiceObservation{Service,Registration}.cs`,
`Api/BackgroundJobs/HangfireWorkerHeartbeatSource.cs`, `Api/HealthChecks/ReadinessVolatileStoreProbe.cs`,
`Infrastructure/Admin/Services/SqlImportRunRecords.cs`, and the shared constants on `JobAdsSynced`. The frontend: the response schema,
the loader's gated read, the card and the attention lines. Tests: the evaluator at every boundary, the sampler's faults, the wire shape,
the Hangfire adapter against real storage, the SQL source against a real audit log written by the real auditor with a counterfactual per
filter, the endpoint's access and privacy, the composition and the pins on the cron and Hangfire's defaults. There is no migration,
dependency or deploy file.

## References

- `docs/runbooks/admin-service-observations.md` · `docs/reviews/2026-10-10-1982-services-cto.md`
- Code: `src/Jobbliggaren.Application/Admin/ServiceObservations/` · `src/Jobbliggaren.Api/Hosting/ServiceObservationService.cs` ·
  `src/Jobbliggaren.Api/Endpoints/AdminServiceObservationEndpoints.cs` · `src/Jobbliggaren.Infrastructure/Admin/Services/SqlImportRunRecords.cs`
- ADR 0150, 0157, 0158, 0154, 0023, 0009 · AGENTS.md §2.1, §3, §5 · CLAUDE.md §9.2
