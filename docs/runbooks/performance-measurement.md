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
select @Timestamp, @Properties['WorkingSetBytes'] as workingSetBytes,
       @Properties['GcHeapBytes'] as gcHeapBytes, @Properties['Gen2Collections'] as gen2Collections
from stream
where @MessageTemplate = 'WorkerMemoryTrend: workingSetBytes={WorkingSetBytes}, gcHeapBytes={GcHeapBytes}, gen2Collections={Gen2Collections}.'
order by @Timestamp asc
```

```sql
-- Edge transitions only (breach + recovery) — a much shorter list than the full trend.
select @Timestamp, @MessageTemplate, @Properties['WorkingSetBytes'] as workingSetBytes
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

A rising `Gen2Collections` count *together with* a rising `WorkingSetBytes` is the ADR
0032 memory-pressure signature — distinct from a large-but-flat working set, which is
more likely a steady-state cache (the taxonomy singleton, ADR 0043; the skill-taxonomy
index).

### When the trend series goes silent

The sampler is deliberately unable to fault the Worker (a telemetry component that can kill
the process it monitors is worse than no telemetry). So it fails *quietly*, and a missing
trend line is a real possibility rather than an impossible one. Two events tell you which:

```sql
select @Timestamp, @MessageTemplate, @Exception
from stream
where @MessageTemplate like 'WorkerMemoryTrendSampler:%'   -- probe or sink failure, per tick
   or @MessageTemplate like 'WorkerMemoryTrendService:%'   -- unexpected tick failure
order by @Timestamp desc
```

If **neither** appears and the trend is still absent, the hosted service never started — check
for an `OptionsValidationException` at Worker boot (`WorkerMemoryTrend:SampleIntervalSeconds`
must be 1–3600). Also note the first sample lands at **t + one interval**, not at t0: a Worker
that has been up for less than a minute has legitimately logged nothing yet.

### The config knobs are not in `appsettings.json` — on purpose

`WorkerMemoryTrend` and `IngestionThroughput` have **no section in any `appsettings.json`**.
The options classes carry the ADR 0045 values as C# defaults (512 MiB; 200 jobs/min), and
binding an absent section leaves those defaults in force.

