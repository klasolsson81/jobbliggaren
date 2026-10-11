# Runbook — Admin service observations: Worker, volatile store and import (#1982, Services sub-track)

The "Tjänster" card of the `/admin` overview shows three observations of the stack the API can reach for itself. This runbook is
the one home for what each row means, where it comes from, how it can be wrong and how to check it against the real box. The
decision and its alternatives are ADR 0159. The Backup card (`host-observations.md`, ADR 0157) and the Server card
(`admin-host-observations.md`, ADR 0158) are different mechanisms: this one has no host script, no mount, no unit and no compose
change.

## 1. Mechanism in one paragraph

A `BackgroundService` inside the API process (`ServiceObservationService`) calls the sampler once at start and then every
`ServiceObservation:SampleIntervalSeconds` (default 30 s). Each tick reads three sources: the Hangfire server records through `JobStorage` (`HangfireWorkerHeartbeatSource`,
synchronous), the registered readiness check `redis-volatile` through `HealthCheckService` (`ReadinessVolatileStoreProbe`), and the
newest stream run in the audit log by one parameterised SQL query (`SqlImportRunRecords`); the two asynchronous reads are each
inside their own deadline (`ProbeTimeoutSeconds`, default 5 s). The newest outcome of each is held in memory as an
immutable snapshot. `GET /api/v1/admin/overview/services` never touches a source: it reads that snapshot, judges it against the
API's clock, stamps the read time and answers. The browser asks for nothing; it never supplies a path, a command or an interval,
and the check name and the audit event type are code constants.

## 2. What each row means

No row is ever "ok" or "fungerar". A positive state is an observation with its time.

