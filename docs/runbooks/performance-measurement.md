# Runbook — measuring Jobbliggaren against the ADR 0045 budgets

> **Purpose:** answer the question *"is anything over budget?"* without hand-writing a query
> each time. This runbook is the operational face of
> [ADR 0045](../decisions/0045-performance-budget-and-fitness-functions.md) — the budgets
> themselves live there and are **not restated here** (two sources of truth is how budgets
> drift). This file tells you how to *read* them.
>
> Every mechanism here is **observe-only** (ADR 0045 Beslut 5). Nothing in this runbook
> blocks CI. Flipping any of it to blocking is a Klas ratchet (Beslut 6) — see §E.

**Sections**

- [Prerequisites](#prerequisites)
- [§A — Handler p95 vs budget](#a--handler-p95-vs-budget)
- [§B — Worker memory trend](#b--worker-memory-trend)
- [§C — Ingestion throughput](#c--ingestion-throughput)
- [§D — Per-query EF measurement session](#d--per-query-ef-measurement-session)
- [§E — What to do when a budget is exceeded](#e--what-to-do-when-a-budget-is-exceeded)

---

## Prerequisites

Structured logs go to **Seq** (TD-104 / Pre-4 STEG 6). The sink attaches only when
`Seq:ServerUrl` is configured — without it, both hosts stay console-only and none of the
queries below have anything to run against.

```bash
docker compose up -d seq          # http://localhost:5341
```

Dev already sets `Seq:ServerUrl=http://localhost:5341` in both hosts'
`appsettings.Development.json`. Run the Api and the Worker, generate traffic (or let a sync
run), then query in the Seq UI.

> **Query on `@MessageTemplate`, never on `EventId`.** EventIds are **not unique** across this
> codebase — 5601/5602, 5701–5703 and 6001/6002 are each used by two or three unrelated
> classes. A signal keyed on an EventId will silently match events from a job you were not
> asking about. The message template is the stable key.

---

## §A — Handler p95 vs budget

`LoggingBehavior` (`src/Jobbliggaren.Application/Common/Behaviors/LoggingBehavior.cs`)
emits `Handled {MessageName} in {ElapsedMs}ms` for **every** Mediator message — this is
ADR 0045's own declared measuring point (Beslut 1: "server-side handler-latens ... det
`LoggingBehavior` redan instrumenterar"), aggregated nowhere until now.

```sql
-- p95 per handler, slowest first. Set the Seq time-range picker to 1h / 24h as needed.
select @Properties['MessageName'] as MessageName,
       percentile(ElapsedMs, 95) as p95_ms,
       count(*) as n
from stream
where @MessageTemplate = 'Handled {MessageName} in {ElapsedMs}ms'
group by @Properties['MessageName']
order by p95_ms desc
```

Map the result's `MessageName` to an ADR 0045 Beslut 1 class before judging it:

| MessageName pattern | Class | p95 budget |
|---|---|---|
| `*JobAdsQuery`, `*ListQuery`, `Run*SearchQuery` | (a) read-query/list | 300 ms |
| Typeahead/suggest queries (SuggestPolicy 30/10s, ADR 0042) | (b) typeahead/suggest | 150 ms |
| `*Command` (CQRS write handlers) | (c) command/write | 400 ms |

A handler that does not fit one of these rows is not budgeted by ADR 0045 Beslut 1 —
report it, do not invent a number for it (same discipline as the rest of this runbook).

Read `n` alongside `p95_ms`: a handler invoked twice in the window has a meaningless
percentile. Widen the window before concluding anything — §E point 1 applies here too.

---

## §B — Worker memory trend

`WorkerMemoryTrendService` (`src/Jobbliggaren.Worker/Hosting/WorkerMemoryTrendService.cs`)
samples the Worker process every `WorkerMemoryTrend:SampleIntervalSeconds` (default 60s,
`src/Jobbliggaren.Application/Common/Telemetry/WorkerMemoryTrendOptions.cs`) and emits
`WorkerMemoryTrend` at Information every tick, plus an edge-triggered
`WorkerMemoryAboveSoftCap` (Warning) / `WorkerMemoryBackWithinSoftCap` (Information) pair
on the below↔above-cap transition (ADR 0045 Beslut 3, 512 MiB soft cap).

```sql
-- Chart workingSetBytes over a run.
select @Timestamp, @Properties['workingSetBytes'] as workingSetBytes,
       @Properties['gcHeapBytes'] as gcHeapBytes, @Properties['gen2Collections'] as gen2Collections
from stream
where @MessageTemplate = 'WorkerMemoryTrend: workingSetBytes={WorkingSetBytes}, gcHeapBytes={GcHeapBytes}, gen2Collections={Gen2Collections}.'
order by @Timestamp asc
```

```sql
-- Edge transitions only (breach + recovery) — a much shorter list than the full trend.
select @Timestamp, @MessageTemplate, @Properties['workingSetBytes'] as workingSetBytes
from stream
where @MessageTemplate like 'WorkerMemoryAboveSoftCap:%' or @MessageTemplate like 'WorkerMemoryBackWithinSoftCap:%'
order by @Timestamp asc
```

**No per-job attribution — read this before asking "which job caused this."**
`Environment.WorkingSet` is a **process** measure. `WorkerCount = 4` means up to four
Hangfire jobs share the process at once; the working set is their sum plus the host
baseline. There is no honest in-process attribution of a byte count to one job instance
(see the dated ADR 0045 Beslut 3 amendment for the full reasoning). The event therefore
carries no JobId/JobName field, by design — do not add one without solving the
attribution problem first.

**Correlate to a specific run by time window**, not by field, against the sync jobs' own
events:

```sql
-- Stream job start/complete. Query on @MessageTemplate, never EventId — 5301/5302 are
-- this job's own, but EventIds are not unique elsewhere in this codebase (see the
-- prerequisites note above).
select @Timestamp, @MessageTemplate, @Properties
from stream
where @MessageTemplate like 'SyncPlatsbankenStreamJob:%'
order by @Timestamp asc

-- Snapshot job start/complete — the long-running, higher-risk one for OOM.
select @Timestamp, @MessageTemplate, @Properties
from stream
where @MessageTemplate like 'SyncPlatsbankenSnapshotJob:%'
order by @Timestamp asc
```

Overlay the two charts by timestamp: a working-set ramp that tracks the snapshot's
started→completed window and falls back afterward is the expected shape. A ramp that does
**not** fall back after the snapshot completes is the ADR 0032-class regression this
instrument exists to catch.

A rising `gen2Collections` count *together with* a rising `workingSetBytes` is the ADR
0032 memory-pressure signature — distinct from a large-but-flat working set, which is
more likely a steady-state cache (the taxonomy singleton, ADR 0043; the skill-taxonomy
index).

---

## §C — Ingestion throughput

`IngestionThroughputReporter`
(`src/Jobbliggaren.Application/JobAds/Jobs/Common/IngestionThroughputReporter.cs`) is
called by both Platsbanken sync jobs after a run completes and emits `IngestionThroughput`
(Information, the trend series) plus `IngestionThroughputBelowFloor` (Warning) when the
rate falls under the ADR 0045 Beslut 1 klass (d) floor (200 jobb/min, `IngestionThroughput`
config section).

```sql
-- Throughput trend, both jobs — one byte-identical template matches both.
select @Timestamp, @Properties['source'] as source, @Properties['jobType'] as jobType,
       @Properties['fetched'] as fetched, @Properties['durationSec'] as durationSec,
       @Properties['itemsPerMinute'] as itemsPerMinute
from stream
where @MessageTemplate = 'IngestionThroughput: source={Source}, jobType={JobType}, fetched={Fetched}, durationSec={DurationSec}, itemsPerMinute={ItemsPerMinute}.'
order by @Timestamp desc
```

```sql
-- Below-floor warnings only.
select @Timestamp, @Properties
from stream
where @MessageTemplate like 'IngestionThroughputBelowFloor:%'
order by @Timestamp desc
```

**What "qualifying" means — read this before wondering where a run's rate went.**
A run only gets a verdict (a logged `itemsPerMinute`, warn or not) if it *qualifies*:
`fetched >= 200 (MinItemsForVerdict) AND durationSec > 0`. A run that fetched fewer than
200 items — e.g. a quiet 10-minute stream cron at 03:00 on a Sunday with 3 changed ads —
emits **nothing**: no `IngestionThroughput` event, no `itemsPerMinute` field anywhere.

**This silence is deliberate, not a gap — do not "fix" it by logging `itemsPerMinute=0`
or similar.** A logged rate is a claim about capacity. Fewer than 200 observed items
cannot support a jobs/min claim (it is extrapolation, not measurement), and a fabricated
`itemsPerMinute` on a healthy quiet run is *exactly* the number someone will chart six
months from now, where it will look like an outage. The raw `fetched`/`durationSec`
values are already visible on the jobs' own `LogCompleted` events (5302/5402) — compute a
rate from those directly if you need one for a specific non-qualifying run, in full view
of how small the sample is.

The stream job (10-min cron, 15-min overlap window, ADR 0032 §3) is **demand-limited**,
not capacity-limited — it processes whatever JobTech changed, never a backlog. It will
therefore rarely qualify, and that is correct: a throughput floor is a capacity claim, and
applying it to a demand-limited workload would be a category error. When the stream job
*does* qualify (≥ 200 changes in one 15-minute window) it can still warn, and that is the
one case worth taking seriously — a capacity-limited stream run that is also slow is the
ADR 0032 rate-limiter/streaming regression class ADR 0045 exists to catch.

---

## §D — Per-query EF measurement session

### Why this is off by default

EF Core logs **one `Executed DbCommand` event per SQL statement** at Information. That is a
useful instrument and an unusable default:

- The Platsbanken snapshot upserts item by item, with a **child DI scope per item** (ADR 0032
  §5) — roughly 47 000 items per run. At Information, a single sync buries the log under
  **100 000+** statement events.
- The same child-scope shape emits ~47 000 **`ContextInitialized`** events, which live in a
  *different* category (`Microsoft.EntityFrameworkCore.Infrastructure`). Silencing only
  `Database.Command` halves a flood that has two sources.

So both categories ship at `Warning` in both hosts' base `appsettings.json` (#752, perf-audit
finding `g2`), and both survive into Development. Pinned by
`tests/Jobbliggaren.Architecture.Tests/EfCoreLoggingConfigurationTests.cs`, which runs the
shipped config through the real MEL filter engine.

**Failed SQL still reaches the log.** `CommandExecuted` is Information; `CommandError` is
**Error**. Warning silences the success chatter, not the failures.

### Turning it on for a session

Both hosts load a gitignored `appsettings.Local.json` last, so a measurement session needs no
committed change and cannot leak into anyone else's environment:

```jsonc
// src/Jobbliggaren.Worker/appsettings.Local.json   (or .../Jobbliggaren.Api/)
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

MEL resolves a category by **longest matching prefix**, so this re-enables exactly the
per-query duration signal and leaves the `ContextInitialized` flood silenced.

In a container, use the environment-variable form — and note the footgun:

```bash
Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information
```

Colons become `__`. **The dots stay dots.** The reflex to convert the dots to underscores as
well produces a key that binds to nothing and fails silently.

### What to query

```sql
-- Per-query duration, slowest first. Requires the override above.
select count(*) as n, percentile(elapsed, 95) as p95_ms, max(elapsed) as max_ms
from stream
where @MessageTemplate like 'Executed DbCommand%'
group by @Properties['commandText']
order by p95_ms desc
```

Read the result against ADR 0045 Beslut 1 — but read it as a *component* cost, not a verdict:
the budgets are stated per **handler** (server-side handler latency, which is what
`LoggingBehavior` measures), not per statement. A slow statement is a lead, not a breach.

### Where a measurement session may run

> **Loopback Seq only.** Run a measurement session against your local
> `Seq:ServerUrl=http://localhost:5341` and nowhere else.

Never enable the per-query signal in staging, in production, or against any shared
`Seq:ServerUrl`. Production points at the self-hosted EU Seq (ADR 0050), and turning this on
there would stream every statement's SQL text into a sink whose retention and access controls
are still open work. The env-var form below exists for a **local container**, not for a
deployed one.

### Turning it off

Delete the `appsettings.Local.json` entry (or unset the env var) and restart the host. On
loopback, leaving it on for a short session is harmless; it will still drown the next sync you
run.

Note the provider-scoped variant, because it hides itself: `Logging:Seq:LogLevel:<category>`
turns a category up **only for the Seq sink**, leaving the console quiet. Someone can enable
the flood without seeing any sign of it in the terminal they are watching. If Seq looks noisy
and the console does not, look there.

### PII guard-rail — read before you widen the logging

> **Redaction protects parameter *values*. It does not protect anything inlined as a
> *literal*, which is logged verbatim in the command text.**

Seq stores the full `commandText` (the §D query above groups by it). EF redacts parameters by
default (`@p0='?'`) and that redaction is load-bearing — these statements carry CV content,
parsed CV text, e-mail addresses and tokens, all of which CLAUDE.md §5 forbids logging in
plaintext. **A measurement session needs durations, not values.**

`EnableSensitiveDataLogging` is the obvious way to defeat that, and it is forbidden. It is not
the only way, and the others are easier to reach from *this* document:

| Do not | Why it defeats redaction |
|---|---|
| `EnableSensitiveDataLogging` | Logs every parameter value verbatim. Not config-bindable (a code call) — keep it that way. |
| **`TranslateParameterizedCollectionsToConstants`** | An EF **performance** option — one search away for anyone reading a **performance** runbook. It inlines every element of a collection as a SQL literal. Our collections carry **organisation numbers**, and for an enskild firma the organisation number **is a personnummer** — §5's highest-priority red line, reached without touching `EnableSensitiveDataLogging` at all. |
| `EF.Constant(...)` | Forces a value to be inlined as a literal instead of parameterised. |
| `FromSqlRaw($"...")` with interpolation | Bakes the interpolated value into the command text. (There are none in `src/` today. Keep it that way.) |

If you want parameter values in order to reproduce a query, reproduce it against **seeded**
data instead. Never widen the logging to get them.

---

## §E — What to do when a budget is exceeded

1. **Confirm it is real.** One slow sample is not a breach; the budgets are p95 (ADR 0045
   Beslut 1). Widen the window and re-run the query before concluding anything.
2. **Fix the regression, or write down why it is acceptable.** CLAUDE.md §2.5: regressing
   against budget requires a fix or a STOPP justification. It is the same discipline as
   lowered test coverage.
3. **Never silently raise the budget.** A cap change is a **dated in-file amendment to ADR
   0045**, reviewed like any other architectural decision. Bumping the number in config
   because the measurement exceeded it is the Goodhart move a fitness function exists to
   prevent — the instrument would then be measuring its own tolerance.
4. **Ratcheting observe-only → blocking** (Beslut 6) is a Klas decision and requires a stable
   distribution over several green runs on consistent hardware. `docs/runbooks/e2e-ci.md`
   documents the same ratchet lever for the E2E workflow.