That is the intended posture, not an omission: **a cap change must be a dated ADR amendment,
never a silent config bump** (§E point 3). A knob sitting in `appsettings.json` invites exactly
the edit the ADR forbids. Add a section locally if you want to *experiment* — it binds normally
— but shipping one is an ADR 0045 decision, not a config decision.

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
select @Timestamp, @Properties['Source'] as source, @Properties['JobType'] as jobType,
       @Properties['Fetched'] as fetched, @Properties['DurationSec'] as durationSec,
       @Properties['ItemsPerMinute'] as itemsPerMinute
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
- The ingest path absorbs a duplicate key by design (ADR 0032 §5), and EF reports each one
  twice: `CommandError` in `Database.Command`, and `SaveChangesFailed` — the whole
  `DbUpdateException`, stack trace included — in a **third** category,
  `Microsoft.EntityFrameworkCore.Update`. Both are Error by default, which no `Warning` rule
  reaches, so `AddPersistence` re-levels them to Information (#1633) and this category rule
  then takes the volume.

So all three categories ship at `Warning` in both hosts' base `appsettings.json` (#752,
perf-audit finding `g2`; #1633), and all three survive into Development. Pinned by
`tests/Jobbliggaren.Architecture.Tests/EfCoreLoggingConfigurationTests.cs`, which runs the
shipped config through the real MEL filter engine and reads the re-levelling back out of
`AddPersistence`'s own `DbContextOptions`.

**A genuine failure still reaches the log at `Error` — from the application, not from EF.**
`LoggingBehavior.LogFailed` logs it with the exception on every Mediator path. What the two
re-levelled EF events add on top of that is the SQL text, and this section is how you get it
back.

### Turning it on for a session

Both hosts load a gitignored `appsettings.Local.json` last, so a measurement session needs no
committed change and cannot leak into anyone else's environment:

```jsonc
// src/Jobbliggaren.Worker/appsettings.Local.json   (or .../Jobbliggaren.Api/)
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information",
      "Microsoft.EntityFrameworkCore.Update": "Information"
    }
  }
}
```

MEL resolves a category by **longest matching prefix**, so this re-enables exactly the
per-query duration signal plus the failed-statement detail, and leaves the
`ContextInitialized` flood silenced. Drop the `Update` line if you want the SQL without the
absorbed duplicate keys — on the ingest path that is the difference between five lines per
collision and seventy-seven.

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

## §F — Frontend document weight and Core Web Vitals

The instrument is the `lighthouse (observe-only)` job in `build.yml`: 8 URLs × 3 runs,
`aggregationMethod: median`, asserting the ADR 0045 Beslut 2 budgets from
`web/jobbliggaren-web/lighthouserc.json`. It only became **verdict-giving** on 2026-07-24
(#1007 fixed collection, #1011 fixed the assert phase) — every earlier green was
green-but-empty, so 2026-07-20 is the first honest baseline in the project's history.

### Which measurements are admissible evidence

CTO bind D0 (2026-07-25), derived from ADR 0045 Beslut 2 (`numberOfRuns: 3` + median was
chosen *because* single-run timing is flaky) and Alternativ 5 (shared runners make absolute
timing thresholds inherently flaky):

- **Byte metrics** (`resource-summary:*`) are deterministic. Measuring them locally against a
  `pnpm build` + `next start` server is admissible evidence for shipping. Local numbers run
  ~1,4 KB below CI's, which counts response headers.
- **Timing metrics** (LCP/CLS/TBT) measured locally are a **hypothesis generator only**. A
  local Windows run brackets ±300 ms on the same build (measured 2026-07-25: TBT varied
  70→386 ms across runs of one build).
- **The CI job is the verdict — but it is not precise either.** Measured 2026-07-25 by
  re-running the Lighthouse job twice on the *same commit* (PR #1041, jobs 89689481966 and
  89691443403): `/cv-granskning` went 2845 → under 2500 and `/matchning` went under 2500 →
  2852. Two URLs **swapped which one failed**, on identical code, with median-of-3 already
  applied. That is a **≥350 ms run-to-run swing at the threshold**.

  So the signal floor on the CI instrument is **350 ms, not 200**, and a single run cannot
  classify a page sitting within ~350 ms of a budget. A page is red only if it is red in
  **two consecutive runs**; one red run near the threshold is a coin flip.

  This does not touch the byte metrics: `resource-summary:*` was byte-identical across both
  runs, which is what makes it the admissible evidence class.

Consequence, stated plainly so it is not re-litigated: a change whose only claim is a
sub-200 ms CI timing delta or a few KB of gzip has **no measured perf benefit**. #750 PR-2
was declined on exactly this basis (−4,3 KB gzip → −55 ms LCP ceiling = noise).

### Baseline — 2026-07-20 (#1011 CI run, job 88423824881)

| URL | document | LCP |
|---|---|---|
| `/` | 43 379 ✗ | 2642 ✗ |
| `/matchning` | 46 031 ✗ | ok |
| `/cv-granskning` | 46 355 ✗ | ok |
| `/hjalpcenter` | 40 702 ✗ | ok |
| `/for-utvecklare` | 42 157 ✗ | ok |
| `/gast/jobb` | 41 461 ✗ | 3068 ✗ |
| `/gast/oversikt` | 43 289 ✗ | 3079 ✗ |
| `/gast/cv` | 40 238 ✗ | 3226 ✗ |

Budgets: document ≤ 30 720 B, LCP ≤ 2500 ms. Everything else (script, stylesheet, font,
image, total, third-party, CLS) was green and stays green.

`document:size` was closed by the per-boundary i18n payload scoping (#737) — measured
locally at 10 216–25 021 B on the same 8 URLs, all clear of the budget with the tightest
margin on `/gast/oversikt`. The instrument of record is still the CI job.

### LCP — pre-registered decision rule

LCP is red on 4/8 with **no proven cause**. Two candidates are excluded by counterfactual,
both measured 2026-07-25:

- **Not the webfonts.** Injecting the missing `<link rel="preload" as="font">` for the two
  woff2 files moved FCP by −308 ms (`/`) and −302 ms (`/gast/cv`) but LCP by **±0**
  (2975→2988, 3337→3345). FCP is not an ADR 0045 gate.
- **Not document weight, at the ceiling.** A probe carrying only `landing`+`common`
  (document 43 → 15 KB) left `/` at 2987 vs 2975. Inside the local noise bracket, so this
  refutes nothing on its own — it only means no *large* effect is visible locally.

The LCP element is server-rendered text (`p.jp-land-hero__lede--plate`, verified present in
the SSR HTML) with Render Delay at 83–85 % of LCP and TTFB ~460 ms. Main-thread profile on
`/`: Style & Layout 427 ms, Parse HTML & CSS 180 ms, Script Evaluation 662 ms, plus long
tasks attributed to the document itself (206 + 103 + 68 ms — the inline Flight payload).

**Read the next CI Lighthouse run against the baseline above and apply this rule as
written. Do not re-derive it after seeing the number** (that is how a measurement becomes a
rationalisation):

- **LCP ≤ 2500 on all 8** → both budgets met. Record it and close the LCP track.
- **LCP improves ≥ 350 ms on the red URLs but stays > 2500** → the payload/hydration
  hypothesis is supported. Next lever: client JS on the critical path, starting from the
  audit finding `b3-no-dynamic-imports-modals`, scoped to the red URLs. One measured
  intervention, its own PR.
- **LCP flat (< 350 ms)** → document weight is not the cause. The next step is
  **attribution, not a fix**: pull Lighthouse's own LCP phase breakdown (TTFB / load delay /
  load time / render delay) per URL from the CI artifact plus `next build`'s per-route First
  Load JS, and test the standing hypothesis — *the red URLs are exactly the ones with a
  real client shell (`/` and the three `/gast/*`); the green ones are static
  marketing-inner content* — which points at hydration cost.

In no branch does a speculative LCP fix ship. Note also that fixing one budget is **not**
the ratchet condition for making this job blocking (Beslut 6 needs a stable distribution
plus Klas-GO) — and the ≥350 ms swing measured above is itself an argument that LCP is not
ready to be a blocking gate on this runner at all.

### Outcome — branch (iii), resolved 2026-07-25 on PR #1041

`document:size` is **closed**: absent from the failure list in both runs, 8/8 URLs, and the
byte counts were identical between them.

LCP took branch **(iii), flat**: the apparent +150…+225 ms against the 5-day-old baseline is
smaller than the instrument's own ≥350 ms swing, so it is not attributable to the payload
change — and there is no mechanism by which removing ~25 KB of inline JSON from a document
delays its paint (`NextIntlClientProvider` renders no DOM node; the diff's `className` lines
are pure re-indentation).

Classifying the eight URLs by the two-consecutive-runs rule:

| URL | run 1 | run 2 | verdict |
|---|---|---|---|
| `/gast/jobb` | 3250 | 3248 | **red — real** |
| `/gast/oversikt` | 3229 | 3241 | **red — real** |
| `/gast/cv` | 3227 | 3219 | **red — real** |
| `/` | 2867 | 2886 | **red — real** |
| `/hjalpcenter` | 2697 | 2722 | **red — real** |
| `/cv-granskning` | 2845 | ≤2500 | borderline — one of each |
| `/matchning` | ≤2500 | 2852 | borderline — one of each |
| `/for-utvecklare` | ≤2500 | ≤2500 | green |

Five URLs are genuinely over budget; two sit inside the noise band and cannot be classified
without more runs. The five real ones split cleanly into *pages with a client shell*
(`/` + the three `/gast/*`) and one static marketing page (`/hjalpcenter`), which is a
partial counterexample to the hydration hypothesis and should be the first thing the
attribution step explains.

### LCP attribution — done 2026-07-25 from the CI artifact (#1048), branch (iii) discharged

The pre-registered flat branch says *attribution, not a fix*, and names the artifact to read.
That artifact did not exist until #1048: `.lighthouseci` is a dot-directory and
`actions/upload-artifact` skips hidden files by default, so every report lhci wrote was
dropped and `if-no-files-found: ignore` kept it quiet. The numbers below are the first ones
ever read out of the instrument's own output — 24 LHR JSONs, 8 URLs × 3 runs, from run
30167162198. **Median run per URL:**

| URL | LCP | FCP | TTFB | Load Delay | Load Time | Render Delay | Rnd % | LCP element |
|---|---|---|---|---|---|---|---|---|
| `/for-utvecklare` | 2277 | 1068 | 459 | 0 | 0 | 1818 | 80 % | server-rendered text |
| `/hjalpcenter` | 2278 | 1068 | 459 | 0 | 0 | 1818 | 80 % | server-rendered text |
| `/matchning` | 2842 | 1215 | 457 | 0 | 0 | 2385 | 84 % | server-rendered text |
| `/cv-granskning` | 2868 | 1214 | 457 | 0 | 0 | 2411 | 84 % | server-rendered text |
| `/` | 2989 | 1214 | 457 | 0 | 0 | 2532 | 85 % | server-rendered text |
| `/gast/oversikt` | 3212 | 1218 | 459 | 0 | 0 | 2753 | 86 % | **client-only dialog** |
| `/gast/cv` | 3218 | 1220 | 460 | 0 | 0 | 2758 | 86 % | **client-only dialog** |
| `/gast/jobb` | 3240 | 1217 | 459 | 0 | 0 | 2782 | 86 % | **client-only dialog** |

**1. The hydration hypothesis as stated is refuted, and its counterexample dissolves.** The
standing hypothesis was *"the red URLs are exactly the ones with a real client shell; the green
ones are static marketing-inner content"*, with `/hjalpcenter` flagged as the partial
counterexample to explain. In this run `/hjalpcenter` and `/for-utvecklare` land **1 ms apart**
(2278 vs 2277) with identical phase profiles. They also ship byte-identical client JS
(211 663 B gzip, 13 files) and the *green* page carries the *larger* document (7 905 vs
6 989 B gzip). They were never two populations. The earlier classification put them on
opposite sides of a line that runs through the instrument's own noise band — "red in two
consecutive runs" and "distinguishable from the green page" are different claims, and only the
first was ever established.

**2. What survives, with a mechanism rather than a correlation.** The three `/gast/*` URLs sit
350–960 ms above everything else *and* have the tightest run-to-run spread in the set (16–160 ms
against 441–463 ms for the marketing pages). They are the only per-page difference that clears
the ≥350 ms floor. They are also the only three whose LCP element is **client-only**: the
description paragraph of the guest welcome modal,
`body > div#radix-… > div.flex > p#radix-…[data-slot="dialog-description"]`.

`GuestWelcomeModal` is mounted from `(guest)/gast/layout.tsx` with `showWelcome={!welcomed}`,
`welcomed` being the `GUEST_WELCOMED_COOKIE`. Lighthouse runs a fresh profile every time, so
the modal opens on every run. Radix renders dialog content into a client portal, and the served
`/gast/oversikt` document contains **zero** `data-slot="dialog-description"` and zero `radix-`
ids — so the element Lighthouse credits as LCP cannot exist until React has hydrated. That is
hydration cost, but sharper than the original hypothesis: not *"this page class is slower"* but
*"on these three pages the LCP element does not exist before hydration"*. Tracked as **#1052**,
which separates the product question from the instrument question rather than answering either.

**3. No resource is on the LCP path anywhere.** `Load Delay` and `Load Time` are **0 on all
eight URLs** — the LCP element is text on every page in the set. Image and font levers are
therefore structurally inert against LCP, which is an independent confirmation of the manual
font-preload counterfactual's LCP ±0 recorded further down. Render Delay is 80–86 % everywhere
and TTFB is a uniform 457–460 ms.

**4. A trap for whoever reads these files next.** Take the median per metric, never the median
run's other metrics. TBT on `/` reads 83 / 93 / **1883** ms across its three runs, and
`/gast/oversikt` reads 88 / 104 / **1806** — single-run runner spikes. Reporting the
median-LCP run's TBT would have produced a landing-page main-thread finding that does not
exist. Median TBT is 63–140 ms on every URL, inside the 200 ms warn threshold, and CLS is 0
everywhere (0,005 on `/`).

**Branch (iii) is discharged.** No speculative fix shipped, per the rule.

### Refuted 2026-07-25 — the missing font preload is NOT the `--webpack` opt-out

**Read this before the table below it.** The diagnosis recorded here earlier — that the
webpack path loses `next/font`'s preload emission and costs ~300 ms FCP per page — **does not
reproduce**. Re-measured on `6129c80b` (#1046), building the same tree both ways and serving
each with `next start`:

| | `next build --webpack` | `next build` (Turbopack) |
|---|---|---|
| Flight font hints in `/` | **2** | **2** |
| `<link rel="preload" as="font">` | **0** | **0** |
| `</head>` ends at byte | 3 159 | 3 159 |
| First `woff2` reference at byte | 22 872 (after `</head>`) | 22 869 (after `</head>`) |
| Document `/` gzip | 17 445 B | 17 430 B |
| Delivered JS `/` gzip | 225 095 B (14 files) | 225 056 B (14 files) |

The two documents are structurally identical. The ~300 ms does not exist.

**The method note is the most useful line in this section.** The earlier "0 hints" for webpack
is a **regex artefact**. The hints sit as escaped JSON inside the Flight stream —
`:HL[\"…woff2\",\"font\",{…}]` — so a probe written as `"font",{` or `:HL\[[^\]]*"font"`
matches nothing and returns 0 on a document that contains two of them. This session hit the
same trap first and read 0 before matching the escaped form. Anyone re-testing this must probe
the escaped shape, or they will reproduce the wrong answer and "confirm" the dead claim.

**The structural cause, which no bundler changes.** No application route is statically
prerendered — only `/icon.svg`, `/robots.txt` and `/sitemap.xml` are `○` in the build output.
Every page is server-rendered on demand, so the font hints ride the Flight stream and land
~30 % into the document under **any** bundler. There is no build-time `<head>` for a preload
link to be written into.

**Consequences for the font lever, which replace filing an issue** (CTO bind D4, 2026-07-25):

- The manual-preload counterfactual measured −308 ms (`/`) and −302 ms (`/gast/cv`) FCP with
  LCP **±0**. FCP is not an ADR 0045 gate (Beslut 2 gates LCP/CLS/INP/page weight), and −308 ms
  from the local hypothesis-generator class sits under the CI instrument's own ≥350 ms floor.
- The lever is therefore inert against every budget we gate on, and #749's `preload: false`
  candidate is inert with it — **under both bundlers**, because the cause is the absent static
  prerender, not the bundler.
- Revisiting it is conditional on a prerendering strategy (PPR / static shell) landing, which
  is an ADR-weight rendering decision. A future session that lands one finds this measurement
  waiting rather than rediscovering it.

**The opt-out's rationale was recorded all along** — in the commit that added it, `63ea6683`
(2026-05-14): *"Turbopack-output bryter Vercel-routing"*, with `X-Vercel-Error: NOT_FOUND` on
every route. A `git log -S` scoped to `web/jobbliggaren-web/package.json` cannot see it, because
the file was `web/jobbpilot-web/package.json` before the ADR 0069 rename; `--follow` finds it.
The claim "no rationale is recorded anywhere", below, was wrong for that reason. The flag was
removed in #1046 on that basis: its cause is a platform this repo no longer builds on.

### Not reproduced — the original 2026-07-25 diagnosis, kept as a record

**Everything from here to the end of §F is superseded by the section above.** It is retained
rather than deleted so the refutation stays attached to the claim; the numbers below must not
be read as current.

**Diagnosed 2026-07-25, deterministically.** Building the same tree twice, once with the
committed `next build --webpack` and once with Next 16's default (Turbopack), and diffing the
emitted document:

| Build | React Flight font hints | `rel="preload" as="font"` | `/` FCP (median-of-3) |
|---|---|---|---|
| `next build --webpack` (committed) | **0** | 0 | 1516 ms |
| `next build` (Turbopack, the default) | **4** — `:HL[…woff2","font",{crossOrigin,type}]` | emitted from the hints | **1212 ms** |

Document weight is unchanged between them (21 437 vs 21 269 B gzip on `/`), so this is not a
trade — the webpack path simply **loses `next/font`'s preload emission**. The −304 ms on `/`
and −302 ms on `/gast/cv` match the manual-preload counterfactual exactly, which is what
confirms the mechanism rather than a coincidence.

**No rationale for the opt-out is recorded anywhere** — no ADR, no BUILD.md entry, no comment;
`--webpack` sits in both `dev` and `build` in `package.json`. It is plausibly a leftover from
the Next 15→16 migration, but that is a guess and should not be treated as one of the facts
above.

**This is a build-toolchain decision (BUILD.md §3.1), not a perf-lane change**, so nothing was
flipped here: dropping the flag changes how every route is compiled, and the cost of whatever
made someone add it is unknown. What is now known is its price — roughly 300 ms of first paint
on every page. FCP is not an ADR 0045 gate (Beslut 2 lists LCP/CLS/INP/page weight), so this
does not breach a budget; it is user-visible time that a flag with no written reason is
spending.

Related and still not authorised here: #749's follow-up candidate (`preload: false` on
JetBrains Mono, ~31 KB off first paint) assumed preload links exist. On the webpack path they
do not, so that lever is inert until the toolchain question is settled.

### Original observation (superseded by the diagnosis above)

Next 16.2.9's own docs (`node_modules/next/dist/docs/01-app/03-api-reference/02-components/font.md`,
lines 148 and 1050) state that a font declared with `subsets` in the **root layout** is
preloaded on all routes. Measured 2026-07-25 on the committed build: the landing document
contains **zero** `woff2` references and no `rel="preload" as="font"` — the two font files are
discovered only after the render-blocking CSS is parsed. Next's own `.p.` filename marker is
present on exactly the two files the browser fetches, so the loader identifies them; the
emission was what went missing. The section above explains why.

No local workaround is in the tree, deliberately: the only expressible form is the hashed
filename, which changes with font config (CLAUDE.md §5 magic strings) and would linger as a
duplicate hint once the real cause is addressed.