| Row | A positive state means | It does **not** mean |
|---|---|---|
| **Worker** (`backgroundJobs`) | The newest Hangfire server heartbeat is at most 120 s old. The card says "senaste livstecken {tid}". | That jobs run. A heartbeat is written from the server's own timer; a Worker with all four job slots stuck still beats (the known `WorkerCount = 4` exhaustion). The import row is the indirect signal. |
| **Tillfälligt lager** (`volatileStore`) | The volatile Redis answered its readiness check (a PING) at the sample time. | That sign-in works (it also needs mail), or that a write would succeed (a `maxmemory` with `noeviction` still answers PING). |
| **Annonsimport** (`jobImport`) | The audit log holds a stream run of Platsbanken that finished at most 25 minutes ago. | That JobTech answered. A failed request ends the stream quietly and is recorded as an empty, successful run (ADR 0159 s1, issue #2103). |

**Worker states.** Alive (newest sign of life ≤ 120 s old), Silent (a record exists and its newest heartbeat is older), NoServer (no
record, no sign of life inside the window, and the sampler has read the storage successfully for as long as the window, counted
from its first successful read at the sample's own time), Unknown (the source could not be
read; the sample is too old; a time is more than 5 s in the future; or the sampler has read the storage for less than the window and
there is nothing to see). The verdict is the age of the **newest** heartbeat, **never** a count of servers: after a
SIGKILL a stale record and a fresh one coexist until a live server's watchdog removes the old one. A graceful stop removes the
record at once, and the last sign of life it gave stays true: the sampler keeps the newest heartbeat it has seen, so a release
reads "last sign of life 20 s ago" and not a warning. A release's few seconds without a server read as Unknown until the sampler
has read the storage for 120 s.

**Import states.** Recorded (≤ 25 min), Overdue (older, inside 24 h), NoneRecorded (nothing inside 24 h: ingestion off, or the job
has not run for a day), Unknown. NoneRecorded is a warning, worded as a fact; it is also what the shipped default (ingestion off,
`JobTech:IngestEnabled=false`) reads, and the API cannot tell the two apart (s2). The error count is shown only above zero; "0 fel"
is never printed.

**The limits are constants, not options**, and each is pinned to its source by a test: 120 s to Hangfire's default heartbeat (30 s)
plus one sampling interval, doubled, and under its 5-minute server timeout (the Worker sets none of the timings); 25 minutes to the
registered cron `*/10 * * * *` (two missed runs and five minutes); the 24 h window.

## 3. Time, states and what the browser gets

- The response is `{ staleAfterSeconds, workerAliveWithinSeconds, importOverdueAfterSeconds, backgroundJobs{state,sampledAt,heartbeatAt},
  volatileStore{state,sampledAt}, jobImport{state,sampledAt,lastRunAt,errors} }`. A state with a time carries it and one without
  carries none; **Unknown carries no time at all**, so a verdict that could not be judged is withdrawn and never shown as an old
  one. There is no reason, server name, queue, worker count, health-check text, latency, audit payload, source, fetched count or
  free text. Why a source failed stays on the server, in log events 6230 (source failed, the exception's type name only), 6231
  (deadline exceeded) and 6232 (an unexpected tick failure).
- `sampledAt` is the injected wall clock read once after the source answered; `X-Admin-Sampled-At` is the API read instant. They are
  different instants: a row can be old although it was just read. A sample older than `StaleAfterSeconds` (120 s) when read, or dated
  more than 5 s ahead of the read, is Unknown.
- The endpoint shares the per-user `admin-read`
  bucket (30 per 10 s).

## 4. Cadence and cost

| Setting | Default | Notes |
|---|---|---|
| `ServiceObservation:SampleIntervalSeconds` | 30 | 5–30: the alive window (120 s) holds a heartbeat interval and a sampling interval, twice. The browser polls the overview every 60 s while visible. |
| `ServiceObservation:StaleAfterSeconds` | 120 | More than three times the interval, validated at start. |
| `ServiceObservation:ProbeTimeoutSeconds` | 5 | 1–30. The volatile check bounds itself at 1.5 s; this catches a hung connection. |

Per tick: one synchronous read of the Hangfire server table, one Redis PING, one indexed range read of the audit log. The synchronous
Hangfire read is bounded by the storage connection's timeout and is the same kind of read as `AdminBackgroundJobsEndpoints`
already makes per request. The read endpoint does no I/O.

## 5. Local development

On a developer machine without a Hangfire schema the Worker row reads Unknown, and with no volatile Redis the store row reads
Unreachable or Unknown; the import row reads NoneRecorded until a stream run is recorded. Nothing is invented, and the log carries
event 6230 once per tick for a source that cannot be read.

## 6. Security boundaries

Admin policy and `IAdminRequest` apply: anonymous 401, ordinary user 403, a revoked admin is refused on the next call, direct
Mediator use is refused. No SSH key, Docker socket, host mount or extra Linux capability is involved. The Hangfire read goes
through `JobStorage` because the `hangfire` schema is closed to every other role (`hangfire-schema.md` §4). The audit read returns
a timestamp and a count; nothing of the payload leaves the query. Nothing is retained beyond the newest snapshot in memory.

## 7. Checking the real values against the box

Under the read rule of CLAUDE.md §9.2: counts and timestamps only, no row of `hangfire` or `audit_log`, no signed-in request to the
application, no log lines. The signed-in card is Klas's.

1. Containers: `ssh jp-vps 'sudo docker ps --format "{{.Names}} {{.Status}}"'`. `jobbliggaren-worker` and `jobbliggaren-redis-volatile`
   should read `(healthy)`.
2. Worker: **aggregates only**, run as the database superuser inside the Postgres container (the connection line is the one in
   `vps-deploy-stack.md`): `SELECT count(*), extract(epoch FROM now() - max(lastheartbeat))::int FROM hangfire.server;`. The age
   should be under 120 s and the card's "senaste livstecken" within a sampling interval of it. After a release the count is 1.
3. Import: `SELECT extract(epoch FROM now() - max(occurred_at))::int FROM audit_log WHERE event_type = 'System.JobAdsSynced' AND
   occurred_at >= now() - interval '24 hours' AND payload->>'JobType' = 'stream';` should be under 25 minutes while ingestion is on,
   and the card's "senaste körning" should match it.
4. Klas opens `/admin` signed in and reads the card.

## 8. Measured cost

Dated one-off measurements, not budgets; re-run the commands before quoting them.

**The import query's plan, 2026-10-10, Testcontainers `postgres:18` on a development workstation (not the box), 200 000 other audit rows
over 60 days.** With no stream row at all (ingestion off) 3 316 buffer hits and 2.1 ms; with a stream run every ten minutes for 60 days
27 buffer hits and 0.6 ms; with 100 000 further rows inside the last 24 h 263 buffer hits and 0.8 ms. Each was an index scan on
`ix_audit_log_occurred_at` per daily partition inside the window. No index is needed; a needed index is a STOPP (ADR 0159 D5).

To regenerate, on a scratch migrated database (a Testcontainers clone or a local dev database, never the box), run the three steps and
read the plan of the last statement after each:

```sql
INSERT INTO audit_log (id, occurred_at, correlation_id, event_type, aggregate_type, aggregate_id)
SELECT gen_random_uuid(), now() - (random() * interval '60 days'), gen_random_uuid(),
       'Application.Submitted', 'Application', gen_random_uuid()
FROM generate_series(1, 200000);                                                    -- step A
INSERT INTO audit_log (id, occurred_at, correlation_id, event_type, aggregate_type, aggregate_id, payload)
SELECT gen_random_uuid(), now() - (g * interval '10 minutes'), gen_random_uuid(),
       'System.JobAdsSynced', 'System.JobAdSync', gen_random_uuid(),
       '{"Source":"Platsbanken","JobType":"stream","Errors":0,"Fetched":3}'::jsonb
FROM generate_series(0, 8640) g;                                                    -- step B
ANALYZE audit_log;
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF)
SELECT occurred_at, CASE WHEN (payload->>'Errors') ~ '^[0-9]{1,9}$' THEN (payload->>'Errors')::int END
FROM audit_log
WHERE event_type = 'System.JobAdsSynced' AND occurred_at >= now() - interval '24 hours'
  AND (payload->>'Source') = 'Platsbanken' AND (payload->>'JobType') = 'stream'
ORDER BY occurred_at DESC LIMIT 1;
```

Step C is step A again with `now() - (random() * interval '24 hours')` and `generate_series(1, 100000)`. Run `ANALYZE` before each plan.
